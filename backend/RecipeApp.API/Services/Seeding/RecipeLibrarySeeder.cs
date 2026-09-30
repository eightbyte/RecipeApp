using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Drives the offline half of the seed import: the stages that run against the local cache rather
/// than the archive.
///
/// <para>Stages 3 (parse), 4 (LLM normalise) and 5 (persist). All three walk the same manifest and
/// share the same per-slug isolation and resume behaviour.</para>
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

    /// <summary>
    /// Stage 5. Persists every normalised recipe through <c>ConfirmAsync</c>, skipping recipes whose
    /// <c>SourceUrl</c> is already in the database. No LLM, and the catalogue must already be loaded.
    /// </summary>
    /// <param name="manifest">Entries to persist, in manifest order.</param>
    /// <param name="limit">Maximum number of recipes to persist this run; null for all.</param>
    /// <param name="force">Replace recipes already in the database rather than skipping them.</param>
    Task<SeedPersistResult> PersistAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default);
}

/// <inheritdoc cref="IRecipeLibrarySeeder"/>
public class RecipeLibrarySeeder(
    SeedCacheStore cache,
    MyPlateRecipeParser parser,
    SeedRecipeNormaliser normaliser,
    IServiceScopeFactory scopeFactory,
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
        // broken grammar fails every recipe the same way, and grinding through a thousand of those
        // helps nobody.
        //
        // Only a systemic failure counts (SeedNormaliseFailureExtensions.IsSystemic). Counting
        // content rejections here wedged the corpus pass completely: cached successes are skipped
        // before this counter is reached, so on a resumed run every recipe attempted below the
        // high-water mark is one that already failed, and those failures reproduce. The guard fired
        // on the first five of a thirty-recipe backlog and stopped at manifest position 77 with
        // nothing normalised, never reaching the 744 recipes never tried — every re-run identically.
        var consecutiveSystemicFailures = 0;

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
                    consecutiveSystemicFailures = 0;
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
                    slugState.Attempts += ex.Attempts;
                    slugState.LastError = ex.StateReason;

                    // A content rejection means the model answered, the grammar held and the gate
                    // disagreed with the answer — evidence the pipeline works, never evidence it is
                    // broken. It leaves the counter alone rather than resetting it, exactly as a
                    // skip does, so an interleaved bad recipe cannot mask a model that is genuinely
                    // unavailable.
                    if (ex.Failure.IsSystemic()) consecutiveSystemicFailures++;

                    logger.LogWarning("[{Position}/{Total}] {Slug} → {Reason}",
                        position, total, entry.Slug, ex.StateReason);
                }

                await cache.SaveStateAsync(ct);

                if (consecutiveSystemicFailures >= _options.MaxConsecutiveLlmFailures)
                {
                    aborted = true;
                    logger.LogError(
                        "Stopping after {Count} consecutive failures in which no answer came back " +
                        "at all — the model is unavailable rather than the recipes being bad. Check " +
                        "Llm:Local:ModelPath and re-run; cached output is kept and the run resumes " +
                        "where it stopped.",
                        consecutiveSystemicFailures);
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

    // ── Stage 5 ───────────────────────────────────────────────────────────────

    public async Task<SeedPersistResult> PersistAsync(
        SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default)
    {
        cache.EnsureDirectories();

        var state = await cache.LoadStateAsync(ct);

        // Loaded once per run, not once per recipe: the catalogue does not change during Stage 5 —
        // that is the point of Phase 9.3 — so every recipe resolves against the same dictionary.
        SeedCatalogueResolver.CatalogueLookup lookup;
        HashSet<string> persistedUrls;
        using (var runScope = scopeFactory.CreateScope())
        {
            lookup = await runScope.ServiceProvider.GetRequiredService<SeedCatalogueResolver>().LoadAsync(ct);

            // §13: SourceUrl is the natural key. Read up front so re-running an imported library is
            // a pass over files, not a thousand queries.
            var db = runScope.ServiceProvider.GetRequiredService<AppDbContext>();
            persistedUrls = (await db.Recipes
                    .Where(recipe => recipe.SourceUrl != null)
                    .Select(recipe => recipe.SourceUrl!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
        }

        if (lookup.Count == 0)
            throw new SeedHarvestException(
                "The ingredient catalogue is empty, so no recipe can resolve its ingredients. Run " +
                "'dotnet run -- import-catalogue' first.");

        var persisted       = 0;
        var replaced        = 0;
        var alreadyPresent  = 0;
        var notNormalised   = 0;
        var filteredOut     = 0;
        var belowThreshold  = 0;
        var rowsRemoved     = 0;
        var amountsDropped  = 0;
        var withoutImage    = 0;
        var failures        = new Dictionary<string, string>(StringComparer.Ordinal);
        var staleSkipLines  = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var total    = manifest.Recipes.Count;
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

                var recipe = cache.HasNormalised(entry.Slug)
                    ? await cache.TryLoadNormalisedAsync(entry.Slug, ct)
                    : null;

                if (recipe is null)
                {
                    notNormalised++;
                    continue;
                }

                var slugState = state.GetOrAdd(entry.Slug);

                var exists = persistedUrls.Contains(recipe.SourceUrl);
                if (exists && !force)
                {
                    alreadyPresent++;
                    slugState.Stage     = SeedStage.Persisted;
                    slugState.LastError = null;
                    continue;
                }

                if (limit is { } cap && persisted >= cap) break;

                try
                {
                    var parsed      = await cache.TryLoadParsedAsync(entry.Slug, ct);
                    var fingerprint = await cache.TryComputeParsedFingerprintAsync(entry.Slug, ct);
                    if (parsed is null || fingerprint is null)
                        throw new SeedPersistException(SeedPersistFailure.StaleArtefact,
                            "no readable parsed recipe to check the artefact against; re-run --parse");

                    // A fresh scope per recipe: a failed save leaves its DbContext unusable, and one
                    // bad recipe must never take the rest of the run with it (§16).
                    using var scope  = scopeFactory.CreateScope();
                    var persister    = scope.ServiceProvider.GetRequiredService<SeedRecipePersister>();
                    var prepared     = persister.Prepare(recipe, parsed, fingerprint, lookup);
                    var (wasReplaced, hasImage) =
                        await persister.PersistAsync(entry.Slug, prepared, replaceExisting: exists, ct);

                    persistedUrls.Add(recipe.SourceUrl);
                    persisted++;
                    if (wasReplaced) replaced++;
                    if (!hasImage) withoutImage++;
                    if (prepared.BelowQualityThreshold) belowThreshold++;
                    rowsRemoved    += prepared.NonIngredientRowsRemoved;
                    amountsDropped += prepared.AmountsDropped;
                    if (prepared.StaleSkipListLines.Count > 0)
                        staleSkipLines[entry.Slug] = prepared.StaleSkipListLines;

                    slugState.Stage     = SeedStage.Persisted;
                    slugState.LastError = null;

                    logger.LogInformation(
                        "[{Position}/{Total}] {Slug} → {Action} ({Ingredients} ingredients, {Steps} steps{Image})",
                        position, total, entry.Slug, wasReplaced ? "replaced" : "persisted",
                        prepared.Request.Ingredients.Count, prepared.Request.Steps.Count,
                        hasImage ? "" : ", no photo");
                }
                catch (Exception ex) when (ex is SeedPersistException or DbUpdateException or IOException)
                {
                    var reason = ex switch
                    {
                        SeedPersistException persistFailure => persistFailure.StateReason,
                        DbUpdateException => $"PersistFailed: database rejected the recipe — {ex.GetBaseException().Message}",
                        _ => $"PersistFailed: photo could not be copied — {ex.Message}",
                    };

                    if (ex is SeedPersistException { Failure: SeedPersistFailure.BelowQualityThreshold })
                        filteredOut++;
                    else
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
            await cache.SaveStateAsync(CancellationToken.None);
        }

        if (notNormalised > 0)
            logger.LogWarning(
                "{Count} manifest entries have no normalised recipe. Run 'seed-recipes --normalise' first.",
                notNormalised);

        return new SeedPersistResult
        {
            Persisted                = persisted,
            Replaced                 = replaced,
            AlreadyPersisted         = alreadyPresent,
            NotNormalised            = notNormalised,
            Failed                   = failures.Count,
            FilteredOut              = filteredOut,
            BelowQualityThreshold    = belowThreshold,
            NonIngredientRowsRemoved = rowsRemoved,
            AmountsDropped           = amountsDropped,
            WithoutImage             = withoutImage,
            StaleSkipListLines       = staleSkipLines,
            Failures                 = failures,
        };
    }
}
