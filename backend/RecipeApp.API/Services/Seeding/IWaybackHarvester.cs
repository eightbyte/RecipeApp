namespace RecipeApp.API.Services.Seeding;

/// <summary>Stages 1, 2 and 2b of the seed import, behind an interface so the rest of the pipeline can be tested without reaching the network.</summary>
public interface IWaybackHarvester
{
    /// <summary>Stage 1: queries the CDX index, applies the recipe-page filter rules, picks one snapshot per slug and writes <c>manifest.json</c>.</summary>
    /// <exception cref="SeedHarvestException">The index is unreachable or the filtered yield is below <see cref="RecipeSeedingOptions.MinimumDiscoveredSlugs"/>, meaning the filter rules have drifted.</exception>
    Task<SeedDiscoveryResult> DiscoverAsync(CancellationToken ct = default);

    /// <summary>The cached manifest, discovering one first if the cache has none, since discovery is slow and its result is committed so it is never repeated implicitly.</summary>
    Task<SeedManifest> EnsureManifestAsync(CancellationToken ct = default);

    /// <summary>Stages 2 and 2b: downloads each manifest entry's archived page, and its photo when <see cref="RecipeSeedingOptions.DownloadImages"/> is set, skipping anything already cached.</summary>
    Task<SeedHarvestResult> HarvestAsync(
        SeedManifest manifest, int? limit = null, CancellationToken ct = default);
}
