using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Data;

/// <summary>
/// Startup seeding for every environment except <see cref="TestingEnvironmentName"/>: migrates, then seeds a fresh database once
/// with the shipped ingredient catalogue and the sample recipe pack, never stopping the app on failure.
/// </summary>
public class FirstRunSeeder(
    AppDbContext db,
    IngredientCatalogueFileStore catalogueStore,
    RecipePackFileStore packStore,
    RecipePackImporter packImporter,
    ILogger<FirstRunSeeder> logger)
{
    /// <summary>The environment integration tests run under, in which startup seeding stays out so each test controls its own data.</summary>
    public const string TestingEnvironmentName = "Testing";

    public static bool RunsIn(IHostEnvironment environment) =>
        !environment.IsEnvironment(TestingEnvironmentName);

    public async Task RunAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        await RunStepAsync(
            SeedLedgerKeys.IngredientCatalogue,
            targetHasData: () => db.Ingredients.AnyAsync(ct),
            loadArtefact:  LoadCatalogueAsync,
            versionOf:     catalogue => catalogue.ContentVersion,
            apply:         ApplyCatalogueAsync,
            ct);

        await IngredientDensitySeeder.SeedAsync(db, logger, ct);

        await RunStepAsync(
            SeedLedgerKeys.SampleRecipes,
            targetHasData: () => db.Recipes.AnyAsync(ct),
            loadArtefact:  LoadSamplePackAsync,
            versionOf:     pack => pack.ContentVersion,
            apply:         ApplySamplePackAsync,
            ct);
    }

    // ── The ledger-guarded step ───────────────────────────────────────────────

    /// <param name="loadArtefact">Returns null after logging when the artefact cannot be read.</param>
    /// <param name="apply">Returns false after logging when the artefact could not be applied.</param>
    private async Task RunStepAsync<TArtefact>(
        string key,
        Func<Task<bool>> targetHasData,
        Func<CancellationToken, Task<TArtefact?>> loadArtefact,
        Func<TArtefact, int> versionOf,
        Func<TArtefact, CancellationToken, Task<bool>> apply,
        CancellationToken ct)
        where TArtefact : class
    {
        if (await db.SeedLedger.AnyAsync(entry => entry.Key == key, ct))
        {
            logger.LogDebug("First-run step '{Step}' already recorded; skipping.", key);
            return;
        }

        var artefact = await loadArtefact(ct);
        if (artefact is null) return;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            string outcome;
            if (await targetHasData())
            {
                outcome = SeedLedgerOutcome.SkippedExistingData;
            }
            else
            {
                if (!await apply(artefact, ct)) return;   // disposing the transaction rolls it back
                outcome = SeedLedgerOutcome.Applied;
            }

            db.SeedLedger.Add(new SeedLedgerEntry
            {
                Key             = key,
                Outcome         = outcome,
                ArtefactVersion = versionOf(artefact),
                AppliedAt       = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation("First-run step '{Step}': {Outcome} (content version {Version}).",
                key, outcome, versionOf(artefact));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A second host recording the same step first is also caught here, because the ledger's primary key rejects the insert.
            logger.LogError(ex,
                "First-run step '{Step}' failed and was rolled back. It will be retried on the next start.", key);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // ── Ingredient catalogue ──────────────────────────────────────────────────

    private async Task<IngredientCatalogueFile?> LoadCatalogueAsync(CancellationToken ct)
    {
        try
        {
            return await catalogueStore.LoadAsync(ct);
        }
        catch (FileNotFoundException)
        {
            logger.LogError(
                "No ingredient catalogue at {Path}. It is committed source and ships with the app; " +
                "restore it from git. The catalogue step will be retried on the next start.",
                catalogueStore.Path);
        }
        catch (CatalogueValidationException ex)
        {
            logger.LogError("The ingredient catalogue cannot be read: {Message}", ex.Message);
        }

        return null;
    }

    private async Task<bool> ApplyCatalogueAsync(IngredientCatalogueFile catalogue, CancellationToken ct)
    {
        try
        {
            await IngredientCatalogueFileSeeder.SeedAsync(db, catalogue, logger, force: false, ct);
            return true;
        }
        catch (CatalogueValidationException ex)
        {
            logger.LogError("The ingredient catalogue was not loaded: {Message}", ex.Message);
            return false;
        }
    }

    // ── Sample recipes ────────────────────────────────────────────────────────

    private async Task<RecipePackFile?> LoadSamplePackAsync(CancellationToken ct)
    {
        try
        {
            return await RecipePackFileStore.LoadAsync(packStore.SamplePackPath, ct);
        }
        catch (FileNotFoundException)
        {
            logger.LogError(
                "No sample recipe pack at {Path}. It is committed source and ships with the app; " +
                "restore it from git. The sample-recipe step will be retried on the next start.",
                packStore.SamplePackPath);
        }
        catch (RecipePackValidationException ex)
        {
            logger.LogError("The sample recipe pack cannot be read: {Message}", ex.Message);
        }

        return null;
    }

    /// <summary>Imports the built-in pack inside the step's transaction, never with <c>--create-missing</c> or <c>--force</c>, because every sample ingredient must resolve against the shipped catalogue.</summary>
    private async Task<bool> ApplySamplePackAsync(RecipePackFile pack, CancellationToken ct)
    {
        var result = await packImporter.ImportAsync(
            pack, Path.GetDirectoryName(packStore.SamplePackPath)!, new RecipePackImportSettings(), ct);

        // The importer has already logged every error.
        return result.Succeeded;
    }
}
