using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Data;

/// <summary>
/// Migrates the database, then loads the committed ingredient catalogue and the curated densities —
/// in that order, because a density can only attach to a row that exists (Phase 9.3 §4.5).
///
/// <para>Shared by <c>import-catalogue</c> and by <c>seed-recipes</c>'s persist stage, so the
/// catalogue → densities → recipes ordering is enforced by the one path both take rather than
/// documented and hoped for. Idempotent without <paramref name="force"/>: an already-imported
/// catalogue only has its nulls filled and new aliases merged in.</para>
/// </summary>
public static class CatalogueImport
{
    /// <exception cref="FileNotFoundException">The catalogue artefact is missing.</exception>
    /// <exception cref="CatalogueValidationException">The artefact contradicts itself.</exception>
    public static async Task RunAsync(
        IServiceProvider services, ILogger logger, bool force = false, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        var store     = scope.ServiceProvider.GetRequiredService<IngredientCatalogueFileStore>();
        var catalogue = await store.LoadAsync(ct);

        await IngredientCatalogueFileSeeder.SeedAsync(db, catalogue, logger, force, ct);
        await IngredientDensitySeeder.SeedAsync(db, logger, ct);
    }
}
