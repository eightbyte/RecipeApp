using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Owns the on-disk seed cache (Phase 9 §5) — the manifest, the per-stage artefact directories,
/// and <c>state.json</c>.
///
/// <para>The cache is what makes the harvest a one-time cost: every stage writes its output before
/// the next stage reads it, so a failure late in the pipeline never forces a re-download and a
/// prompt change never forces a re-fetch.</para>
///
/// <para>Slugs arrive from a remote index and are used as filenames, so every path-building entry
/// point runs them through <see cref="ValidateSlug"/> first.</para>
/// </summary>
public class SeedCacheStore
{
    private const string ManifestFileName = "manifest.json";
    private const string StateFileName    = "state.json";
    private const string RawExtension     = ".html";
    private const string JsonExtension    = ".json";

    /// <summary>Suffix for the temporary file an atomic write moves into place.</summary>
    private const string TempExtension = ".tmp";

    /// <summary>
    /// Drupal pathauto slugs: lowercase alphanumerics separated by single hyphens or underscores.
    /// Anchored and deliberately narrow — this is the path-traversal guard, not a tidiness check,
    /// so it admits no separators, no dots and no percent escapes.
    /// </summary>
    private static readonly Regex SlugPattern =
        new(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
        Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ILogger<SeedCacheStore> _logger;
    private SeedState? _state;

    public SeedCacheStore(
        IOptions<RecipeSeedingOptions> options,
        IHostEnvironment environment,
        ILogger<SeedCacheStore> logger)
    {
        _logger = logger;

        var configured = options.Value.CacheDirectory;
        RootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public string RootPath { get; }

    public string RawDirectory        => Path.Combine(RootPath, "raw");
    public string ParsedDirectory     => Path.Combine(RootPath, "parsed");
    public string NormalisedDirectory => Path.Combine(RootPath, "normalised");
    public string ImagesDirectory     => Path.Combine(RootPath, "images");

    public string ManifestPath => Path.Combine(RootPath, ManifestFileName);
    public string StatePath    => Path.Combine(RootPath, StateFileName);

    /// <summary>Creates every cache directory. Safe to call repeatedly.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(RawDirectory);
        Directory.CreateDirectory(ParsedDirectory);
        Directory.CreateDirectory(NormalisedDirectory);
        Directory.CreateDirectory(ImagesDirectory);
    }

    // ── Slug safety ───────────────────────────────────────────────────────────

    /// <summary>Whether a slug is safe to use as a cache filename.</summary>
    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrEmpty(slug) && SlugPattern.IsMatch(slug);

    /// <summary>
    /// Returns the slug unchanged, or throws if it could escape the cache directory.
    /// Called by every method that turns a slug into a path.
    /// </summary>
    public static string ValidateSlug(string? slug)
    {
        if (!IsValidSlug(slug))
            throw new ArgumentException(
                $"'{slug}' is not a usable cache slug. Slugs are used as filenames and must be " +
                "lowercase alphanumerics separated by single hyphens or underscores.",
                nameof(slug));

        return slug!;
    }

    // ── manifest.json ─────────────────────────────────────────────────────────

    public bool HasManifest() => File.Exists(ManifestPath);

    /// <summary>Reads the manifest, or null when it is absent or unreadable.</summary>
    public async Task<SeedManifest?> TryLoadManifestAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ManifestPath)) return null;

        try
        {
            await using var stream = File.OpenRead(ManifestPath);
            return await JsonSerializer.DeserializeAsync<SeedManifest>(stream, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Seed manifest at {Path} is unreadable. Re-run discovery.", ManifestPath);
            return null;
        }
    }

    public async Task SaveManifestAsync(SeedManifest manifest, CancellationToken ct = default)
    {
        EnsureDirectories();
        await WriteJsonAtomicallyAsync(ManifestPath, manifest, ct);
    }

    // ── state.json ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads progress state, caching it for the lifetime of this store. A corrupt file is
    /// discarded rather than thrown on: state is derived bookkeeping, and losing it costs
    /// a re-scan of the cache directories, not a re-download.
    /// </summary>
    public async Task<SeedState> LoadStateAsync(CancellationToken ct = default)
    {
        if (_state is not null) return _state;

        if (!File.Exists(StatePath))
            return _state = new SeedState();

        try
        {
            await using var stream = File.OpenRead(StatePath);
            _state = await JsonSerializer.DeserializeAsync<SeedState>(stream, JsonOptions, ct)
                     ?? new SeedState();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Seed state at {Path} is corrupt and has been discarded. Progress will be " +
                "rebuilt from the cache directories.", StatePath);
            _state = new SeedState();
        }

        return _state;
    }

    public async Task SaveStateAsync(CancellationToken ct = default)
    {
        var state = await LoadStateAsync(ct);
        state.UpdatedAt = DateTime.UtcNow;

        EnsureDirectories();
        await WriteJsonAtomicallyAsync(StatePath, state, ct);
    }

    /// <summary>
    /// Records the stage a slug has reached and flushes state to disk, so an interrupted run
    /// resumes from the last completed page rather than the start.
    /// </summary>
    public async Task RecordStageAsync(
        string slug, SeedStage stage, string? error = null, CancellationToken ct = default)
    {
        ValidateSlug(slug);

        var state     = await LoadStateAsync(ct);
        var slugState = state.GetOrAdd(slug);

        slugState.Stage     = stage;
        slugState.LastError = error;

        await SaveStateAsync(ct);
    }

    /// <summary>Increments the attempt counter for a slug without flushing — the caller records the outcome.</summary>
    public async Task<int> RecordAttemptAsync(string slug, CancellationToken ct = default)
    {
        ValidateSlug(slug);

        var state = await LoadStateAsync(ct);
        return ++state.GetOrAdd(slug).Attempts;
    }

    // ── Stage artefacts ───────────────────────────────────────────────────────

    public string RawPath(string slug) =>
        Path.Combine(RawDirectory, ValidateSlug(slug) + RawExtension);

    public string ParsedPath(string slug) =>
        Path.Combine(ParsedDirectory, ValidateSlug(slug) + JsonExtension);

    public string NormalisedPath(string slug) =>
        Path.Combine(NormalisedDirectory, ValidateSlug(slug) + JsonExtension);

    public bool HasRaw(string slug) => File.Exists(RawPath(slug));

    /// <summary>Writes the archived response bytes verbatim — decoding is the parser's problem, not the cache's.</summary>
    public async Task WriteRawAsync(string slug, byte[] content, CancellationToken ct = default)
    {
        EnsureDirectories();
        await WriteBytesAtomicallyAsync(RawPath(slug), content, ct);
    }

    public Task<string> ReadRawAsync(string slug, CancellationToken ct = default) =>
        File.ReadAllTextAsync(RawPath(slug), ct);

    // ── Images (Stage 2b) ─────────────────────────────────────────────────────

    /// <summary>Path of the cached image for a slug, whatever its extension, or null if none was harvested.</summary>
    public string? FindImage(string slug)
    {
        ValidateSlug(slug);
        if (!Directory.Exists(ImagesDirectory)) return null;

        return Directory.EnumerateFiles(ImagesDirectory, slug + ".*").FirstOrDefault();
    }

    public bool HasImage(string slug) => FindImage(slug) is not null;

    public async Task WriteImageAsync(
        string slug, byte[] content, string extension, CancellationToken ct = default)
    {
        EnsureDirectories();
        await WriteBytesAtomicallyAsync(
            Path.Combine(ImagesDirectory, ValidateSlug(slug) + extension), content, ct);
    }

    // ── Cache maintenance ─────────────────────────────────────────────────────

    /// <summary>
    /// Discards every cached page and image and rewinds all progress, for <c>--refresh-cache</c>.
    /// The manifest survives: re-harvesting the same pinned snapshots is the point.
    /// </summary>
    public async Task ClearFetchedContentAsync(CancellationToken ct = default)
    {
        DeleteDirectoryContents(RawDirectory);
        DeleteDirectoryContents(ImagesDirectory);

        var state = await LoadStateAsync(ct);
        state.RewindToDiscovered();
        await SaveStateAsync(ct);

        _logger.LogInformation("Discarded cached pages and images under {Path}.", RootPath);
    }

    private static void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory))
            File.Delete(file);
    }

    // ── Atomic writes ─────────────────────────────────────────────────────────

    // state.json is rewritten after every page of a multi-hour run. Writing through a temporary
    // file means an interruption mid-write leaves the previous state intact instead of truncating
    // the record of a thousand fetches.

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken ct)
    {
        var tempPath = path + TempExtension;

        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);

        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task WriteBytesAtomicallyAsync(string path, byte[] content, CancellationToken ct)
    {
        var tempPath = path + TempExtension;

        await File.WriteAllBytesAsync(tempPath, content, ct);
        File.Move(tempPath, path, overwrite: true);
    }
}
