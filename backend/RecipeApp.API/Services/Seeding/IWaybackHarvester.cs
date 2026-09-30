namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stages 1, 2 and 2b of the seed import: discovering recipe slugs in the Internet Archive and
/// caching each snapshot's HTML and photo locally.
///
/// <para>Behind an interface so the rest of the pipeline can be tested without reaching the
/// network — CI must never depend on the Wayback Machine being up.</para>
/// </summary>
public interface IWaybackHarvester
{
    /// <summary>
    /// Stage 1. Queries the CDX index, applies the recipe-page filter rules, picks one snapshot
    /// per slug and writes <c>manifest.json</c>.
    /// </summary>
    /// <exception cref="SeedHarvestException">
    /// The index is unreachable, or the filtered yield is below
    /// <see cref="RecipeSeedingOptions.MinimumDiscoveredSlugs"/> — which means the filter rules
    /// have drifted, not that the archive shrank.
    /// </exception>
    Task<SeedDiscoveryResult> DiscoverAsync(CancellationToken ct = default);

    /// <summary>
    /// The cached manifest, discovering one first if the cache has none. Discovery is a slow,
    /// network-bound operation whose result is committed to git, so it is never repeated
    /// implicitly.
    /// </summary>
    Task<SeedManifest> EnsureManifestAsync(CancellationToken ct = default);

    /// <summary>
    /// Stages 2 and 2b. Downloads each manifest entry's archived page — and its photo when
    /// <see cref="RecipeSeedingOptions.DownloadImages"/> is set — into the cache, skipping
    /// anything already present.
    /// </summary>
    /// <param name="manifest">Entries to harvest, in manifest order.</param>
    /// <param name="limit">Maximum number of uncached pages to fetch this run; null for all.</param>
    Task<SeedHarvestResult> HarvestAsync(
        SeedManifest manifest, int? limit = null, CancellationToken ct = default);
}
