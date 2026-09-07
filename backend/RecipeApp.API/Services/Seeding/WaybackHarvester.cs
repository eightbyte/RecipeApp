using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stages 1, 2 and 2b — discovery and caching of USDA MyPlate recipe pages from the Internet
/// Archive (Phase 9 §8–§9).
///
/// <para>The live site was retired in January 2026, so the archive is the only remaining channel.
/// Its content is the USDA's own public-domain work, but the archive itself is a shared,
/// rate-limited service: requests are strictly sequential, spaced by
/// <see cref="RecipeSeedingOptions.FetchDelayMilliseconds"/>, and backed off on 429/5xx.
/// Parallelising this would earn a block, not a speed-up.</para>
///
/// <para>Not thread-safe by design — it drives one sequential harvest at a time, from the CLI.</para>
/// </summary>
public class WaybackHarvester(
    IHttpClientFactory httpClientFactory,
    IOptions<RecipeSeedingOptions> options,
    SeedCacheStore cache,
    ILogger<WaybackHarvester> logger) : IWaybackHarvester
{
    /// <summary>Named <see cref="HttpClient"/> configured with the archive's timeout and user agent.</summary>
    public const string HttpClientName = "RecipeSeeder";

    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>Wayback capture stamps are fixed-width <c>yyyyMMddHHmmss</c>, so they sort chronologically as text.</summary>
    private static readonly Regex TimestampPattern = new(@"^\d{14}$", RegexOptions.Compiled);

    /// <summary>Calorie-filter artefacts such as <c>/recipes/111.57</c>, which are not recipes.</summary>
    private static readonly Regex NumericSlugPattern = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>Escaped line breaks that leaked into a handful of archived URLs.</summary>
    private static readonly string[] MalformedUrlMarkers = ["%5Cr%5Cn", "%5cr%5cn", ".While"];

    /// <summary>When the next archive request may be sent, honouring the politeness delay.</summary>
    private DateTime _nextRequestAllowedAt = DateTime.MinValue;

    // ── Stage 1 — discovery ───────────────────────────────────────────────────

    public async Task<SeedDiscoveryResult> DiscoverAsync(CancellationToken ct = default)
    {
        var rows = await QueryCdxAsync(ct);

        var dropped   = new Dictionary<string, int>(StringComparer.Ordinal);
        var bySlug    = new Dictionary<string, CdxRow>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!TimestampPattern.IsMatch(row.Timestamp))
            {
                Drop(dropped, DiscoveryDropReason.InvalidTimestamp);
                continue;
            }

            var slug = TryExtractSlug(row.OriginalUrl, _options.RecipePathPrefix, out var reason);
            if (slug is null)
            {
                Drop(dropped, reason!);
                continue;
            }

            // Keep whichever capture the preference rules favour; the losing row is a duplicate
            // of the same recipe (a www./non-www. host or an earlier snapshot), not a drop.
            bySlug[slug] = bySlug.TryGetValue(slug, out var existing)
                ? PreferredSnapshot(existing, row, _options.PreferredSnapshotYear)
                : row;
        }

        var manifest = new SeedManifest
        {
            HarvestedAt   = DateTime.UtcNow,
            SourcePattern = _options.SourceUrlPattern,
            Recipes = [.. bySlug
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new SeedManifestEntry(pair.Key, pair.Value.Timestamp, pair.Value.OriginalUrl))],
        };

        if (manifest.Recipes.Count < _options.MinimumDiscoveredSlugs)
            throw new SeedHarvestException(
                $"Discovery yielded {manifest.Recipes.Count} recipe slugs from {rows.Count} CDX rows, " +
                $"below the minimum of {_options.MinimumDiscoveredSlugs}. This indicates the filter " +
                "rules have drifted against a changed archive rather than a genuinely small corpus. " +
                $"Dropped rows by reason: {DescribeDrops(dropped)}.");

        await cache.SaveManifestAsync(manifest, ct);

        var state = await cache.LoadStateAsync(ct);
        foreach (var entry in manifest.Recipes)
            state.GetOrAdd(entry.Slug);
        await cache.SaveStateAsync(ct);

        logger.LogInformation(
            "Discovered {SlugCount} recipe slugs from {RowCount} CDX rows. Dropped: {Dropped}.",
            manifest.Recipes.Count, rows.Count, DescribeDrops(dropped));

        return new SeedDiscoveryResult(manifest, rows.Count, dropped);
    }

    public async Task<SeedManifest> EnsureManifestAsync(CancellationToken ct = default)
    {
        var cached = await cache.TryLoadManifestAsync(ct);
        if (cached is not null && cached.Recipes.Count > 0)
        {
            logger.LogInformation(
                "Using cached manifest of {Count} recipes discovered {HarvestedAt:u}.",
                cached.Recipes.Count, cached.HarvestedAt);
            return cached;
        }

        return (await DiscoverAsync(ct)).Manifest;
    }

    // ── Stage 1 — CDX query ───────────────────────────────────────────────────

    private async Task<IReadOnlyList<CdxRow>> QueryCdxAsync(CancellationToken ct)
    {
        // Deliberately *not* collapsed by urlkey. CDX collapse keeps the first capture of each
        // key, which would hand back one arbitrary early snapshot per recipe and make the
        // preferred-year rule below inert — measured against the live archive, collapsing pins
        // 1,086 of 1,123 recipes to a 2024 capture, while the uncollapsed set resolves 1,121 of
        // them to the 2025 captures taken shortly before the site was retired. Same slugs either
        // way; this is purely about getting the last published version of each page. The cost is
        // a ~5 MB response, paid once.
        var requestUrl =
            $"{_options.WaybackCdxUrl}?url={Uri.EscapeDataString(_options.SourceUrlPattern)}" +
            "&output=json&fl=original,timestamp,statuscode&filter=statuscode:200";

        logger.LogInformation("Querying the Wayback CDX index: {Url}", requestUrl);

        // The index query is one large response rather than a page fetch, so it gets its own,
        // longer budget instead of the per-page timeout.
        var outcome = await SendWithRetriesAsync(
            requestUrl, ct, TimeSpan.FromSeconds(_options.DiscoveryTimeoutSeconds));
        if (!outcome.Succeeded)
            throw new SeedHarvestException(
                $"The Wayback CDX index could not be reached ({outcome.Error}). " +
                "No manifest was written.");

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(outcome.Content!);
        }
        catch (JsonException ex)
        {
            throw new SeedHarvestException(
                "The Wayback CDX index returned a response that is not valid JSON.", ex);
        }

        if (payload is not JsonArray table || table.Count < 2)
            throw new SeedHarvestException(
                "The Wayback CDX index returned no captures for " +
                $"'{_options.SourceUrlPattern}'. The archive or the URL pattern has changed.");

        // The first row names the columns. Reading them by name rather than by position keeps
        // discovery working if the archive ever reorders its output.
        var columns = (table[0] as JsonArray)?
            .Select((column, index) => (Name: column?.GetValue<string>() ?? string.Empty, Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);

        if (columns is null
            || !columns.TryGetValue("original", out var originalIndex)
            || !columns.TryGetValue("timestamp", out var timestampIndex))
            throw new SeedHarvestException(
                "The Wayback CDX index response is missing the 'original' or 'timestamp' column.");

        var rows = new List<CdxRow>(table.Count - 1);
        foreach (var row in table.Skip(1))
        {
            if (row is not JsonArray fields) continue;
            if (fields.Count <= originalIndex || fields.Count <= timestampIndex) continue;

            var original  = fields[originalIndex]?.GetValue<string>();
            var timestamp = fields[timestampIndex]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(timestamp))
                rows.Add(new CdxRow(original, timestamp));
        }

        return rows;
    }

    // ── Stage 1 — filter rules (§8) ───────────────────────────────────────────

    /// <summary>
    /// Reduces an archived URL to its canonical recipe slug, or returns null with the rule that
    /// rejected it. Host case and a leading <c>www.</c> are collapsed so the two spellings of the
    /// same recipe key together.
    /// </summary>
    internal static string? TryExtractSlug(string originalUrl, string recipePathPrefix, out string? dropReason)
    {
        dropReason = null;

        if (MalformedUrlMarkers.Any(marker => originalUrl.Contains(marker, StringComparison.Ordinal)))
        {
            dropReason = DiscoveryDropReason.MalformedUrl;
            return null;
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
        {
            dropReason = DiscoveryDropReason.MalformedUrl;
            return null;
        }

        // Pagination and tracking variants (?page=1, ?utm_source=…) are duplicates of the page
        // they hang off, and archive to a different urlkey.
        if (!string.IsNullOrEmpty(uri.Query))
        {
            dropReason = DiscoveryDropReason.QueryString;
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.StartsWith(recipePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            dropReason = DiscoveryDropReason.NonRecipePath;
            return null;
        }

        var remainder = path[recipePathPrefix.Length..];
        if (remainder.Length == 0 || remainder.Contains('/'))
        {
            dropReason = DiscoveryDropReason.NonRecipePath;
            return null;
        }

        var slug = Uri.UnescapeDataString(remainder).ToLowerInvariant();

        if (NumericSlugPattern.IsMatch(slug))
        {
            dropReason = DiscoveryDropReason.NumericSlug;
            return null;
        }

        if (!SeedCacheStore.IsValidSlug(slug))
        {
            dropReason = DiscoveryDropReason.UnusableSlug;
            return null;
        }

        return slug;
    }

    /// <summary>
    /// Picks between two captures of the same recipe: the latest snapshot inside the preferred
    /// year, and failing that the latest overall. Timestamps are fixed-width, so text comparison
    /// is chronological comparison.
    /// </summary>
    internal static CdxRow PreferredSnapshot(CdxRow first, CdxRow second, int preferredYear)
    {
        var firstPreferred  = IsFromYear(first.Timestamp, preferredYear);
        var secondPreferred = IsFromYear(second.Timestamp, preferredYear);

        if (firstPreferred != secondPreferred)
            return firstPreferred ? first : second;

        return string.CompareOrdinal(first.Timestamp, second.Timestamp) >= 0 ? first : second;
    }

    private static bool IsFromYear(string timestamp, int year) =>
        timestamp.AsSpan(0, 4).SequenceEqual(year.ToString().AsSpan());

    // ── Stage 2 — fetch and cache ─────────────────────────────────────────────

    public async Task<SeedHarvestResult> HarvestAsync(
        SeedManifest manifest, int? limit = null, CancellationToken ct = default)
    {
        cache.EnsureDirectories();

        var state    = await cache.LoadStateAsync(ct);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);

        int fetched = 0, skipped = 0, failed = 0;
        int imagesFetched = 0, imagesSkipped = 0, imagesFailed = 0;
        var position = 0;

        foreach (var entry in manifest.Recipes)
        {
            ct.ThrowIfCancellationRequested();
            position++;

            if (limit is { } maximum && fetched + failed >= maximum)
            {
                logger.LogInformation("Reached the requested limit of {Limit} pages.", maximum);
                break;
            }

            var pageAlreadyCached = cache.HasRaw(entry.Slug);
            if (pageAlreadyCached)
            {
                skipped++;
                // The cached file is the authority: a state entry that lags behind it (an
                // interrupted flush, a hand-copied cache) is corrected rather than trusted.
                if (!state.GetOrAdd(entry.Slug).HasReached(SeedStage.Fetched))
                    await cache.RecordStageAsync(entry.Slug, SeedStage.Fetched, error: null, ct);
            }
            else
            {
                var elapsed = Stopwatch.StartNew();
                var outcome = await SendWithRetriesAsync(SnapshotUrl(entry), ct);

                if (!outcome.Succeeded)
                {
                    failed++;
                    failures[entry.Slug] = outcome.Error!;
                    await cache.RecordAttemptAsync(entry.Slug, ct);
                    await cache.RecordStageAsync(entry.Slug, SeedStage.Failed, outcome.Error, ct);

                    logger.LogWarning("[{Position}/{Total}] {Slug} → failed ({Error})",
                        position, manifest.Recipes.Count, entry.Slug, outcome.Error);
                    continue;
                }

                await cache.RecordAttemptAsync(entry.Slug, ct);
                await cache.WriteRawAsync(entry.Slug, outcome.Content!, ct);
                await cache.RecordStageAsync(entry.Slug, SeedStage.Fetched, error: null, ct);
                fetched++;

                logger.LogInformation("[{Position}/{Total}] {Slug} → fetched ({Elapsed:0.0}s)",
                    position, manifest.Recipes.Count, entry.Slug, elapsed.Elapsed.TotalSeconds);
            }

            if (!_options.DownloadImages) continue;

            switch (await HarvestImageAsync(entry, ct))
            {
                case ImageOutcome.Fetched: imagesFetched++; break;
                case ImageOutcome.Skipped: imagesSkipped++; break;
                case ImageOutcome.Failed:  imagesFailed++;  break;
            }
        }

        return new SeedHarvestResult
        {
            PagesFetched  = fetched,
            PagesSkipped  = skipped,
            PagesFailed   = failed,
            ImagesFetched = imagesFetched,
            ImagesSkipped = imagesSkipped,
            ImagesFailed  = imagesFailed,
            Failures      = failures,
        };
    }

    private string SnapshotUrl(SeedManifestEntry entry) =>
        $"{_options.WaybackContentUrl.TrimEnd('/')}/{entry.Timestamp}/{entry.OriginalUrl}";

    // ── Stage 2b — images ─────────────────────────────────────────────────────

    private enum ImageOutcome { Fetched, Skipped, Failed }

    /// <summary>
    /// Downloads the recipe photo referenced by the page's JSON-LD. Always non-fatal: a recipe
    /// without a photo still imports, so a failure here is counted and moved past.
    /// </summary>
    private async Task<ImageOutcome> HarvestImageAsync(SeedManifestEntry entry, CancellationToken ct)
    {
        if (cache.HasImage(entry.Slug)) return ImageOutcome.Skipped;

        string html;
        try
        {
            html = await cache.ReadRawAsync(entry.Slug, ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read the cached page for {Slug} to locate its image.", entry.Slug);
            return ImageOutcome.Failed;
        }

        var imageUrl = TryExtractImageUrl(html);
        if (imageUrl is null)
        {
            logger.LogDebug("{Slug} has no JSON-LD image URL.", entry.Slug);
            return ImageOutcome.Skipped;
        }

        var outcome = await SendWithRetriesAsync(ArchivedImageUrl(imageUrl, entry.Timestamp), ct);
        if (!outcome.Succeeded)
        {
            logger.LogWarning("{Slug} image could not be harvested ({Error}). Importing without a photo.",
                entry.Slug, outcome.Error);
            return ImageOutcome.Failed;
        }

        var extension = ResolveImageExtension(outcome.Content!, outcome.ContentType, imageUrl);
        if (extension is null)
        {
            // The archive serves its own interstitial and error pages with a 200, so a successful
            // status is not evidence that the bytes are a photograph.
            logger.LogWarning(
                "{Slug} image response was {ContentType}, not a usable image. Importing without a photo.",
                entry.Slug, outcome.ContentType ?? "of unknown type");
            return ImageOutcome.Failed;
        }

        await cache.WriteImageAsync(entry.Slug, outcome.Content!, extension, ct);
        return ImageOutcome.Fetched;
    }

    /// <summary>Reads <c>image.url</c> from the page's schema.org Recipe block, in any of the shapes it takes.</summary>
    internal static string? TryExtractImageUrl(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var recipe   = RecipeJsonLdExtractor.TryFindRecipeNode(document);

        return recipe?["image"] switch
        {
            JsonValue value when value.TryGetValue<string>(out var url) => url,
            JsonObject image => image["url"]?.GetValue<string>(),
            JsonArray images => images
                .Select(image => image switch
                {
                    JsonValue value when value.TryGetValue<string>(out var url) => url,
                    JsonObject candidate => candidate["url"]?.GetValue<string>(),
                    _ => null,
                })
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
            _ => null,
        };
    }

    /// <summary>Matches the archive's <c>/web/{timestamp}{modifier}/</c> snapshot prefix.</summary>
    private static readonly Regex SnapshotPrefixPattern =
        new(@"/web/(?<timestamp>\d{14})(?<modifier>[a-z_]*)/", RegexOptions.Compiled);

    /// <summary>
    /// Rewrites an archived image reference to the <c>im_</c> snapshot form, which serves the
    /// captured bytes directly. Without the modifier the archive redirects to its HTML viewer and
    /// the "image" downloads as a page — the original image host was decommissioned with the
    /// site, so there is no unwrapped URL to fall back to.
    /// </summary>
    private string ArchivedImageUrl(string imageUrl, string timestamp)
    {
        var match = SnapshotPrefixPattern.Match(imageUrl);

        return match.Success
            ? SnapshotPrefixPattern.Replace(
                imageUrl, $"/web/{match.Groups["timestamp"].Value}im_/", count: 1)
            : $"{_options.WaybackContentUrl.TrimEnd('/')}/{timestamp}im_/{imageUrl}";
    }

    /// <summary>
    /// The extension to store a harvested image under, or null when the payload is not a usable
    /// image at all. The bytes decide first: a remote path and a remote header are both weaker
    /// evidence than the file's own signature.
    /// </summary>
    private string? ResolveImageExtension(byte[] content, string? contentType, string imageUrl)
    {
        if (DetectImageExtension(content) is { } detected)
            return _options.ImageExtensions.Contains(detected) ? detected : null;

        // An unrecognised signature is only trusted as an image if the server said so, and even
        // then the extension comes from the allow-list rather than from the remote path.
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        var path = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : imageUrl;
        var fromUrl = Path.GetExtension(path).ToLowerInvariant();

        return _options.ImageExtensions.Contains(fromUrl) ? fromUrl : _options.DefaultImageExtension;
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The file extension implied by a payload's own magic bytes, or null if unrecognised.</summary>
    internal static string? DetectImageExtension(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return ".jpg";

        if (content.Length >= 8 && content[..8].SequenceEqual(PngSignature))
            return ".png";

        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content[8..12].SequenceEqual("WEBP"u8))
            return ".webp";

        if (content.Length >= 6 && content[..4].SequenceEqual("GIF8"u8))
            return ".gif";

        return null;
    }

    // ── HTTP with politeness, retries and backoff ─────────────────────────────

    private sealed record FetchOutcome(byte[]? Content, string? ContentType, string? Error)
    {
        public bool Succeeded => Content is not null;

        public static FetchOutcome Ok(byte[] content, string? contentType) =>
            new(content, contentType, null);

        public static FetchOutcome Failure(string error) => new(null, null, error);
    }

    /// <summary>
    /// Fetches one archive URL, waiting out the politeness delay first and retrying transient
    /// failures with exponential backoff. Returns a failure rather than throwing: one bad page
    /// must never abort a thousand-page harvest.
    /// </summary>
    private async Task<FetchOutcome> SendWithRetriesAsync(
        string url, CancellationToken ct, TimeSpan? timeout = null)
    {
        var client        = httpClientFactory.CreateClient(HttpClientName);
        var totalAttempts = _options.MaxFetchRetries + 1;

        // The factory hands back a fresh HttpClient over a pooled handler, so raising the budget
        // for a single long request affects nothing else.
        if (timeout is { } budget) client.Timeout = budget;

        var effectiveTimeout = timeout ?? client.Timeout;

        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            await WaitForPolitenessWindowAsync(ct);

            try
            {
                using var response = await client.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                    return FetchOutcome.Ok(
                        await response.Content.ReadAsByteArrayAsync(ct),
                        response.Content.Headers.ContentType?.MediaType);

                if (!IsTransient(response.StatusCode) || attempt == totalAttempts)
                    return FetchOutcome.Failure($"HTTP {(int)response.StatusCode} after {attempt} attempt(s)");

                await BackOffAsync(attempt, RetryAfter(response), url, $"HTTP {(int)response.StatusCode}", ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation that the caller did not ask for.
                if (attempt == totalAttempts)
                    return FetchOutcome.Failure(
                        $"timed out after {attempt} attempt(s) at {effectiveTimeout.TotalSeconds:0}s each");

                await BackOffAsync(attempt, retryAfter: null, url, "timeout", ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == totalAttempts)
                    return FetchOutcome.Failure($"{ex.Message} after {attempt} attempt(s)");

                await BackOffAsync(attempt, retryAfter: null, url, ex.Message, ct);
            }
        }

        return FetchOutcome.Failure($"exhausted {totalAttempts} attempt(s)");
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    private async Task BackOffAsync(
        int attempt, TimeSpan? retryAfter, string url, string reason, CancellationToken ct)
    {
        // The archive's own Retry-After wins when it sends one — it knows its load better than
        // a doubling schedule does.
        var backoff = retryAfter is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromMilliseconds(_options.RetryBackoffMilliseconds * Math.Pow(2, attempt - 1));

        logger.LogWarning(
            "Archive request failed ({Reason}) for {Url}. Retrying attempt {Next} in {Backoff:0.0}s.",
            reason, url, attempt + 1, backoff.TotalSeconds);

        if (backoff > TimeSpan.Zero)
            await Task.Delay(backoff, ct);
    }

    private async Task WaitForPolitenessWindowAsync(CancellationToken ct)
    {
        var wait = _nextRequestAllowedAt - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);

        _nextRequestAllowedAt =
            DateTime.UtcNow.AddMilliseconds(_options.FetchDelayMilliseconds);
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    private static void Drop(Dictionary<string, int> dropped, string reason) =>
        dropped[reason] = dropped.TryGetValue(reason, out var count) ? count + 1 : 1;

    private static string DescribeDrops(IReadOnlyDictionary<string, int> dropped) =>
        dropped.Count == 0
            ? "none"
            : string.Join(", ", dropped.OrderByDescending(pair => pair.Value)
                                       .Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>One capture row from the CDX index.</summary>
    internal record CdxRow(string OriginalUrl, string Timestamp);
}

/// <summary>Why a CDX row was excluded from the manifest. Reported as a breakdown after discovery.</summary>
internal static class DiscoveryDropReason
{
    public const string QueryString      = "queryString";
    public const string NumericSlug      = "numericSlug";
    public const string NonRecipePath    = "nonRecipePath";
    public const string MalformedUrl     = "malformedUrl";
    public const string UnusableSlug     = "unusableSlug";
    public const string InvalidTimestamp = "invalidTimestamp";
}
