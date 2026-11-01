namespace RecipeApp.API.Services.Seeding;

/// <summary>Configuration for the one-time USDA MyPlate seed import, with every network endpoint configurable so it can be re-pointed without a code change.</summary>
public class RecipeSeedingOptions
{
    public const string SectionName = "RecipeSeeding";

    /// <summary>Root of the on-disk cache, resolved against the content root; not <c>data/</c>, which would collide with the case-insensitive <c>Data/</c> folder on Windows.</summary>
    public string CacheDirectory { get; init; } = "seed-data/myplate";

    /// <summary>CDX index endpoint used for slug discovery (Stage 1).</summary>
    public string WaybackCdxUrl { get; init; } = "http://web.archive.org/cdx/search/cdx";

    /// <summary>Archived-content base URL. Snapshot URLs are <c>{this}/{timestamp}/{originalUrl}</c>.</summary>
    public string WaybackContentUrl { get; init; } = "http://web.archive.org/web";

    /// <summary>CDX wildcard identifying recipe pages in the archive.</summary>
    public string SourceUrlPattern { get; init; } = "myplate.gov/recipes/*";

    /// <summary>Path prefix a discovered URL must carry to count as a recipe page, excluding index pages, sibling sections and localised variants.</summary>
    public string RecipePathPrefix { get; init; } = "/recipes/";

    /// <summary>Snapshot year to prefer per slug; falls back to the latest available capture.</summary>
    public int PreferredSnapshotYear { get; init; } = 2025;

    /// <summary>Per-page Wayback timeout — the archive is slow.</summary>
    public int FetchTimeoutSeconds { get; init; } = 60;

    /// <summary>Budget for the one CDX index query, which returns the whole capture history (about 5 MB, 35 s).</summary>
    public int DiscoveryTimeoutSeconds { get; init; } = 300;

    /// <summary>Politeness delay between archive requests. Fetching is deliberately sequential.</summary>
    public int FetchDelayMilliseconds { get; init; } = 1500;

    /// <summary>Retries per page on 429/5xx/timeout, in addition to the first attempt.</summary>
    public int MaxFetchRetries { get; init; } = 3;

    /// <summary>Base backoff before the first retry; doubled on each subsequent attempt.</summary>
    public int RetryBackoffMilliseconds { get; init; } = 5000;

    /// <summary>Serving count assumed when a page publishes no <c>recipeYield</c>, since a recipe with no serving count cannot be scaled.</summary>
    public int DefaultServings { get; init; } = 4;

    /// <summary>Retries per recipe when LLM output fails the Stage 4 validation gate.</summary>
    public int MaxLlmRetries { get; init; } = 2;

    /// <summary>Budget for one Stage 4 completion, longer than the scraper's because the model copies every step back out.</summary>
    public int LlmTimeoutSeconds { get; init; } = 180;

    /// <summary>Token ceiling for one Stage 4 completion, with roughly half again as much headroom as the largest recipe needs.</summary>
    public int MaxLlmOutputTokens { get; init; } = 4096;

    /// <summary>Consecutive systemic recipe failures that stop a Stage 4 run, counting only failures where no readable answer came back.</summary>
    public int MaxConsecutiveLlmFailures { get; init; } = 5;

    // ── Stage 4.5: the corpus-derived ingredient catalogue ────────────────────

    /// <summary>The shipped, curated catalogue, resolved against the content root and kept beside the cache so <c>--refresh-cache</c> cannot delete it.</summary>
    public string CatalogueFilePath { get; init; } = "seed-data/ingredient-catalogue.json";

    /// <summary>Where <c>--build-catalogue</c> writes its derived catalogue, inside the MyPlate cache so a re-run never overwrites the curated file.</summary>
    public string CatalogueBuildFilePath { get; init; } = "seed-data/myplate/catalogue-build.json";

    /// <summary>Names per grouping call, large enough that a head-noun family fits in one call and small enough that the model reads every source line.</summary>
    public int CatalogueBatchSize { get; init; } = 40;

    /// <summary>The build aborts below this many entries, because a much smaller result means the grouping pass collapsed the corpus.</summary>
    public int CatalogueMinimumEntries { get; init; } = 800;

    /// <summary>A grouping answer whose group spans more families than this is treated as degenerate: retried, then dissolved so its names stand alone.</summary>
    public int CatalogueMaxFamiliesPerGroup { get; init; } = 3;

    /// <summary>Discovery aborts below this many slugs, because a short manifest means the CDX filter has drifted and importing a fraction silently is worse than failing loudly.</summary>
    public int MinimumDiscoveredSlugs { get; init; } = 800;

    /// <summary>Whether to harvest recipe photos alongside the pages (Stage 2b).</summary>
    public bool DownloadImages { get; init; } = true;

    /// <summary>Image extensions accepted from an archived image URL; anything else is stored under <see cref="DefaultImageExtension"/>.</summary>
    public IReadOnlyList<string> ImageExtensions { get; init; } = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Extension used when the archived image URL carries none this app recognises.</summary>
    public string DefaultImageExtension { get; init; } = ".jpg";

    /// <summary>Provenance text appended to <c>Recipe.Description</c> at persist time.</summary>
    public string SeedAttributionNote { get; init; } = "Source: USDA MyPlate Kitchen";

    /// <summary>Start of MyPlate's related-foods link list inside the page notes, whose line is removed before the notes reach <c>Recipe.Description</c>, where empty disables the strip.</summary>
    public string NotesLinkListMarker { get; init; } = "Learn more about";

    /// <summary>File name prefix for seeded photos (<c>{prefix}{slug}{extension}</c>), deterministic so a <c>--force</c> re-import overwrites its own photo.</summary>
    public string SeedImageFilePrefix { get; init; } = "myplate-";

    /// <summary>Exclude recipes below the quality thresholds instead of only counting them.</summary>
    public bool ApplyQualityFilter { get; init; }

    /// <summary>Fewest ingredients a recipe may have and still pass the quality filter.</summary>
    public int QualityMinimumIngredients { get; init; } = 3;

    /// <summary>Fewest steps a recipe may have and still pass the quality filter.</summary>
    public int QualityMinimumSteps { get; init; } = 2;

    /// <summary>User agent sent to the Internet Archive, which identifies the client honestly rather than impersonating a browser.</summary>
    public string UserAgent { get; init; } = "RecipeApp/1.0 (recipe library seeder)";
}
