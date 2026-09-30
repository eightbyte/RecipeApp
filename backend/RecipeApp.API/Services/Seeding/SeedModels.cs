namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Furthest pipeline stage a slug has reached. Ordered, so <see cref="SeedSlugState.HasReached"/>
/// can answer "is this already done?" with a comparison — except <see cref="Failed"/>, which is
/// terminal rather than advanced and is excluded from that comparison explicitly.
/// </summary>
public enum SeedStage
{
    Discovered,
    Fetched,
    Parsed,
    Normalised,
    Persisted,
    Failed,
}

// ── manifest.json (Stage 1 output) ────────────────────────────────────────────

/// <summary>
/// The discovered corpus: one entry per recipe slug, each pinned to a specific archive snapshot.
/// Committed to git, so a re-harvest on another machine reads the same captures.
/// </summary>
public record SeedManifest
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public DateTime HarvestedAt { get; init; }
    public string SourcePattern { get; init; } = string.Empty;
    public IReadOnlyList<SeedManifestEntry> Recipes { get; init; } = [];
}

/// <param name="Slug">Canonical recipe slug — also the cache filename key.</param>
/// <param name="Timestamp">Wayback capture stamp, <c>yyyyMMddHHmmss</c>.</param>
/// <param name="OriginalUrl">The URL as archived, used verbatim to build the snapshot URL.</param>
public record SeedManifestEntry(string Slug, string Timestamp, string OriginalUrl);

// ── state.json (per-slug progress) ────────────────────────────────────────────

/// <summary>
/// Per-slug stage progress, rewritten after every page so an interrupted run resumes instead of
/// restarting. Treated as disposable: a corrupt file is discarded, and the cache directories
/// themselves are the real record of what has been done.
/// </summary>
public class SeedState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, SeedSlugState> Slugs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Existing entry for a slug, or a newly tracked one at <see cref="SeedStage.Discovered"/>.</summary>
    public SeedSlugState GetOrAdd(string slug)
    {
        if (!Slugs.TryGetValue(slug, out var slugState))
        {
            slugState = new SeedSlugState();
            Slugs[slug] = slugState;
        }
        return slugState;
    }

    /// <summary>
    /// Rewinds every slug to <see cref="SeedStage.Discovered"/>, for <c>--refresh-cache</c>:
    /// once the cached HTML is gone, no later stage can honestly claim to be complete.
    /// </summary>
    public void RewindToDiscovered()
    {
        foreach (var slugState in Slugs.Values)
        {
            slugState.Stage     = SeedStage.Discovered;
            slugState.Attempts  = 0;
            slugState.LastError = null;
        }
    }
}

public class SeedSlugState
{
    public SeedStage Stage { get; set; } = SeedStage.Discovered;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Whether this slug has completed <paramref name="stage"/>. A failed slug has reached
    /// nothing — it is excluded rather than compared, because <see cref="SeedStage.Failed"/>
    /// sorts last without being the furthest progress.
    /// </summary>
    public bool HasReached(SeedStage stage) => Stage != SeedStage.Failed && Stage >= stage;
}

// ── Stage results ─────────────────────────────────────────────────────────────

/// <param name="Manifest">The manifest written to the cache.</param>
/// <param name="RawRowCount">CDX rows returned before filtering.</param>
/// <param name="DroppedByReason">Rows discarded per filter rule — the diagnostic that says
/// <i>why</i> a discovery run came back short.</param>
public record SeedDiscoveryResult(
    SeedManifest Manifest,
    int RawRowCount,
    IReadOnlyDictionary<string, int> DroppedByReason);

/// <summary>Outcome of a Stage 2 / 2b run.</summary>
public record SeedHarvestResult
{
    /// <summary>Pages downloaded during this run.</summary>
    public int PagesFetched { get; init; }

    /// <summary>Pages already present in the cache and left untouched.</summary>
    public int PagesSkipped { get; init; }

    /// <summary>Pages that exhausted their retries or returned an unrecoverable status.</summary>
    public int PagesFailed { get; init; }

    public int ImagesFetched { get; init; }
    public int ImagesSkipped { get; init; }

    /// <summary>Image failures are non-fatal — the recipe still imports, without a photo.</summary>
    public int ImagesFailed { get; init; }

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}

// ── Failures ──────────────────────────────────────────────────────────────────

/// <summary>
/// Raised only for conditions that must abort the whole run — an unreachable CDX index or a
/// manifest short enough to indicate drifted filter rules. A single bad recipe never throws.
/// </summary>
public class SeedHarvestException(string message, Exception? innerException = null)
    : Exception(message, innerException);
