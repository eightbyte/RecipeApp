using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Data;

/// <summary>
/// Startup seeding for every environment except <see cref="TestingEnvironmentName"/>
/// (Phase 10 §4.2). Migrates, then seeds a fresh database once with the shipped ingredient
/// catalogue and the sample recipe pack.
///
/// <para><b>Each one-time step runs once per database (D2).</b> The <see cref="SeedLedgerEntry"/>
/// row, not the seeded data, records that it ran, so deleting every sample recipe does not bring
/// them back. A step and its ledger row commit in one transaction: a crash halfway leaves no row,
/// and the step re-runs cleanly on the next start.</para>
///
/// <para><b>An upgraded database is left alone (§4.3).</b> A step whose ledger row is missing but
/// whose target already holds data records <see cref="SeedLedgerOutcome.SkippedExistingData"/> and
/// inserts nothing, so a database holding the MyPlate library does not gain 12 more recipes.</para>
///
/// <para><b>A failed step never stops the app.</b> It logs every error, writes no ledger row, and
/// is retried on the next start, after the artefact is fixed.</para>
///
/// <para>Order is load-bearing: densities attach to catalogue rows, and the sample recipes resolve
/// their ingredients through the catalogue. Densities fill nulls only, so they run on every start
/// with no ledger row.</para>
/// </summary>
public class FirstRunSeeder(
    AppDbContext db,
    IngredientCatalogueFileStore catalogueStore,
    RecipePackFileStore packStore,
    RecipePackImporter packImporter,
    ILogger<FirstRunSeeder> logger)
{
    /// <summary>
    /// The environment integration tests run under. Startup seeding stays out of it so each test
    /// controls its own data.
    /// </summary>
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
            // Includes a second host recording the same step first: the ledger's primary key
            // rejects this insert and the step's writes roll back with it.
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

    /// <summary>
    /// Imports the built-in pack inside the step's transaction. Never with <c>--create-missing</c>
    /// or <c>--force</c> (§4.4.3): every sample ingredient must resolve against the shipped
    /// catalogue, and the step only runs on an empty <c>Recipes</c> table.
    /// </summary>
    private async Task<bool> ApplySamplePackAsync(RecipePackFile pack, CancellationToken ct)
    {
        var result = await packImporter.ImportAsync(
            pack, Path.GetDirectoryName(packStore.SamplePackPath)!, new RecipePackImportSettings(), ct);

        // The importer has already logged every error.
        return result.Succeeded;
    }
}
