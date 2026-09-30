using RecipeApp.API.Data;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The <c>seed-recipes</c> CLI command (Phase 9 §15). Parses its own argument vector — everything
/// after the <c>seed-recipes</c> token — and drives the harvest stages, then exits without
/// starting Kestrel.
///
/// <para>Every stage is implemented. Each has its own flag, and no stage flag at all runs the full
/// pipeline — every stage in order, each resuming from its cache, so a complete cache makes the
/// earlier stages no-ops and the run costs only the database writes.</para>
/// </summary>
public static class SeedRecipesCommand
{
    public const string CommandName = "seed-recipes";

    /// <summary>Exit code for a usage error or an aborted run.</summary>
    private const int FailureExitCode = 1;

    /// <summary>
    /// Index of the command token in the process argument vector, or -1 when the command was not
    /// requested. Everything after it belongs to this command, not to host configuration.
    /// </summary>
    public static int IndexIn(string[] args) => Array.IndexOf(args, CommandName);

    public static bool IsRequestedIn(string[] args) => IndexIn(args) >= 0;

    // ── Arguments ─────────────────────────────────────────────────────────────

    public record SeedRecipesArguments
    {
        /// <summary>Stage 1 only — rebuild the manifest and report the yield.</summary>
        public bool Discover { get; init; }

        /// <summary>Stages 1–2 — download and cache pages and images, no LLM.</summary>
        public bool Harvest { get; init; }

        /// <summary>Stage 3 — parse cached pages into recipes, no LLM and no database.</summary>
        public bool Parse { get; init; }

        /// <summary>Stage 4 — run parsed recipes through the LLM pass. Needs a model, not a database.</summary>
        public bool Normalise { get; init; }

        /// <summary>
        /// Stage 4.5 — derive the committed ingredient catalogue from <c>normalised/</c>.
        /// Needs a model, not a database (Phase 9.3 §4.3).
        /// </summary>
        public bool BuildCatalogue { get; init; }

        /// <summary>
        /// Stage 5 — persist normalised recipes to the database. No LLM; migrates and imports the
        /// catalogue first so the catalogue → densities → recipes order cannot be skipped.
        /// </summary>
        public bool Persist { get; init; }

        /// <summary>Print the cached progress summary and do no work.</summary>
        public bool Report { get; init; }

        /// <summary>Discard cached pages and images before harvesting.</summary>
        public bool RefreshCache { get; init; }

        /// <summary>
        /// Restrict the run to the pinned stratified subset (<see cref="SeedTrialSelection"/>). Alone it
        /// runs the full pipeline over those recipes; with <c>--persist</c>, only Stage 5.
        /// </summary>
        public bool Trial { get; init; }

        /// <summary>Redo work already done — re-parse cached recipes, re-import persisted ones.</summary>
        public bool Force { get; init; }

        /// <summary>Cap on how many recipes this run processes.</summary>
        public int? Limit { get; init; }

        /// <summary>
        /// Restrict the run to these slugs. Empty means the whole manifest.
        ///
        /// <para>Exists for §13's prompt-iteration loop: re-running one named recipe is the
        /// difference between a twenty-second experiment and a ten-minute one, and Stage 4's
        /// failures cluster by ingredient-line shape rather than by manifest position.</para>
        /// </summary>
        public IReadOnlyList<string> Slugs { get; init; } = [];

        /// <summary>
        /// With <c>--build-catalogue</c>: run the grouping pass on these batches only (1-based), log
        /// what each one merged, and write nothing. Empty means a real build.
        ///
        /// <para><c>--slug</c>'s counterpart for Stage 4.5. A full build is 45 calls and most of an
        /// hour, while the grouping prompt's failures cluster in a handful of family-dense batches —
        /// the cereals batch collapsed 17 names to 2 on two different prompts.</para>
        /// </summary>
        public IReadOnlyList<int> CatalogueBatches { get; init; } = [];

        /// <summary>Whether no single stage was named, so every stage runs in order.</summary>
        public bool RunsFullPipeline =>
            !Discover && !Harvest && !Parse && !Normalise && !BuildCatalogue && !Persist && !Report;
    }

    /// <summary>Parses the command's own arguments, or returns null after reporting a usage error.</summary>
    public static SeedRecipesArguments? TryParse(string[] commandArgs, ILogger logger)
    {
        var arguments = new SeedRecipesArguments();

        for (var index = 0; index < commandArgs.Length; index++)
        {
            switch (commandArgs[index])
            {
                case "--discover":      arguments = arguments with { Discover = true }; break;
                case "--harvest":       arguments = arguments with { Harvest = true }; break;
                case "--parse":         arguments = arguments with { Parse = true }; break;
                case "--normalise":     arguments = arguments with { Normalise = true }; break;
                case "--build-catalogue": arguments = arguments with { BuildCatalogue = true }; break;
                case "--persist":       arguments = arguments with { Persist = true }; break;
                case "--report":        arguments = arguments with { Report = true }; break;
                case "--refresh-cache": arguments = arguments with { RefreshCache = true }; break;
                case "--trial":         arguments = arguments with { Trial = true }; break;
                case "--force":         arguments = arguments with { Force = true }; break;

                case "--slug":
                    if (index + 1 >= commandArgs.Length
                        || !SeedCacheStore.IsValidSlug(commandArgs[index + 1]))
                    {
                        logger.LogError(
                            "--slug requires a recipe slug, e.g. --slug apple-carrot-soup.");
                        return null;
                    }
                    arguments = arguments with { Slugs = [.. arguments.Slugs, commandArgs[index + 1]] };
                    index++;
                    break;

                case "--limit":
                    if (index + 1 >= commandArgs.Length
                        || !int.TryParse(commandArgs[index + 1], out var limit)
                        || limit <= 0)
                    {
                        logger.LogError("--limit requires a positive whole number, e.g. --limit 50.");
                        return null;
                    }
                    arguments = arguments with { Limit = limit };
                    index++;
                    break;

                case "--batch":
                    if (index + 1 >= commandArgs.Length
                        || !int.TryParse(commandArgs[index + 1], out var batch)
                        || batch <= 0)
                    {
                        logger.LogError("--batch requires a positive batch number, e.g. --batch 7.");
                        return null;
                    }
                    arguments = arguments with { CatalogueBatches = [.. arguments.CatalogueBatches, batch] };
                    index++;
                    break;

                default:
                    logger.LogError("Unrecognised argument '{Argument}'.\n{Usage}", commandArgs[index], Usage);
                    return null;
            }
        }

        // A batch number means nothing to any other stage, and silently ignoring it would run a
        // full, hour-long build that the caller asked to preview.
        if (arguments.CatalogueBatches.Count > 0 && !arguments.BuildCatalogue)
        {
            logger.LogError("--batch only applies to --build-catalogue.");
            return null;
        }

        // Both name the recipes to run on; honouring one silently would run the wrong set.
        if (arguments.Trial && arguments.Slugs.Count > 0)
        {
            logger.LogError("--trial and --slug each choose the recipes to run; use one or the other.");
            return null;
        }

        return arguments;
    }

    private const string Usage = """
        Usage: dotnet run -- seed-recipes [options]

          --discover        Stage 1 only — build the manifest and report the slug count
          --harvest         Stages 1-2 — download and cache pages and images, no LLM
          --parse           Stage 3 — parse cached pages into recipes, offline
          --normalise       Stage 4 — LLM quantity extraction and step linkage
          --build-catalogue Stage 4.5 — derive seed-data/ingredient-catalogue.json from
                            normalised/. Run once, review the result, commit it.
          --batch <n>       With --build-catalogue: group only batch n, log its merges and
                            write nothing; repeatable
          --persist         Stage 5 — write normalised recipes to the database, no LLM
          --report          Print the cached progress summary and do no work
          --refresh-cache   Discard cached pages and images, then re-harvest
          --limit <n>       Process at most n recipes this run
          --slug <name>     Restrict the run to this recipe; repeatable
          --trial           Restrict the run to the 20 pinned trial recipes
          --force           Redo work already done (re-parse, re-normalise, re-import)

        With no stage flag, every stage runs in order, each resuming from its cache.
        --force then applies to Stage 5 only: it re-imports recipes already in the
        database and never discards a cached page, parse or LLM pass.
        """;

    // ── Execution ─────────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(
        IServiceProvider services, ILogger logger, string[] commandArgs, CancellationToken ct = default)
    {
        var arguments = TryParse(commandArgs, logger);
        if (arguments is null) return FailureExitCode;

        var harvester = services.GetRequiredService<IWaybackHarvester>();
        var cache     = services.GetRequiredService<SeedCacheStore>();

        try
        {
            if (arguments.Report)
                return await ReportAsync(cache, logger, ct);

            if (arguments.RefreshCache)
                await cache.ClearFetchedContentAsync(ct);

            if (arguments.Discover)
                return await DiscoverAsync(harvester, logger, ct);

            if (arguments.Harvest || arguments.RefreshCache)
                return await HarvestAsync(harvester, logger, arguments.Limit, ct);

            if (arguments.Parse)
                return await ParseAsync(services, harvester, cache, logger, arguments, ct);

            if (arguments.Normalise)
                return await NormaliseAsync(services, harvester, cache, logger, arguments, ct);

            if (arguments.BuildCatalogue)
                return await BuildCatalogueAsync(services, harvester, logger, arguments, ct);

            if (arguments.Persist)
                return await PersistAsync(services, harvester, logger, arguments, ct);

            return await RunFullPipelineAsync(services, harvester, cache, logger, arguments, ct);
        }
        catch (SeedHarvestException ex)
        {
            logger.LogError("Seed harvest aborted: {Message}", ex.Message);
            return FailureExitCode;
        }
        catch (CatalogueValidationException ex)
        {
            // Nothing was written. A catalogue that contradicts itself is worse than no catalogue,
            // and every consumer downstream treats this file as authoritative (§4.7).
            logger.LogError("Catalogue build aborted: {Message}", ex.Message);
            return FailureExitCode;
        }
        catch (Exception ex) when (ex is FileNotFoundException or Npgsql.NpgsqlException)
        {
            // Only Stage 5 reaches either: the catalogue artefact is missing, or Postgres is down.
            logger.LogError("Seed persist aborted: {Message}", ex.Message);
            if (ex is Npgsql.NpgsqlException)
                logger.LogError("Is the database running? 'docker compose up -d postgres'.");
            return FailureExitCode;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Seed run cancelled. Progress is cached — re-run to resume.");
            return FailureExitCode;
        }
    }

    private static async Task<int> DiscoverAsync(
        IWaybackHarvester harvester, ILogger logger, CancellationToken ct)
    {
        var result = await harvester.DiscoverAsync(ct);

        logger.LogInformation(
            "Discovery complete: {Slugs} recipe slugs from {Rows} CDX rows.",
            result.Manifest.Recipes.Count, result.RawRowCount);

        foreach (var (reason, count) in result.DroppedByReason.OrderByDescending(pair => pair.Value))
            logger.LogInformation("  dropped {Count,6} — {Reason}", count, reason);

        return 0;
    }

    private static async Task<int> HarvestAsync(
        IWaybackHarvester harvester, ILogger logger, int? limit, CancellationToken ct)
    {
        var manifest = await harvester.EnsureManifestAsync(ct);
        var result   = await harvester.HarvestAsync(manifest, limit, ct);

        logger.LogInformation(
            "Harvest complete: {Fetched} fetched, {Skipped} already cached, {Failed} failed " +
            "(of {Total} in the manifest).",
            result.PagesFetched, result.PagesSkipped, result.PagesFailed, manifest.Recipes.Count);

        logger.LogInformation(
            "Images: {Fetched} fetched, {Skipped} skipped, {Failed} failed.",
            result.ImagesFetched, result.ImagesSkipped, result.ImagesFailed);

        foreach (var (slug, error) in result.Failures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            logger.LogWarning("  {Slug}: {Error}", slug, error);

        return 0;
    }

    private static async Task<int> ParseAsync(
        IServiceProvider services,
        IWaybackHarvester harvester,
        SeedCacheStore cache,
        ILogger logger,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        var seeder = services.GetRequiredService<IRecipeLibrarySeeder>();

        // --force here means "re-derive", not "re-download": the cached pages are untouched, which
        // is the whole point of caching between stages 2 and 3. Naming slugs scopes it to them —
        // the seeder redoes a forced recipe whether or not its old output was deleted first, so
        // wiping the other thousand would be pure collateral damage.
        if (arguments.Force && arguments.Slugs.Count == 0)
            await cache.ClearParsedContentAsync(ct);

        var manifest = Restrict(await harvester.EnsureManifestAsync(ct), arguments.Slugs, logger);
        var result   = await seeder.ParseAsync(manifest, arguments.Limit, arguments.Force, ct);

        logger.LogInformation(
            "Parse complete: {Parsed} parsed, {Skipped} already parsed, {Failed} failed " +
            "(of {Total} in the manifest).",
            result.Parsed, result.Skipped, result.Failed, manifest.Recipes.Count);

        logger.LogInformation("Templates: {Primary} primary, {Legacy} legacy.",
            result.PrimaryTemplate, result.LegacyTemplate);

        if (result.NotHarvested > 0)
            logger.LogWarning("{Count} recipes have no cached page yet.", result.NotHarvested);

        foreach (var (slug, error) in result.Failures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            logger.LogWarning("  {Slug}: {Error}", slug, error);

        return 0;
    }

    /// <summary>
    /// The manifest narrowed to the requested slugs, or unchanged when none were named. A slug that
    /// is not in the manifest is reported rather than silently yielding an empty run.
    /// </summary>
    internal static SeedManifest Restrict(
        SeedManifest manifest, IReadOnlyList<string> slugs, ILogger logger)
    {
        if (slugs.Count == 0) return manifest;

        var wanted   = new HashSet<string>(slugs, StringComparer.Ordinal);
        var selected = manifest.Recipes.Where(entry => wanted.Contains(entry.Slug)).ToList();

        foreach (var missing in wanted.Except(selected.Select(entry => entry.Slug)))
            logger.LogWarning("'{Slug}' is not in the manifest and will be skipped.", missing);

        return manifest with { Recipes = selected };
    }

    private static async Task<int> NormaliseAsync(
        IServiceProvider services,
        IWaybackHarvester harvester,
        SeedCacheStore cache,
        ILogger logger,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        var seeder = services.GetRequiredService<IRecipeLibrarySeeder>();

        // --force here throws away a GPU pass, not a harvest: the cached pages and parsed recipes
        // are untouched, which is what makes prompt iteration cheap (§13). Scoped to the named
        // slugs when there are any — `--normalise --force --slug x` is the iteration loop itself,
        // and it must not cost the other thousand recipes hours of inference.
        if (arguments.Force && arguments.Slugs.Count == 0)
            await cache.ClearNormalisedContentAsync(ct);

        var manifest = Restrict(await harvester.EnsureManifestAsync(ct), arguments.Slugs, logger);
        var result   = await seeder.NormaliseAsync(manifest, arguments.Limit, arguments.Force, ct);

        logger.LogInformation(
            "Normalise complete: {Normalised} normalised, {Skipped} already current, " +
            "{Failed} failed (of {Total} in the manifest).",
            result.Normalised, result.Skipped, result.Failed, manifest.Recipes.Count);

        if (result.Retried > 0)
            logger.LogInformation(
                "{Count} recipes needed more than one attempt — the prompt is worth a look.",
                result.Retried);

        if (result.Stale > 0)
            logger.LogInformation(
                "{Count} recipes were re-normalised because their parsed input had changed.",
                result.Stale);

        if (result.NotParsed > 0)
            logger.LogWarning("{Count} recipes have no parsed recipe yet.", result.NotParsed);

        foreach (var (slug, error) in result.Failures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            logger.LogWarning("  {Slug}: {Error}", slug, error);

        // An aborted run is not a successful one, even though everything it did is cached.
        return result.Aborted ? FailureExitCode : 0;
    }

    /// <summary>
    /// Stage 4.5 (Phase 9.3). Derives the committed ingredient catalogue from the normalised corpus.
    ///
    /// <para>Runs once, on whoever has the GPU; its output is reviewed and committed, and every
    /// other machine reads the file. <c>--slug</c> deliberately does not narrow it: a catalogue
    /// derived from part of the corpus cannot resolve the rest of it, which is the failure §4.7
    /// rule 5 exists to catch.</para>
    /// </summary>
    private static async Task<int> BuildCatalogueAsync(
        IServiceProvider services,
        IWaybackHarvester harvester,
        ILogger logger,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        if (arguments.Slugs.Count > 0)
        {
            logger.LogError(
                "--build-catalogue reads the whole corpus and cannot be narrowed with --slug. " +
                "Every recipe's ingredient names must be reachable or Stage 5 cannot persist them.");
            return FailureExitCode;
        }

        var builder  = services.GetRequiredService<IngredientCatalogueBuilder>();
        var manifest = await harvester.EnsureManifestAsync(ct);

        if (arguments.CatalogueBatches.Count > 0)
        {
            var preview = await builder.PreviewAsync(manifest, arguments.CatalogueBatches, ct);

            logger.LogInformation(
                "Preview: {Names} names → {Groups} groups over {Batches} of {Total} batches " +
                "({Calls} model calls). Nothing was written.",
                preview.Names, preview.Groups, preview.BatchesRun, preview.TotalBatches, preview.LlmCalls);

            return 0;
        }

        var result = await builder.BuildAsync(manifest, ct);

        if (!result.Succeeded)
        {
            logger.LogError(
                "Catalogue build failed {Count} validation check(s); nothing was written.",
                result.ValidationErrors.Count);
            return FailureExitCode;
        }

        logger.LogInformation(
            "Catalogue built: {Entries} entries from {Names} distinct names over {Rows} rows in " +
            "{Recipes} recipes ({Calls} model calls).",
            result.Entries, result.DistinctNames, result.IngredientRows, result.Recipes, result.LlmCalls);

        if (result.NamesRecoveredFromGaps > 0)
            logger.LogWarning(
                "{Count} names were not grouped by the model and became single-name entries.",
                result.NamesRecoveredFromGaps);

        logger.LogInformation(
            "Written to {Path}. Review it against the merge rules, then commit it.", result.Path);

        return 0;
    }

    /// <summary>
    /// The recipes this invocation runs on: the pinned trial subset, the named slugs, or the whole
    /// manifest.
    /// </summary>
    internal static SeedManifest SelectRecipes(
        SeedManifest manifest, SeedRecipesArguments arguments, ILogger logger) =>
        Restrict(manifest, arguments.Trial ? SeedTrialSelection.Slugs : arguments.Slugs, logger);

    /// <summary>Stage 5 alone. No LLM — the model is never loaded.</summary>
    private static async Task<int> PersistAsync(
        IServiceProvider services,
        IWaybackHarvester harvester,
        ILogger logger,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        var manifest = SelectRecipes(await harvester.EnsureManifestAsync(ct), arguments, logger);
        return await PersistSelectedAsync(services, logger, manifest, arguments, ct);
    }

    private static async Task<int> PersistSelectedAsync(
        IServiceProvider services,
        ILogger logger,
        SeedManifest manifest,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        // Catalogue → densities → recipes (Phase 9.3 §4.5), enforced by running the first two here
        // rather than trusting that import-catalogue was run. Idempotent: an imported catalogue only
        // has nulls filled and newly committed aliases merged in, and --force never reaches it —
        // re-importing recipes must not discard someone's catalogue corrections.
        await CatalogueImport.RunAsync(services, logger, force: false, ct);

        var seeder = services.GetRequiredService<IRecipeLibrarySeeder>();
        var result = await seeder.PersistAsync(manifest, arguments.Limit, arguments.Force, ct);

        logger.LogInformation(
            "Persist complete: {Persisted} persisted ({Replaced} replacing an existing recipe), " +
            "{Already} already in the database, {Failed} failed (of {Total} selected).",
            result.Persisted, result.Replaced, result.AlreadyPersisted, result.Failed, manifest.Recipes.Count);

        logger.LogInformation(
            "Adjustments: {Rows} heading/note rows removed, {Dropped} unexplained amounts stored " +
            "unquantified, {NoPhoto} recipes without a photo.",
            result.NonIngredientRowsRemoved, result.AmountsDropped, result.WithoutImage);

        if (result.FilteredOut > 0)
            logger.LogInformation("{Count} recipes excluded by the quality filter.", result.FilteredOut);

        if (result.BelowQualityThreshold > 0)
            logger.LogInformation(
                "{Count} persisted recipes fall below the quality thresholds (filter is off).",
                result.BelowQualityThreshold);

        if (result.NotNormalised > 0)
            logger.LogWarning("{Count} recipes have no normalised recipe yet.", result.NotNormalised);

        foreach (var (slug, lines) in result.StaleSkipListLines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            logger.LogWarning(
                "  {Slug}: skip-list lines matched nothing — remove them from SeedNonIngredientRows: {Lines}",
                slug, string.Join(" | ", lines));

        foreach (var (slug, error) in result.Failures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            logger.LogWarning("  {Slug}: {Error}", slug, error);

        return 0;
    }

    /// <summary>
    /// Every stage in order over the selected recipes. Each stage resumes from its own cache, so
    /// against a complete cache the harvest makes no request, the normalise pass calls no model, and
    /// the run is the database writes alone.
    ///
    /// <para><c>--limit</c> and <c>--force</c> reach Stage 5 only (§13): a re-import must cost no
    /// GPU time, and discarding a parse or an LLM pass has its own stage flag.</para>
    /// </summary>
    private static async Task<int> RunFullPipelineAsync(
        IServiceProvider services,
        IWaybackHarvester harvester,
        SeedCacheStore cache,
        ILogger logger,
        SeedRecipesArguments arguments,
        CancellationToken ct)
    {
        var manifest = SelectRecipes(await harvester.EnsureManifestAsync(ct), arguments, logger);
        var seeder   = services.GetRequiredService<IRecipeLibrarySeeder>();

        var harvest = await harvester.HarvestAsync(manifest, limit: null, ct);
        logger.LogInformation("Stage 2: {Fetched} pages fetched, {Skipped} already cached, {Failed} failed.",
            harvest.PagesFetched, harvest.PagesSkipped, harvest.PagesFailed);

        var parse = await seeder.ParseAsync(manifest, limit: null, force: false, ct);
        logger.LogInformation("Stage 3: {Parsed} parsed, {Skipped} already parsed, {Failed} failed.",
            parse.Parsed, parse.Skipped, parse.Failed);

        var normalise = await seeder.NormaliseAsync(manifest, limit: null, force: false, ct);
        logger.LogInformation("Stage 4: {Normalised} normalised, {Skipped} already current, {Failed} failed.",
            normalise.Normalised, normalise.Skipped, normalise.Failed);

        // Persisting past an aborted Stage 4 would import the part that happened to finish and
        // report the run as done.
        if (normalise.Aborted)
        {
            logger.LogError("Stage 4 stopped early, so Stage 5 did not run. Fix the model and re-run.");
            return FailureExitCode;
        }

        return await PersistSelectedAsync(services, logger, manifest, arguments, ct);
    }

    private static async Task<int> ReportAsync(
        SeedCacheStore cache, ILogger logger, CancellationToken ct)
    {
        var manifest = await cache.TryLoadManifestAsync(ct);
        var state    = await cache.LoadStateAsync(ct);

        logger.LogInformation("Seed cache: {Path}", cache.RootPath);
        logger.LogInformation("Manifest: {Description}",
            manifest is null
                ? "not built — run 'seed-recipes --discover'"
                : $"{manifest.Recipes.Count} recipes discovered {manifest.HarvestedAt:u}");

        if (state.Slugs.Count == 0)
        {
            logger.LogInformation("No slugs have been processed yet.");
            return 0;
        }

        foreach (var stage in Enum.GetValues<SeedStage>())
        {
            var count = state.Slugs.Values.Count(slugState => slugState.Stage == stage);
            if (count > 0) logger.LogInformation("  {Stage,-11} {Count,6}", stage, count);
        }

        var failures = state.Slugs
            .Where(pair => pair.Value.Stage == SeedStage.Failed)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (slug, slugState) in failures)
            logger.LogWarning("  {Slug}: {Error} (attempts: {Attempts})",
                slug, slugState.LastError ?? "unknown", slugState.Attempts);

        return 0;
    }
}
