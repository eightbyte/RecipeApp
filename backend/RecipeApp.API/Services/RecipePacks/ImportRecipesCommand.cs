using RecipeApp.API.Data;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Services.RecipePacks;

/// <summary>
/// The <c>import-recipes</c> CLI command (Phase 10 §4.4.3). Imports one recipe pack, then exits
/// without starting Kestrel.
///
/// <para>Migrates and runs a non-force catalogue import first, as <c>seed-recipes --persist</c>
/// does, so it works on an empty database. Does not use the first-run ledger.</para>
/// </summary>
public static class ImportRecipesCommand
{
    public const string CommandName = "import-recipes";

    public const string CreateMissingFlag = "--create-missing";
    public const string ForceFlag         = "--force";

    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;

    /// <summary>
    /// Index of the command token in the process argument vector, or -1 when the command was not
    /// requested. Everything after it belongs to this command, not to host configuration.
    /// </summary>
    public static int IndexIn(string[] args) => Array.IndexOf(args, CommandName);

    /// <param name="PackPath">The pack file, as given on the command line.</param>
    public record ImportRecipesArguments(string PackPath, RecipePackImportSettings Settings);

    /// <summary>Parses the command's own arguments, or returns null after reporting a usage error.</summary>
    public static ImportRecipesArguments? TryParse(string[] commandArgs, ILogger logger)
    {
        string? packPath      = null;
        var     createMissing = false;
        var     force         = false;

        foreach (var argument in commandArgs)
        {
            switch (argument)
            {
                case CreateMissingFlag: createMissing = true; break;
                case ForceFlag:         force = true; break;

                case var flag when flag.StartsWith("--", StringComparison.Ordinal):
                    logger.LogError("Unrecognised argument '{Argument}'.\n{Usage}", flag, Usage);
                    return null;

                default:
                    if (packPath is not null)
                    {
                        logger.LogError("import-recipes takes one pack file; got '{First}' and '{Second}'.\n{Usage}",
                            packPath, argument, Usage);
                        return null;
                    }
                    packPath = argument;
                    break;
            }
        }

        if (packPath is null)
        {
            logger.LogError("import-recipes needs the path of a recipe pack.\n{Usage}", Usage);
            return null;
        }

        return new ImportRecipesArguments(packPath, new RecipePackImportSettings(createMissing, force));
    }

    private const string Usage = """
        Usage: dotnet run -- import-recipes <pack.json> [--create-missing] [--force]

          <pack.json>         A recipe pack (Phase 10 §4.4). Image paths resolve against its folder.
          --create-missing    Create an OTHER-category ingredient for each name the catalogue lacks,
                              instead of rejecting the pack
          --force             Replace recipes whose sourceUrl already exists, instead of skipping
                              them. A recipe a meal plan uses is never replaced.

        The pack is imported all or nothing: every error is listed and nothing is written.
        """;

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <returns>0 on success, including when recipes were skipped or in use; 1 otherwise.</returns>
    public static async Task<int> RunAsync(
        IServiceProvider services, ILogger logger, string[] commandArgs, CancellationToken ct = default)
    {
        var arguments = TryParse(commandArgs, logger);
        if (arguments is null) return FailureExitCode;

        var packPath = Path.GetFullPath(arguments.PackPath);

        try
        {
            var pack = await RecipePackFileStore.LoadAsync(packPath, ct);

            // Never forced: this command's --force is about recipes, and must not discard
            // corrections made to the catalogue.
            await CatalogueImport.RunAsync(services, logger, force: false, ct);

            using var scope    = services.CreateScope();
            var       importer = scope.ServiceProvider.GetRequiredService<RecipePackImporter>();
            var       result   = await importer.ImportAsync(
                pack, Path.GetDirectoryName(packPath)!, arguments.Settings, ct);

            if (!result.Succeeded) return FailureExitCode;

            Report(logger, result);
            return SuccessExitCode;
        }
        catch (Exception ex) when (ex is FileNotFoundException or RecipePackValidationException or CatalogueValidationException)
        {
            logger.LogError("{Message}", ex.Message);
            return FailureExitCode;
        }
        catch (Npgsql.NpgsqlException ex)
        {
            logger.LogError("Recipe import aborted: {Message}", ex.Message);
            logger.LogError("Is the database running? 'docker compose up -d postgres'.");
            return FailureExitCode;
        }
    }

    private static void Report(ILogger logger, RecipePackImportResult result)
    {
        foreach (var label in result.Skipped)
            logger.LogInformation("  skipped {Recipe}: its sourceUrl is already imported (use --force to replace it).", label);

        foreach (var label in result.InUse)
            logger.LogWarning("  kept {Recipe}: a meal plan uses the existing recipe, so it was not replaced.", label);

        foreach (var name in result.CreatedIngredients)
            logger.LogInformation("  created ingredient '{Name}' in OTHER. Tidy it on the ingredient screen.", name);
    }
}
