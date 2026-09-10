using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Drives the offline half of the seed import: the stages that run against the local cache rather
/// than the archive.
///
/// <para>Stages 3 (parse) and 4 (LLM normalise) are implemented. Stage 5 (persist) is not yet
/// built; when it is, it joins this interface — it walks the same manifest and shares the same
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

    /// <summary>
    /// Stage 4. Runs every parsed recipe through the LLM pass and the §11.3 validation gate,
    /// writing the admitted result to <c>normalised/</c>.
    /// </summary>
    /// <param name="manifest">Entries to normalise, in manifest order.</param>
    /// <param name="limit">Maximum number of recipes to normalise this run; null for all.</param>
    /// <param name="force">Re-run the LLM pass for recipes whose output is already cached.</param>
    Task<SeedNormaliseResult> NormaliseAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default);
}

/// <inheritdoc cref="IRecipeLibrarySeeder"/>
public class RecipeLibrarySeeder(
    SeedCacheStore cache,
    MyPlateRecipeParser parser,
    SeedRecipeNormaliser normaliser,
    IOptions<RecipeSeedingOptions> options,
    ILogger<RecipeLibrarySeeder> logger) : IRecipeLibrarySeeder
{
    private readonly RecipeSeedingOptions _options = options.Value;

    // ── Stage 3 ───────────────────────────────────────────────────────────────

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

    // ── Stage 4 ───────────────────────────────────────────────────────────────

    public async Task<SeedNormaliseResult> NormaliseAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default)
    {
        cache.EnsureDirectories();

        var state = await cache.LoadStateAsync(ct);

        var normalised = 0;
        var skipped    = 0;
        var notParsed  = 0;
        var retried    = 0;
        var stale      = 0;
        var aborted    = false;
        var failures   = new Dictionary<string, string>(StringComparer.Ordinal);

        // One bad recipe never aborts a run (§16) — but an unloaded model, an exhausted GPU or a
        // broken grammar fails every recipe the same way, and a run that has failed nothing else
        // is not isolating a bad recipe, it is broken.
        var consecutiveFailures = 0;

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
                    failures[entry.Slug] = "UnusableSlug: not safe to use as a cache filename";
                    continue;
                }

                var fingerprint = await cache.TryComputeParsedFingerprintAsync(entry.Slug, ct);
                if (fingerprint is null)
                {
                    notParsed++;
                    continue;
                }

                if (!force)
                {
                    var cached = cache.HasNormalised(entry.Slug)
                        ? await cache.TryLoadNormalisedAsync(entry.Slug, ct)
                        : null;

                    if (cached is not null)
                    {
                        if (cached.ParsedFingerprint == fingerprint)
                        {
                            skipped++;
                            continue;
                        }

                        // The page was re-parsed since this was produced, so the LLM answered a
                        // question that is no longer being asked. Redone rather than kept — silent
                        // staleness is the failure mode a cache between stages invites.
                        stale++;
                        logger.LogInformation(
                            "[{Position}/{Total}] {Slug} → re-normalising: the parsed recipe " +
                            "changed since this output was cached.", position, total, entry.Slug);
                    }
                }

                if (limit is { } cap && normalised >= cap) break;

                var parsed = await cache.TryLoadParsedAsync(entry.Slug, ct);
                if (parsed is null)
                {
                    // The fingerprint proved a file exists, so this is a corrupt one. Stage 3 will
                    // rewrite it for free; there is nothing for Stage 4 to spend a GPU pass on.
                    notParsed++;
                    continue;
                }

                var slugState = state.GetOrAdd(entry.Slug);

                try
                {
                    var recipe = await normaliser.NormaliseAsync(parsed, fingerprint, ct);
                    await cache.WriteNormalisedAsync(entry.Slug, recipe, ct);

                    slugState.Stage     = SeedStage.Normalised;
                    slugState.Attempts += recipe.Attempts;
                    slugState.LastError = null;

                    normalised++;
                    consecutiveFailures = 0;
                    if (recipe.Attempts > 1) retried++;

                    logger.LogInformation(
                        "[{Position}/{Total}] {Slug} → normalised ({Ingredients} ingredients, " +
                        "{Steps} steps, {Attempts} attempt(s))",
                        position, total, entry.Slug, recipe.Ingredients.Count, recipe.Steps.Count,
                        recipe.Attempts);
                }
                catch (SeedNormaliseException ex)
                {
                    // §11.3: after the final retry the recipe is excluded. A wrong recipe is worse
                    // than a missing one — an implausible quantity silently corrupts every shopping
                    // list it appears in.
                    failures[entry.Slug] = ex.StateReason;
                    slugState.Stage     = SeedStage.Failed;
                    slugState.LastError = ex.StateReason;
                    consecutiveFailures++;

                    logger.LogWarning("[{Position}/{Total}] {Slug} → {Reason}",
                        position, total, entry.Slug, ex.StateReason);
                }

                await cache.SaveStateAsync(ct);

                if (consecutiveFailures >= _options.MaxConsecutiveLlmFailures)
                {
                    aborted = true;
                    logger.LogError(
                        "Stopping after {Count} consecutive failures. A run that has failed " +
                        "nothing else is not isolating bad recipes — check Llm:Local:ModelPath and " +
                        "re-run; cached output is kept and the run resumes where it stopped.",
                        consecutiveFailures);
                    break;
                }
            }
        }
        finally
        {
            await cache.SaveStateAsync(CancellationToken.None);
        }

        if (notParsed > 0)
            logger.LogWarning(
                "{Count} manifest entries have no parsed recipe. Run 'seed-recipes --parse' first.",
                notParsed);

        return new SeedNormaliseResult
        {
            Normalised = normalised,
            Skipped    = skipped,
            NotParsed  = notParsed,
            Failed     = failures.Count,
            Retried    = retried,
            Stale      = stale,
            Aborted    = aborted,
            Failures   = failures,
        };
    }
}
