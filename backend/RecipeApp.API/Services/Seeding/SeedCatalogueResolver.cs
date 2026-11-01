using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Turns a normalised seed recipe into a <see cref="ScrapeConfirmRequest"/> by dictionary lookup against the catalogue, with zero LLM calls so the import is deterministic.</summary>
public class SeedCatalogueResolver(
    AppDbContext db,
    MeasurementConverter measurementConverter,
    ILogger<SeedCatalogueResolver> logger)
{
    /// <summary>The catalogue as a lookup loaded once per run: ids by every name and alias, and bulk densities for the entries that have one.</summary>
    public record CatalogueLookup(
        IReadOnlyDictionary<string, Guid> IdByName,
        IReadOnlyDictionary<Guid, decimal?> DensityById)
    {
        public int Count => IdByName.Count;
    }

    /// <summary>Builds the lookup from the catalogue rows currently in the database, loading names before aliases so a name always outranks an alias.</summary>
    public async Task<CatalogueLookup> LoadAsync(CancellationToken ct = default)
    {
        var rows = await db.Ingredients
            .AsNoTracking()
            .Select(i => new { i.Id, i.Name, i.Aliases, i.GramsPerMillilitre })
            .ToListAsync(ct);

        var idByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) idByName[row.Name] = row.Id;
        foreach (var row in rows)
            foreach (var alias in row.Aliases) idByName.TryAdd(alias, row.Id);

        var densityById = rows.ToDictionary(row => row.Id, row => row.GramsPerMillilitre);

        logger.LogInformation(
            "Catalogue lookup ready: {Entries} entries reachable by {Keys} names and aliases, " +
            "{Densities} of them with a bulk density.",
            rows.Count, idByName.Count, densityById.Count(pair => pair.Value.HasValue));

        return new CatalogueLookup(idByName, densityById);
    }

    /// <summary>Maps one normalised recipe onto a confirm request.</summary>
    /// <param name="allowUnknownIngredients">Create a catalogue row for an unknown name instead of failing, which is off by default because a miss means the catalogue is stale.</param>
    /// <exception cref="SeedCatalogueResolutionException">An ingredient name resolved to nothing.</exception>
    public ScrapeConfirmRequest ToConfirmRequest(
        NormalisedSeedRecipe recipe,
        CatalogueLookup lookup,
        bool allowUnknownIngredients = false)
    {
        var ingredients = new List<ScrapeConfirmIngredient>(recipe.Ingredients.Count);
        var unresolved  = new List<string>();

        for (var index = 0; index < recipe.Ingredients.Count; index++)
        {
            var row  = recipe.Ingredients[index];
            var name = row.Name.Trim().ToLowerInvariant();

            if (lookup.IdByName.TryGetValue(name, out var ingredientId))
            {
                lookup.DensityById.TryGetValue(ingredientId, out var density);
                var (amount, unit) = ResolveMeasurement(row, density);

                ingredients.Add(new ScrapeConfirmIngredient(
                    IngredientId:             ingredientId,
                    NewIngredientName:        null,
                    NewIngredientDisplayName: null,
                    Category:                 null,
                    Amount:                   amount,
                    Unit:                     unit,
                    Notes:                    row.Notes,
                    DisplayOrder:             index,
                    SourceAmount:             row.SourceAmount,
                    SourceUnit:               row.SourceUnit));
                continue;
            }

            if (!allowUnknownIngredients)
            {
                unresolved.Add(name);
                continue;
            }

            // An unmatched name has no density, so its measurement is kept exactly as Stage 4 stated it.
            ingredients.Add(new ScrapeConfirmIngredient(
                IngredientId:             null,
                NewIngredientName:        name,
                NewIngredientDisplayName: row.DisplayName,
                Category:                 RecipeScrapeService.CategoriseIngredient(name),
                Amount:                   row.Amount,
                Unit:                     row.Unit,
                Notes:                    row.Notes,
                DisplayOrder:             index,
                SourceAmount:             row.SourceAmount,
                SourceUnit:               row.SourceUnit));
        }

        if (unresolved.Count > 0)
            throw new SeedCatalogueResolutionException(recipe.Slug, unresolved);

        var steps = recipe.Steps
            .OrderBy(step => step.StepNumber)
            .Select(step => new ScrapeConfirmStep(
                step.StepNumber, step.Instruction, [.. step.IngredientIndexes]))
            .ToList();

        return new ScrapeConfirmRequest(
            Name:        recipe.Name,
            Description: recipe.Description,
            SourceUrl:   recipe.SourceUrl,
            Servings:    recipe.Servings,
            Ingredients: ingredients,
            Steps:       steps);
    }

    /// <summary>Stage B only: resolves <c>cup</c> against the matched entry's density, handing an unquantified row through untouched.</summary>
    private (decimal? Amount, string? Unit) ResolveMeasurement(
        NormalisedSeedIngredient row, decimal? density)
    {
        if (row.Amount is not { } amount || row.Unit is not { } unit) return (null, null);

        var (resolvedAmount, resolvedUnit) = measurementConverter.ResolveForImport(amount, unit, density);
        return (resolvedAmount, resolvedUnit);
    }
}

/// <summary>A seed recipe naming an ingredient the catalogue does not cover, which means the shipped catalogue is stale relative to <c>normalised/</c>.</summary>
public class SeedCatalogueResolutionException(string slug, IReadOnlyList<string> names)
    : Exception($"{slug}: the catalogue has no entry for {string.Join(", ", names.Select(n => $"'{n}'"))}. " +
                "Add each name to seed-data/ingredient-catalogue.json as an entry or an alias, or " +
                "reassign the row in SeedIngredientRowOverrides.")
{
    public string Slug { get; } = slug;

    /// <summary>Every name that resolved to nothing, so one run names them all rather than one at a time.</summary>
    public IReadOnlyList<string> UnresolvedNames { get; } = names;

    /// <summary>The reason string written to <c>state.json</c> and printed in the run summary.</summary>
    public string StateReason => $"CatalogueMiss: {string.Join(", ", UnresolvedNames)}";
}
