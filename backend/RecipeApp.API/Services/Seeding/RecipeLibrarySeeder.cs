namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Drives the offline half of the seed import: the stages that run against the local cache rather
/// than the archive.
///
/// <para>Stage 3 (parse) is implemented. Stages 4 (LLM normalise) and 5 (persist) are not yet
/// built; when they are, they join this interface — they walk the same manifest and share the same
/// per-slug isolation and resume behaviour.</para>
/// </summary>
public interface IRecipeLibrarySeeder
{
    /// <summary>
    /// Stage 3. Turns every cached page into a <see cref="ParsedSeedRecipe"/> under
    /// <c>parsed/</c>, skipping recipes already parsed.
    /// </summary>
    /// <param name="manifest">Entries to parse, in manifest order.</param>
    /// <param name="limit">Maximum number of unparsed recipes to handle this run; null for all.</param>
    /// <param name="force">Re-parse recipes whose output is already cached.</param>
    Task<SeedParseResult> ParseAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default);
}

/// <inheritdoc cref="IRecipeLibrarySeeder"/>
public class RecipeLibrarySeeder(
    SeedCacheStore cache,
    MyPlateRecipeParser parser,
    ILogger<RecipeLibrarySeeder> logger) : IRecipeLibrarySeeder
{
    public async Task<SeedParseResult> ParseAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default)
    {
        cache.EnsureDirectories();

        // Progress is held in memory and flushed once. The harvest flushes after every page because
        // each one costs a couple of seconds of network and losing it means re-downloading; parsing
        // the whole corpus takes seconds, and the resume guarantee comes from the parsed/ files
        // themselves — state.json is derived bookkeeping either way (§5.2).
        var state = await cache.LoadStateAsync(ct);

        var parsed = 0;
        var skipped = 0;
        var notHarvested = 0;
        var primary = 0;
        var legacy = 0;
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);

        var total = manifest.Recipes.Count;
        var position = 0;

        try
        {
            foreach (var entry in manifest.Recipes)
            {
                ct.ThrowIfCancellationRequested();
                position++;

                if (!SeedCacheStore.IsValidSlug(entry.Slug))
                {
                    // Discovery rejects these, so reaching here means a hand-edited manifest.
                    // Recording it beats letting ValidateSlug throw and take the run down.
                    failures[entry.Slug] = "UnusableSlug: not safe to use as a cache filename";
                    continue;
                }

                if (!cache.HasRaw(entry.Slug))
                {
                    notHarvested++;
                    continue;
                }

                if (!force && cache.HasParsed(entry.Slug))
                {
                    skipped++;
                    continue;
                }

                if (limit is { } cap && parsed >= cap) break;

                var slugState = state.GetOrAdd(entry.Slug);
                slugState.Attempts++;

                try
                {
                    var html   = await cache.ReadRawAsync(entry.Slug, ct);
                    var recipe = parser.Parse(html, entry.Slug, entry.OriginalUrl);

                    await cache.WriteParsedAsync(entry.Slug, recipe, ct);

                    slugState.Stage     = SeedStage.Parsed;
                    slugState.LastError = null;

                    parsed++;
                    if (recipe.Template == MyPlateTemplate.Primary) primary++; else legacy++;

                    logger.LogDebug(
                        "[{Position}/{Total}] {Slug} → parsed ({Ingredients} ingredients, " +
                        "{Steps} steps, {Template} template)",
                        position, total, entry.Slug, recipe.Ingredients.Count, recipe.Steps.Count,
                        recipe.Template);
                }
                catch (Exception ex) when (ex is SeedParseException or IOException)
                {
                    // §10.4: a page that cannot be read is recorded and skipped. One bad page must
                    // not abort a thousand-recipe run.
                    var reason = ex is SeedParseException parseFailure
                        ? parseFailure.StateReason
                        : $"ParseFailed: cached page unreadable — {ex.Message}";

                    failures[entry.Slug] = reason;
                    slugState.Stage     = SeedStage.Failed;
                    slugState.LastError = reason;

                    logger.LogWarning("[{Position}/{Total}] {Slug} → {Reason}",
                        position, total, entry.Slug, reason);
                }
            }
        }
        finally
        {
            // Cancellation still flushes: Ctrl+C part-way through must leave the run resumable
            // rather than discarding what it managed to parse.
            await cache.SaveStateAsync(CancellationToken.None);
        }

        if (notHarvested > 0)
            logger.LogWarning(
                "{Count} manifest entries have no cached page. Run 'seed-recipes --harvest' first.",
                notHarvested);

        return new SeedParseResult
        {
            Parsed          = parsed,
            Skipped         = skipped,
            NotHarvested    = notHarvested,
            Failed          = failures.Count,
            PrimaryTemplate = primary,
            LegacyTemplate  = legacy,
            Failures        = failures,
        };
    }
}
