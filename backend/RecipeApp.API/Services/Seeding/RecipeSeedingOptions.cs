namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Configuration for the one-time USDA MyPlate seed import (Phase 9 §6).
///
/// <para>Every network endpoint is configurable because the source is an archive, not a live
/// site: if the Wayback Machine changes host or path shape, the import must be re-pointable
/// without a code change.</para>
/// </summary>
public class RecipeSeedingOptions
{
    public const string SectionName = "RecipeSeeding";

    /// <summary>
    /// Root of the on-disk cache. Relative paths resolve against the content root.
    ///
    /// <para>Phase 9 §5 names this <c>data/seed/myplate</c>, but Windows paths are
    /// case-insensitive and that collides with the project's existing <c>Data/</c> source folder —
    /// the harvest would land beside <c>AppDbContext.cs</c> and the EF migrations.</para>
    /// </summary>
    public string CacheDirectory { get; init; } = "seed-data/myplate";

    /// <summary>CDX index endpoint used for slug discovery (Stage 1).</summary>
    public string WaybackCdxUrl { get; init; } = "http://web.archive.org/cdx/search/cdx";

    /// <summary>Archived-content base URL. Snapshot URLs are <c>{this}/{timestamp}/{originalUrl}</c>.</summary>
    public string WaybackContentUrl { get; init; } = "http://web.archive.org/web";

    /// <summary>CDX wildcard identifying recipe pages in the archive.</summary>
    public string SourceUrlPattern { get; init; } = "myplate.gov/recipes/*";

    /// <summary>
    /// Path prefix a discovered URL must carry to count as a recipe page. Guards against index
    /// pages (<c>/recipes</c>), sibling sections (<c>/recipes-cookbooks-and-menus</c>) and
    /// localised variants (<c>/es/recipes/...</c>), all of which the CDX wildcard also returns.
    /// </summary>
    public string RecipePathPrefix { get; init; } = "/recipes/";

    /// <summary>Snapshot year to prefer per slug; falls back to the latest available capture.</summary>
    public int PreferredSnapshotYear { get; init; } = 2025;

    /// <summary>Per-page Wayback timeout — the archive is slow.</summary>
    public int FetchTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Budget for the one CDX index query, which returns the whole capture history for the
    /// pattern (~5 MB, ~35 s) rather than a single page.
    /// </summary>
    public int DiscoveryTimeoutSeconds { get; init; } = 300;

    /// <summary>Politeness delay between archive requests. Fetching is deliberately sequential.</summary>
    public int FetchDelayMilliseconds { get; init; } = 1500;

    /// <summary>Retries per page on 429/5xx/timeout, in addition to the first attempt.</summary>
    public int MaxFetchRetries { get; init; } = 3;

    /// <summary>Base backoff before the first retry; doubled on each subsequent attempt.</summary>
    public int RetryBackoffMilliseconds { get; init; } = 5000;

    /// <summary>
    /// Serving count assumed when a page publishes no <c>recipeYield</c>. Every page in the
    /// harvested corpus states one, so this is a guard rather than a routine path — but a recipe
    /// with no serving count cannot be scaled or shopped for, and inventing four is more useful
    /// than storing zero.
    /// </summary>
    public int DefaultServings { get; init; } = 4;

    /// <summary>Retries per recipe when LLM output fails the Stage 4 validation gate.</summary>
    public int MaxLlmRetries { get; init; } = 2;

    /// <summary>
    /// Budget for one Stage 4 completion. Longer than the scraper's because the model copies every
    /// step back out as well as extracting the ingredients, and the corpus reaches 19 steps.
    /// </summary>
    public int LlmTimeoutSeconds { get; init; } = 180;

    /// <summary>
    /// Token ceiling for one Stage 4 completion. The corpus's largest recipe is 18 ingredients and
    /// 19 steps; the default leaves roughly half again as much headroom.
    /// </summary>
    public int MaxLlmOutputTokens { get; init; } = 4096;

    /// <summary>
    /// Consecutive <i>systemic</i> recipe failures that stop a Stage 4 run. One bad recipe never
    /// aborts a run (§16), but an unloaded model, an exhausted GPU or a broken grammar fails every
    /// recipe identically, and grinding through a thousand of those helps nobody.
    ///
    /// <para>Only failures where no readable answer came back are counted — see
    /// <see cref="SeedNormaliseFailureExtensions.IsSystemic"/> for why counting content rejections
    /// here made a resumed corpus pass impossible to complete.</para>
    /// </summary>
    public int MaxConsecutiveLlmFailures { get; init; } = 5;

    /// <summary>Recipes imported by <c>--trial</c>.</summary>
    public int TrialSize { get; init; } = 20;

    /// <summary>
    /// Discovery aborts below this many slugs. A short manifest means the CDX filter rules have
    /// drifted against a changed archive, not that the archive is genuinely small — and silently
    /// importing a fraction of the library is worse than failing loudly (§8, §16).
    /// </summary>
    public int MinimumDiscoveredSlugs { get; init; } = 800;

    /// <summary>Whether to harvest recipe photos alongside the pages (Stage 2b).</summary>
    public bool DownloadImages { get; init; } = true;

    /// <summary>
    /// Image extensions accepted from an archived image URL. Anything else is stored under
    /// <see cref="DefaultImageExtension"/> rather than trusting a remote path to name a local file.
    /// </summary>
    public IReadOnlyList<string> ImageExtensions { get; init; } = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Extension used when the archived image URL carries none this app recognises.</summary>
    public string DefaultImageExtension { get; init; } = ".jpg";

    /// <summary>Provenance text appended to <c>Recipe.Description</c> at persist time.</summary>
    public string SeedAttributionNote { get; init; } = "Source: USDA MyPlate Kitchen (public domain)";

    /// <summary>
    /// User agent sent to the Internet Archive. Descriptive rather than browser-impersonating —
    /// the archive is a co-operative service and identifying the client honestly is the courtesy
    /// its rate limiter is built around.
    /// </summary>
    public string UserAgent { get; init; } = "RecipeApp/1.0 (recipe library seeder)";
}
