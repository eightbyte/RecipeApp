using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Turns a normalised seed recipe into a <see cref="ScrapeConfirmRequest"/> by looking every
/// ingredient name up in the catalogue — Stage 5's replacement for
/// <c>RecipeScrapeService.NormaliseAsync</c> (Phase 9.3 §4.8).
///
/// <para><b>Zero LLM calls.</b> That is the point of the whole phase. Routing 1,123 recipes through
/// the scraper's matching pass would have sent the entire catalogue as candidates on every recipe —
/// 1,475 entries is ~11,800 tokens per call — while <i>also</i> creating new entries mid-run, so
/// recipe #1 was matched against ~200 candidates and recipe #900 against ~1,300. The same
/// ingredient name could resolve differently depending on where its recipe sat in the manifest, and
/// a second machine could produce a different catalogue from the same input. A committed artefact
/// and a dictionary make the import deterministic.</para>
///
/// <para><b>Persistence is still <c>ConfirmAsync</c>'s.</b> Only the construction of the request
/// changes, so Phase 9 §12's "no parallel persistence logic" rule holds and every invariant
/// <c>ConfirmAsync</c> enforces — not least <c>RecipeIngredient.ToStoredMeasurement</c>'s
/// half-set-measurement guard — still applies.</para>
/// </summary>
public class SeedCatalogueResolver(
    AppDbContext db,
    MeasurementConverter measurementConverter,
    ILogger<SeedCatalogueResolver> logger)
{
    /// <summary>
    /// The catalogue as a lookup, loaded once per run rather than once per recipe.
    /// </summary>
    /// <param name="IdByName">Keyed by every entry's name and every alias, case-insensitively.</param>
    /// <param name="DensityById">Bulk density for the entries that have one; the rest keep their measurement as stated.</param>
    public record CatalogueLookup(
        IReadOnlyDictionary<string, Guid> IdByName,
        IReadOnlyDictionary<Guid, decimal?> DensityById)
    {
        public int Count => IdByName.Count;
    }

    /// <summary>
    /// Builds the lookup from the catalogue rows currently in the database.
    ///
    /// <para>Names are loaded before aliases and aliases only with <c>TryAdd</c>, so an entry's own
    /// name always outranks another entry's alias. The artefact's validation forbids that collision
    /// outright; this is what makes the outcome defined anyway if a hand-edited row ever introduces
    /// one.</para>
    /// </summary>
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

    /// <summary>
    /// Maps one normalised recipe onto a confirm request.
    /// </summary>
    /// <param name="allowUnknownIngredients">
    /// Fall back to creating a catalogue row for a name the artefact does not cover, instead of
    /// failing the recipe. Off by default: a miss means the committed catalogue is stale relative to
    /// <c>normalised/</c>, which is a real error and should be loud rather than quietly growing the
    /// catalogue back into the order-dependent thing this phase removed.
    /// </param>
    /// <exception cref="SeedCatalogueResolutionException">
    /// An ingredient name resolved to nothing. Caught per slug by the stage runner, which records it
    /// and moves on — the same per-recipe isolation Stages 3 and 4 use.
    /// </exception>
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

            // An unmatched name has no catalogue row and therefore no density by construction, so
            // its measurement is kept exactly as Stage 4 stated it — there is nothing to resolve a
            // cup against.
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

    /// <summary>
    /// Stage B only. Stage A already ran — <see cref="NormalisedSeedIngredient.Amount"/> is the
    /// post-<c>ToCanonical</c> figure and <c>SourceAmount</c>/<c>SourceUnit</c> hold what preceded
    /// it — so all that remains is resolving <c>cup</c> against the matched entry's density.
    ///
    /// <para>An unquantified row has nothing to resolve and is handed through untouched: a null
    /// amount never reaches the converter, and a unit without an amount would be as invented as the
    /// amount (Phase 9.1).</para>
    /// </summary>
    private (decimal? Amount, string? Unit) ResolveMeasurement(
        NormalisedSeedIngredient row, decimal? density)
    {
        if (row.Amount is not { } amount || row.Unit is not { } unit) return (null, null);

        var (resolvedAmount, resolvedUnit) = measurementConverter.ResolveForImport(amount, unit, density);
        return (resolvedAmount, resolvedUnit);
    }
}

/// <summary>
/// A seed recipe naming an ingredient the catalogue does not cover.
///
/// <para>This means the committed artefact is stale relative to <c>normalised/</c> — the corpus was
/// re-normalised without rebuilding the catalogue, or the catalogue was hand-edited and a name was
/// lost. §4.7 rule 5 exists to catch it at authoring time; reaching here means it was not run.</para>
/// </summary>
public class SeedCatalogueResolutionException(string slug, IReadOnlyList<string> names)
    : Exception($"{slug}: the catalogue has no entry for {string.Join(", ", names.Select(n => $"'{n}'"))}. " +
                $"Rebuild it with '{SeedRecipesCommand.CommandName} --build-catalogue', or pass " +
                "--allow-unknown-ingredients to create rows on the fly.")
{
    public string Slug { get; } = slug;

    /// <summary>Every name that resolved to nothing, so one run names them all rather than one at a time.</summary>
    public IReadOnlyList<string> UnresolvedNames { get; } = names;

    /// <summary>The reason string written to <c>state.json</c> and printed in the run summary.</summary>
    public string StateReason => $"CatalogueMiss: {string.Join(", ", UnresolvedNames)}";
}
