using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stages 1, 2 and 2b (Phase 9 §8–§9): CDX discovery, the recipe-page filter rules, snapshot
/// selection, and the resumable fetch loop.
///
/// <para>Every archive call is stubbed. CI must never depend on the Wayback Machine being up, and
/// the retry and politeness paths are only testable with time-free delays anyway.</para>
/// </summary>
public class WaybackHarvesterTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "recipeapp-harvest-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Politeness delays, retry backoff and the discovery floor are neutralised by default, so
    /// tests assert on behaviour rather than on waiting.
    /// </summary>
    private static RecipeSeedingOptions TestOptions(
        bool downloadImages = false,
        int maxFetchRetries = 3,
        int minimumDiscoveredSlugs = 0) => new()
    {
        FetchDelayMilliseconds   = 0,
        RetryBackoffMilliseconds = 0,
        MinimumDiscoveredSlugs   = minimumDiscoveredSlugs,
        MaxFetchRetries          = maxFetchRetries,
        DownloadImages           = downloadImages,
    };

    private SeedCacheStore BuildCache(RecipeSeedingOptions options) =>
        new(Options.Create(options), new TestHostEnvironment(_contentRoot),
            NullLogger<SeedCacheStore>.Instance);

    private (WaybackHarvester Harvester, SeedCacheStore Cache) Build(
        StubHttpClientFactory http, RecipeSeedingOptions options)
    {
        var cache = BuildCache(options);
        return (new WaybackHarvester(http, Options.Create(options), cache,
                    NullLogger<WaybackHarvester>.Instance),
                cache);
    }

    private static string CdxResponse(params (string Original, string Timestamp)[] rows)
    {
        var body = new StringBuilder("[[\"original\",\"timestamp\",\"statuscode\"]");
        foreach (var (original, timestamp) in rows)
            body.Append($",[\"{original}\",\"{timestamp}\",\"200\"]");
        return body.Append(']').ToString();
    }

    // ── Stage 1: filter rules (§8) ────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.myplate.gov/recipes/20-minute-chicken-creole", "20-minute-chicken-creole")]
    [InlineData("https://myplate.gov/recipes/3-can-chili", "3-can-chili")]
    [InlineData("http://www.myplate.gov/recipes/apple-oatmeal-bars/", "apple-oatmeal-bars")]
    [InlineData("https://www.myplate.gov/recipes/Apple-Oatmeal-Bars", "apple-oatmeal-bars")]
    public void TryExtractSlug_AcceptsRecipePages(string url, string expectedSlug)
    {
        var slug = WaybackHarvester.TryExtractSlug(url, "/recipes/", out var dropReason);

        slug.Should().Be(expectedSlug);
        dropReason.Should().BeNull();
    }

    [Theory]
    // Pagination and tracking variants of a page already captured without a query string
    [InlineData("https://www.myplate.gov/recipes/3-can-chili?page=1")]
    [InlineData("https://www.myplate.gov/recipes/3-can-chili?ajax_form=1")]
    [InlineData("https://www.myplate.gov/recipes/3-can-chili?utm_source=newsletter")]
    // Calorie-filter artefacts, not recipes
    [InlineData("https://www.myplate.gov/recipes/111.57")]
    [InlineData("https://www.myplate.gov/recipes/23")]
    // Index and sibling paths the CDX wildcard also returns
    [InlineData("https://www.myplate.gov/recipes")]
    [InlineData("https://www.myplate.gov/recipes/")]
    [InlineData("https://www.myplate.gov/recipes-cookbooks-and-menus")]
    [InlineData("https://www.myplate.gov/recipes/category/breakfast")]
    // Localised variants — English only for v1
    [InlineData("https://www.myplate.gov/es/recipes/chili-de-3-latas")]
    // Malformed archival entries
    [InlineData("https://www.myplate.gov/recipes/3-can-chili%5Cr%5Cn")]
    [InlineData("https://www.myplate.gov/recipes/3-can-chili.While")]
    [InlineData("not a url at all")]
    public void TryExtractSlug_RejectsEverythingThatIsNotARecipePage(string url)
    {
        var slug = WaybackHarvester.TryExtractSlug(url, "/recipes/", out var dropReason);

        slug.Should().BeNull();
        dropReason.Should().NotBeNull();
    }

    [Fact]
    public void TryExtractSlug_NamesTheRuleThatRejectedTheRow()
    {
        WaybackHarvester.TryExtractSlug(
            "https://www.myplate.gov/recipes/3-can-chili?page=1", "/recipes/", out var queryReason);
        WaybackHarvester.TryExtractSlug(
            "https://www.myplate.gov/recipes/111.57", "/recipes/", out var numericReason);
        WaybackHarvester.TryExtractSlug(
            "https://www.myplate.gov/recipes-cookbooks-and-menus", "/recipes/", out var pathReason);

        queryReason.Should().Be("queryString");
        numericReason.Should().Be("numericSlug");
        pathReason.Should().Be("nonRecipePath");
    }

    // ── Stage 1: snapshot selection ───────────────────────────────────────────

    [Fact]
    public void PreferredSnapshot_PrefersTheLatestCaptureInsideThePreferredYear()
    {
        var url      = "https://www.myplate.gov/recipes/3-can-chili";
        var early2025 = new WaybackHarvester.CdxRow(url, "20250104120000");
        var late2025  = new WaybackHarvester.CdxRow(url, "20251231013807");
        var later2026 = new WaybackHarvester.CdxRow(url, "20260401090000");

        WaybackHarvester.PreferredSnapshot(early2025, late2025, 2025).Should().Be(late2025);
        WaybackHarvester.PreferredSnapshot(later2026, late2025, 2025).Should().Be(late2025);
        WaybackHarvester.PreferredSnapshot(late2025, later2026, 2025).Should().Be(late2025);
    }

    [Fact]
    public void PreferredSnapshot_FallsBackToTheLatestCaptureWhenThePreferredYearIsAbsent()
    {
        var url  = "https://www.myplate.gov/recipes/3-can-chili";
        var old  = new WaybackHarvester.CdxRow(url, "20190104120000");
        var newer = new WaybackHarvester.CdxRow(url, "20240401090000");

        WaybackHarvester.PreferredSnapshot(old, newer, 2025).Should().Be(newer);
        WaybackHarvester.PreferredSnapshot(newer, old, 2025).Should().Be(newer);
    }

    // ── Stage 1: discovery end to end ─────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_BuildsAManifestOfDistinctRecipeSlugs()
    {
        var http = StubHttpClientFactory.Returning(CdxResponse(
            ("https://www.myplate.gov/recipes/3-can-chili",             "20250104120000"),
            ("https://myplate.gov/recipes/3-can-chili",                 "20251231013807"),
            ("https://www.myplate.gov/recipes/3-can-chili?page=1",      "20251231013807"),
            ("https://www.myplate.gov/recipes/20-minute-chicken-creole","20251115090000"),
            ("https://www.myplate.gov/recipes/111.57",                  "20251115090000"),
            ("https://www.myplate.gov/recipes-cookbooks-and-menus",     "20251115090000")),
            mediaType: "application/json");

        var (harvester, cache) = Build(http, TestOptions());

        var result = await harvester.DiscoverAsync();

        result.RawRowCount.Should().Be(6);
        result.Manifest.Recipes.Select(recipe => recipe.Slug)
            .Should().Equal("20-minute-chicken-creole", "3-can-chili");

        // The www. and non-www. captures are the same recipe; the later 2025 snapshot wins.
        result.Manifest.Recipes.Single(recipe => recipe.Slug == "3-can-chili")
            .Timestamp.Should().Be("20251231013807");

        result.DroppedByReason["queryString"].Should().Be(1);
        result.DroppedByReason["numericSlug"].Should().Be(1);
        result.DroppedByReason["nonRecipePath"].Should().Be(1);

        (await cache.TryLoadManifestAsync())!.Recipes.Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscoverAsync_AsksTheIndexForEveryCaptureRatherThanACollapsedOne()
    {
        // CDX collapse=urlkey keeps the *first* capture of each key, which would silently pin
        // every recipe to an arbitrary early snapshot and make the preferred-year rule inert.
        var http = StubHttpClientFactory.Returning(CdxResponse(
            ("https://www.myplate.gov/recipes/3-can-chili", "20250104120000")),
            mediaType: "application/json");

        var (harvester, _) = Build(http, TestOptions());
        await harvester.DiscoverAsync();

        var requestUrl = http.RequestedUrls.Should().ContainSingle().Subject;
        requestUrl.Should().NotContain("collapse");
        requestUrl.Should().Contain("filter=statuscode:200");
    }

    [Fact]
    public async Task DiscoverAsync_PinsEachSlugToItsPreferredYearSnapshot()
    {
        var http = StubHttpClientFactory.Returning(CdxResponse(
            ("https://www.myplate.gov/recipes/3-can-chili", "20240301080000"),
            ("https://www.myplate.gov/recipes/3-can-chili", "20250601120000"),
            ("https://www.myplate.gov/recipes/3-can-chili", "20251231013807"),
            ("https://www.myplate.gov/recipes/5-day-salad", "20230301080000"),
            ("https://www.myplate.gov/recipes/5-day-salad", "20240915093000")),
            mediaType: "application/json");

        var (harvester, _) = Build(http, TestOptions());
        var result = await harvester.DiscoverAsync();

        var chili = result.Manifest.Recipes.Single(recipe => recipe.Slug == "3-can-chili");
        chili.Timestamp.Should().Be("20251231013807", "the last capture inside the preferred year wins");

        var salad = result.Manifest.Recipes.Single(recipe => recipe.Slug == "5-day-salad");
        salad.Timestamp.Should().Be("20240915093000", "with no 2025 capture, the latest overall wins");
    }

    [Fact]
    public async Task DiscoverAsync_RecordsEveryDiscoveredSlugInState()
    {
        var http = StubHttpClientFactory.Returning(CdxResponse(
            ("https://www.myplate.gov/recipes/3-can-chili", "20250104120000")),
            mediaType: "application/json");

        var (harvester, cache) = Build(http, TestOptions());
        await harvester.DiscoverAsync();

        var state = await cache.LoadStateAsync();
        state.Slugs.Should().ContainKey("3-can-chili");
        state.Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Discovered);
    }

    [Fact]
    public async Task DiscoverAsync_AbortsWhenTheYieldIsBelowTheMinimum()
    {
        // A short manifest means the filter rules have drifted, not that the archive shrank.
        var options = TestOptions(minimumDiscoveredSlugs: 800);
        var http = StubHttpClientFactory.Returning(CdxResponse(
            ("https://www.myplate.gov/recipes/3-can-chili", "20250104120000")),
            mediaType: "application/json");

        var (harvester, cache) = Build(http, options);

        var discover = async () => await harvester.DiscoverAsync();

        (await discover.Should().ThrowAsync<SeedHarvestException>())
            .WithMessage("*below the minimum of 800*");
        cache.HasManifest().Should().BeFalse();
    }

    [Fact]
    public async Task DiscoverAsync_AbortsWithoutAPartialManifestWhenTheIndexIsUnreachable()
    {
        var options = TestOptions(maxFetchRetries: 1);
        var http    = new StubHttpClientFactory(_ =>
            StubHttpClientFactory.Status(HttpStatusCode.ServiceUnavailable));

        var (harvester, cache) = Build(http, options);

        var discover = async () => await harvester.DiscoverAsync();

        (await discover.Should().ThrowAsync<SeedHarvestException>())
            .WithMessage("*could not be reached*");
        cache.HasManifest().Should().BeFalse();
        http.RequestedUrls.Should().HaveCount(2, "the initial attempt plus one retry");
    }

    [Fact]
    public async Task DiscoverAsync_AbortsWhenTheIndexReturnsNoCaptures()
    {
        var http = StubHttpClientFactory.Returning(
            "[[\"original\",\"timestamp\",\"statuscode\"]]", mediaType: "application/json");

        var (harvester, _) = Build(http, TestOptions());

        var discover = async () => await harvester.DiscoverAsync();

        (await discover.Should().ThrowAsync<SeedHarvestException>())
            .WithMessage("*no captures*");
    }

    [Fact]
    public async Task EnsureManifestAsync_ReusesTheCachedManifestWithoutQueryingTheIndex()
    {
        var options = TestOptions();
        var cache   = BuildCache(options);
        await cache.SaveManifestAsync(new SeedManifest
        {
            Recipes = [new("3-can-chili", "20250104120000", "https://www.myplate.gov/recipes/3-can-chili")],
        });

        var http      = new StubHttpClientFactory(_ => throw new InvalidOperationException("must not query CDX"));
        var harvester = new WaybackHarvester(http, Options.Create(options), cache,
            NullLogger<WaybackHarvester>.Instance);

        var manifest = await harvester.EnsureManifestAsync();

        manifest.Recipes.Should().ContainSingle().Which.Slug.Should().Be("3-can-chili");
        http.RequestedUrls.Should().BeEmpty();
    }

    // ── Stage 2: fetch and cache ──────────────────────────────────────────────

    private static SeedManifest ManifestOf(params string[] slugs) => new()
    {
        SourcePattern = "myplate.gov/recipes/*",
        Recipes = [.. slugs.Select(slug =>
            new SeedManifestEntry(slug, "20251231013807", $"https://www.myplate.gov/recipes/{slug}"))],
    };

    [Fact]
    public async Task HarvestAsync_CachesEachArchivedPageAndRecordsProgress()
    {
        var options = TestOptions(downloadImages: false);
        var http    = StubHttpClientFactory.Returning("<html><body>chili</body></html>");
        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili", "apple-oatmeal-bars"));

        result.PagesFetched.Should().Be(2);
        result.PagesFailed.Should().Be(0);
        cache.HasRaw("3-can-chili").Should().BeTrue();
        (await cache.ReadRawAsync("apple-oatmeal-bars")).Should().Be("<html><body>chili</body></html>");

        var state = await cache.LoadStateAsync();
        state.Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Fetched);
        state.Slugs["3-can-chili"].Attempts.Should().Be(1);
    }

    [Fact]
    public async Task HarvestAsync_RequestsTheSnapshotPinnedByTheManifest()
    {
        var options = TestOptions(downloadImages: false);
        var http    = StubHttpClientFactory.Returning("<html></html>");
        var (harvester, _) = Build(http, options);

        await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        http.RequestedUrls.Should().ContainSingle().Which.Should().Be(
            "http://web.archive.org/web/20251231013807/https://www.myplate.gov/recipes/3-can-chili");
    }

    [Fact]
    public async Task HarvestAsync_SkipsPagesAlreadyInTheCache()
    {
        var options = TestOptions(downloadImages: false);
        var http    = StubHttpClientFactory.Returning("<html>fresh</html>");
        var (harvester, cache) = Build(http, options);

        await cache.WriteRawAsync("3-can-chili", Encoding.UTF8.GetBytes("<html>cached</html>"));

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili", "apple-oatmeal-bars"));

        result.PagesSkipped.Should().Be(1);
        result.PagesFetched.Should().Be(1);
        http.RequestedUrls.Should().ContainSingle()
            .Which.Should().EndWith("/recipes/apple-oatmeal-bars");
        (await cache.ReadRawAsync("3-can-chili")).Should().Be("<html>cached</html>");
    }

    [Fact]
    public async Task HarvestAsync_CorrectsStateThatLagsBehindTheCacheContents()
    {
        // A cache copied between machines, or an interrupted state flush: the file on disk is the
        // authority, so the skipped page is still recorded as fetched.
        var options = TestOptions(downloadImages: false);
        var (harvester, cache) = Build(StubHttpClientFactory.Returning("<html></html>"), options);

        await cache.WriteRawAsync("3-can-chili", Encoding.UTF8.GetBytes("<html>cached</html>"));

        await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        (await cache.LoadStateAsync()).Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Fetched);
    }

    [Fact]
    public async Task HarvestAsync_StopsAtTheRequestedLimit()
    {
        var options = TestOptions(downloadImages: false);
        var http    = StubHttpClientFactory.Returning("<html></html>");
        var (harvester, _) = Build(http, options);

        var result = await harvester.HarvestAsync(
            ManifestOf("a-recipe", "b-recipe", "c-recipe"), limit: 2);

        result.PagesFetched.Should().Be(2);
        http.RequestedUrls.Should().HaveCount(2);
    }

    // ── Stage 2: retries and failure isolation ────────────────────────────────

    [Fact]
    public async Task HarvestAsync_RetriesATransientFailureAndThenSucceeds()
    {
        var options  = TestOptions(downloadImages: false, maxFetchRetries: 3);
        var attempts = 0;
        var http = new StubHttpClientFactory(_ =>
            ++attempts < 3
                ? StubHttpClientFactory.Status(HttpStatusCode.TooManyRequests)
                : StubHttpClientFactory.Ok("<html>chili</html>"));

        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.PagesFetched.Should().Be(1);
        result.PagesFailed.Should().Be(0);
        attempts.Should().Be(3);
        cache.HasRaw("3-can-chili").Should().BeTrue();
    }

    [Fact]
    public async Task HarvestAsync_MarksAPageFailedAfterExhaustingRetriesWithoutAbortingTheRun()
    {
        var options = TestOptions(downloadImages: false, maxFetchRetries: 2);
        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().Contains("3-can-chili")
                ? StubHttpClientFactory.Status(HttpStatusCode.ServiceUnavailable)
                : StubHttpClientFactory.Ok("<html>fine</html>"));

        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili", "apple-oatmeal-bars"));

        result.PagesFailed.Should().Be(1);
        result.PagesFetched.Should().Be(1, "one bad page must never abort the harvest");
        result.Failures.Should().ContainKey("3-can-chili");
        result.Failures["3-can-chili"].Should().Contain("503");

        var state = await cache.LoadStateAsync();
        state.Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Failed);
        state.Slugs["3-can-chili"].LastError.Should().Contain("503");
        cache.HasRaw("3-can-chili").Should().BeFalse("a failed fetch must not leave a partial page");
    }

    [Fact]
    public async Task HarvestAsync_DoesNotRetryAPermanentFailure()
    {
        var options = TestOptions(downloadImages: false, maxFetchRetries: 3);
        var http    = new StubHttpClientFactory(_ => StubHttpClientFactory.Status(HttpStatusCode.NotFound));
        var (harvester, _) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.PagesFailed.Should().Be(1);
        http.RequestedUrls.Should().ContainSingle("a 404 will not become a 200 on retry");
    }

    // ── Stage 2b: images ──────────────────────────────────────────────────────

    /// <summary>Real JPEG and PNG signatures — the harvester identifies a payload by its own bytes.</summary>
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] PngBytes  = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private const string PageWithArchivedImage = """
        <html><head>
        <script type="application/ld+json">
        {"@context":"http://web.archive.org/web/20251231013807/https://schema.org",
         "@graph":[{"@type":"Recipe","name":"3 Can Chili",
                    "image":{"@type":"ImageObject",
                             "url":"http://web.archive.org/web/20251231013807/https://myplate-prod.azureedge.us/Chili.jpg"}}]}
        </script></head><body>Chili</body></html>
        """;

    [Fact]
    public void TryExtractImageUrl_ReadsTheArchivedPhotoUrlFromJsonLd() =>
        WaybackHarvester.TryExtractImageUrl(PageWithArchivedImage).Should().Be(
            "http://web.archive.org/web/20251231013807/https://myplate-prod.azureedge.us/Chili.jpg");

    [Fact]
    public void TryExtractImageUrl_ReturnsNullWhenThePageHasNoRecipeMarkup() =>
        WaybackHarvester.TryExtractImageUrl("<html><body>no json-ld here</body></html>")
            .Should().BeNull();

    [Theory]
    // Already rewritten by the archive, but pointing at its HTML viewer rather than the bytes
    [InlineData("http://web.archive.org/web/20251231013810/https://myplate-prod.azureedge.us/Chili.jpg",
                "http://web.archive.org/web/20251231013810im_/https://myplate-prod.azureedge.us/Chili.jpg")]
    // Already carrying a different snapshot modifier
    [InlineData("http://web.archive.org/web/20251231013810js_/https://myplate-prod.azureedge.us/Chili.jpg",
                "http://web.archive.org/web/20251231013810im_/https://myplate-prod.azureedge.us/Chili.jpg")]
    public async Task HarvestAsync_RequestsPhotosThroughTheRawSnapshotModifier(
        string jsonLdImageUrl, string expectedRequest)
    {
        // Without im_ the archive redirects to its HTML viewer and the "photo" downloads as a page.
        var page =
            "<html><head><script type=\"application/ld+json\">" +
            "{\"@type\":\"Recipe\",\"name\":\"3 Can Chili\",\"image\":{\"url\":\"" + jsonLdImageUrl + "\"}}" +
            "</script></head><body></body></html>";

        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().Contains("Chili.jpg")
                ? StubHttpClientFactory.Ok(JpegBytes, "image/jpeg")
                : StubHttpClientFactory.Ok(page));

        var (harvester, _) = Build(http, TestOptions(downloadImages: true));

        await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        http.RequestedUrls.Should().Contain(expectedRequest);
    }

    [Fact]
    public async Task HarvestAsync_RejectsAnArchiveErrorPageServedInPlaceOfAPhoto()
    {
        // The Wayback Machine returns its own interstitial and error pages with a 200, so a
        // successful status is not evidence that the bytes are a photograph.
        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().Contains("Chili.jpg")
                ? StubHttpClientFactory.Ok("<!DOCTYPE html><html><title>Wayback Machine</title></html>")
                : StubHttpClientFactory.Ok(PageWithArchivedImage));

        var (harvester, cache) = Build(http, TestOptions(downloadImages: true));

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.ImagesFailed.Should().Be(1);
        result.ImagesFetched.Should().Be(0);
        cache.HasImage("3-can-chili").Should().BeFalse();
        result.PagesFetched.Should().Be(1, "the recipe still imports, just without a photo");
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif")]
    public void DetectImageExtension_IdentifiesAPayloadByItsSignature(byte[] content, string expected) =>
        WaybackHarvester.DetectImageExtension(content).Should().Be(expected);

    [Fact]
    public void DetectImageExtension_ReturnsNullForMarkup() =>
        WaybackHarvester.DetectImageExtension("<!DOCTYPE html>"u8).Should().BeNull();

    [Fact]
    public async Task HarvestAsync_CachesTheRecipePhotoAlongsideThePage()
    {
        var options = TestOptions(downloadImages: true);
        var photo   = JpegBytes;
        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? StubHttpClientFactory.Ok(photo, "image/jpeg")
                : StubHttpClientFactory.Ok(PageWithArchivedImage));

        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.ImagesFetched.Should().Be(1);
        cache.FindImage("3-can-chili").Should().EndWith("3-can-chili.jpg");
        (await File.ReadAllBytesAsync(cache.FindImage("3-can-chili")!)).Should().Equal(photo);
    }

    [Fact]
    public async Task HarvestAsync_TreatsAFailedPhotoFetchAsNonFatal()
    {
        var options = TestOptions(downloadImages: true, maxFetchRetries: 0);
        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? StubHttpClientFactory.Status(HttpStatusCode.NotFound)
                : StubHttpClientFactory.Ok(PageWithArchivedImage));

        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.PagesFetched.Should().Be(1, "the recipe still imports, just without a photo");
        result.ImagesFailed.Should().Be(1);
        cache.HasImage("3-can-chili").Should().BeFalse();

        var state = await cache.LoadStateAsync();
        state.Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Fetched);
    }

    [Fact]
    public async Task HarvestAsync_SkipsPhotosAlreadyInTheCache()
    {
        var options = TestOptions(downloadImages: true);
        var http    = new StubHttpClientFactory(_ => StubHttpClientFactory.Ok(PageWithArchivedImage));
        var (harvester, cache) = Build(http, options);

        await cache.WriteImageAsync("3-can-chili", JpegBytes, ".jpg");

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.ImagesSkipped.Should().Be(1);
        result.ImagesFetched.Should().Be(0);
        http.RequestedUrls.Should().ContainSingle().Which.Should().EndWith("/recipes/3-can-chili");
    }

    [Fact]
    public async Task HarvestAsync_LeavesPhotosAloneWhenImageHarvestingIsOff()
    {
        var options = TestOptions(downloadImages: false);
        var http    = new StubHttpClientFactory(_ => StubHttpClientFactory.Ok(PageWithArchivedImage));
        var (harvester, cache) = Build(http, options);

        var result = await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        result.ImagesFetched.Should().Be(0);
        cache.HasImage("3-can-chili").Should().BeFalse();
        http.RequestedUrls.Should().ContainSingle();
    }

    [Fact]
    public async Task HarvestAsync_WrapsAPhotoUrlTheArchiveDidNotRewrite()
    {
        // The original image host was decommissioned with the site, so an unrewritten URL is only
        // reachable through the same snapshot.
        const string pageWithLiveImageUrl = """
            <html><head><script type="application/ld+json">
            {"@type":"Recipe","name":"3 Can Chili",
             "image":{"url":"https://myplate-prod.azureedge.us/Chili.png"}}
            </script></head><body></body></html>
            """;

        var options = TestOptions(downloadImages: true);
        var http = new StubHttpClientFactory(request =>
            request.RequestUri!.ToString().EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? StubHttpClientFactory.Ok(PngBytes, "image/png")
                : StubHttpClientFactory.Ok(pageWithLiveImageUrl));

        var (harvester, cache) = Build(http, options);

        await harvester.HarvestAsync(ManifestOf("3-can-chili"));

        http.RequestedUrls.Should().Contain(
            "http://web.archive.org/web/20251231013807im_/https://myplate-prod.azureedge.us/Chili.png");
        cache.FindImage("3-can-chili").Should().EndWith("3-can-chili.png");
    }
}
