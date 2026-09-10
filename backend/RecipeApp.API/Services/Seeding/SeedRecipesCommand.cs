namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The <c>seed-recipes</c> CLI command (Phase 9 §15). Parses its own argument vector — everything
/// after the <c>seed-recipes</c> token — and drives the harvest stages, then exits without
/// starting Kestrel.
///
/// <para>Stages 1-4 (discover, fetch, images, parse, normalise) are implemented. The pipeline
/// modes that need persistence report as unimplemented rather than silently doing part of the
/// job.</para>
/// </summary>
public static class SeedRecipesCommand
{
    public const string CommandName = "seed-recipes";

    /// <summary>Exit code for a usage error or an aborted run.</summary>
    private const int FailureExitCode = 1;

    /// <summary>Exit code for a mode that is recognised but not yet built.</summary>
    private const int NotImplementedExitCode = 2;

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

        /// <summary>Print the cached progress summary and do no work.</summary>
        public bool Report { get; init; }

        /// <summary>Discard cached pages and images before harvesting.</summary>
        public bool RefreshCache { get; init; }

        /// <summary>Run the stratified trial subset through the full pipeline.</summary>
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

        /// <summary>Whether this invocation needs Stage 5, which is not yet implemented.</summary>
        public bool RequiresFullPipeline =>
            !Discover && !Harvest && !Parse && !Normalise && !Report;
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

                default:
                    logger.LogError("Unrecognised argument '{Argument}'.\n{Usage}", commandArgs[index], Usage);
                    return null;
            }
        }

        return arguments;
    }

    private const string Usage = """
        Usage: dotnet run -- seed-recipes [options]

          --discover        Stage 1 only — build the manifest and report the slug count
          --harvest         Stages 1-2 — download and cache pages and images, no LLM
          --parse           Stage 3 — parse cached pages into recipes, offline
          --normalise       Stage 4 — LLM quantity extraction and step linkage
          --report          Print the cached progress summary and do no work
          --refresh-cache   Discard cached pages and images, then re-harvest
          --limit <n>       Process at most n recipes this run
          --slug <name>     Restrict the run to this recipe; repeatable
          --trial           Full pipeline over the stratified trial subset
          --force           Redo work already done (re-parse, re-normalise, re-import)
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

            logger.LogError(
                "Stage 5 (persist) is not implemented yet, so this mode cannot run. " +
                "Use --discover, --harvest, --parse, --normalise or --report.\n{Usage}", Usage);
            return NotImplementedExitCode;
        }
        catch (SeedHarvestException ex)
        {
            logger.LogError("Seed harvest aborted: {Message}", ex.Message);
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
