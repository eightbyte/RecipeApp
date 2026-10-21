using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Services.RecipePacks;

/// <summary>How a pack is imported (Phase 10 §4.4.3).</summary>
/// <param name="CreateMissingIngredients">
/// Create a catalogue entry for each ingredient the catalogue does not know (<c>--create-missing</c>),
/// instead of rejecting the pack.
/// </param>
/// <param name="ReplaceExisting">
/// Replace an existing recipe with the same <c>SourceUrl</c> (<c>--force</c>), instead of skipping
/// it. A recipe a meal plan uses is never replaced.
/// </param>
public record RecipePackImportSettings(bool CreateMissingIngredients = false, bool ReplaceExisting = false);

/// <summary>What one import did. Each list holds recipe labels, except <see cref="CreatedIngredients"/>.</summary>
/// <param name="Errors">Every reason the pack was rejected. When any exist, nothing was written.</param>
/// <param name="Skipped">Recipes whose <c>SourceUrl</c> already existed, left alone.</param>
/// <param name="InUse">Recipes <c>--force</c> would have replaced, left alone because a meal plan uses them.</param>
/// <param name="CreatedIngredients">Catalogue entries <c>--create-missing</c> created.</param>
public record RecipePackImportResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> InUse,
    IReadOnlyList<string> CreatedIngredients)
{
    public bool Succeeded => Errors.Count == 0;

    internal static RecipePackImportResult Failed(IReadOnlyList<string> errors) =>
        new(errors, [], [], [], [], []);
}

/// <summary>
/// Imports a recipe pack (Phase 10 §4.4.2): resolves every ingredient against the catalogue,
/// validates the whole pack, then writes it in one transaction.
///
/// <para><b>Persistence goes through the real API path.</b> Each recipe becomes a
/// <see cref="CreateRecipeRequest"/>, is checked by the same validator the endpoint uses, and is
/// saved by <see cref="RecipeService.CreateAsync"/>. An imported recipe is exactly what a user could
/// have saved by hand.</para>
///
/// <para><b>All or nothing.</b> Nothing is written until the whole pack has resolved and validated,
/// and every error is reported, not only the first. Skipped and in-use recipes are reported
/// without failing the pack.</para>
/// </summary>
public class RecipePackImporter(
    AppDbContext db,
    RecipeService recipeService,
    SeedCatalogueResolver catalogueResolver,
    IValidator<CreateRecipeRequest> validator,
    ImageService imageService,
    ILogger<RecipePackImporter> logger)
{
    /// <summary>
    /// Stored images are named this plus the new recipe's id. A pack image has no stable name of its
    /// own, and <see cref="ImageService.ImportAsync"/> overwrites a file with the same name.
    /// </summary>
    public const string ImageFileNamePrefix = "pack-";

    /// <summary>A recipe that resolved and validated, ready to write.</summary>
    private sealed record PlannedRecipe(string Label, CreateRecipeRequest Request, string? ImagePath);

    /// <summary>
    /// Imports <paramref name="pack"/>.
    ///
    /// <para>Runs in its own transaction, or joins the caller's when one is already open, so the
    /// first-run seeder can commit the import and its ledger row together. When joined, images
    /// copied before a later rollback by the caller are not cleaned up here.</para>
    /// </summary>
    /// <param name="packDirectory">The directory image paths resolve against: the pack file's own.</param>
    public async Task<RecipePackImportResult> ImportAsync(
        RecipePackFile pack,
        string packDirectory,
        RecipePackImportSettings settings,
        CancellationToken ct = default)
    {
        var lookup  = await catalogueResolver.LoadAsync(ct);
        var errors  = new List<string>();
        var missing = new Dictionary<string, Ingredient>(StringComparer.Ordinal);
        var planned = new List<PlannedRecipe>(pack.Recipes.Count);

        ReportRepeatedSourceUrls(pack, errors);

        for (var index = 0; index < pack.Recipes.Count; index++)
        {
            var recipe = pack.Recipes[index];
            if (recipe is null)
            {
                errors.Add($"recipes[{index}] is null.");
                continue;
            }

            var label = $"recipes[{index}] '{recipe.Name}'";
            var plan  = Plan(recipe, label, lookup, missing, packDirectory, settings, errors);

            var validation = await validator.ValidateAsync(plan.Request, ct);
            errors.AddRange(validation.Errors.Select(failure =>
                $"{label}: {failure.PropertyName}: {failure.ErrorMessage}"));

            planned.Add(plan);
        }

        if (errors.Count > 0)
        {
            logger.LogError("The recipe pack was not imported. {Count} error(s):\n  {Errors}",
                errors.Count, string.Join("\n  ", errors));
            return RecipePackImportResult.Failed(errors);
        }

        return await WriteAsync(planned, missing, settings, ct);
    }

    // ── Resolution and validation (no writes) ─────────────────────────────────

    private PlannedRecipe Plan(
        RecipePackRecipe recipe,
        string label,
        SeedCatalogueResolver.CatalogueLookup lookup,
        Dictionary<string, Ingredient> missing,
        string packDirectory,
        RecipePackImportSettings settings,
        List<string> errors)
    {
        var rows        = new List<RecipeIngredientRequest>(recipe.Ingredients.Count);
        var rowsByEntry = new Dictionary<Guid, List<int>>();

        for (var index = 0; index < recipe.Ingredients.Count; index++)
        {
            var row = recipe.Ingredients[index];
            if (row is null)
            {
                errors.Add($"{label}: ingredients[{index}] is null.");
                continue;
            }

            var ingredientId = ResolveRow(row.Ingredient, label, lookup, missing, settings, errors);
            if (!rowsByEntry.TryGetValue(ingredientId, out var indexes))
                rowsByEntry[ingredientId] = indexes = [];
            indexes.Add(rows.Count);

            rows.Add(new RecipeIngredientRequest(
                IngredientId: ingredientId,
                Amount:       row.Amount,
                Unit:         row.Unit,
                Notes:        row.Notes,
                DisplayOrder: rows.Count));
        }

        var steps = new List<RecipeStepRequest>(recipe.Steps.Count);
        for (var index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            if (step is null)
            {
                errors.Add($"{label}: steps[{index}] is null.");
                continue;
            }

            var stepNumber = steps.Count + 1;
            var linked     = new List<int>();
            foreach (var written in step.Ingredients)
            {
                // A step links rows the recipe lists; it never introduces an ingredient.
                var name = Normalise(written);
                Guid? entryId = lookup.IdByName.TryGetValue(name, out var found) ? found
                              : missing.TryGetValue(name, out var created)        ? created.Id
                              : null;

                if (entryId is { } id && rowsByEntry.TryGetValue(id, out var indexes))
                    linked.AddRange(indexes);
                else
                    errors.Add($"{label}: step {stepNumber} names '{written}', which the recipe does not list.");
            }

            steps.Add(new RecipeStepRequest(stepNumber, step.Instruction, [.. linked.Distinct()]));
        }

        string? imagePath = null;
        if (recipe.Image is not null)
        {
            imagePath = Path.GetFullPath(Path.Combine(packDirectory, recipe.Image));
            if (imageService.ValidateImportSource(imagePath) is { } problem)
                errors.Add($"{label}: image '{recipe.Image}': {problem}.");
        }

        var request = new CreateRecipeRequest(
            Name:        recipe.Name,
            Description: recipe.Description,
            SourceUrl:   string.IsNullOrWhiteSpace(recipe.SourceUrl) ? null : recipe.SourceUrl.Trim(),
            Servings:    recipe.Servings,
            Ingredients: rows,
            Steps:       steps);

        return new PlannedRecipe(label, request, imagePath);
    }

    /// <summary>
    /// The catalogue entry a row names, by name then alias. A miss is an error unless
    /// <c>--create-missing</c> is set; either way it gets a placeholder entry, so the recipe still
    /// validates and one run reports every problem. The placeholder is only written when the pack is.
    /// </summary>
    private static Guid ResolveRow(
        string written,
        string label,
        SeedCatalogueResolver.CatalogueLookup lookup,
        Dictionary<string, Ingredient> missing,
        RecipePackImportSettings settings,
        List<string> errors)
    {
        var name = Normalise(written);
        if (name.Length == 0)
        {
            errors.Add($"{label}: an ingredient row names no ingredient.");
            return Guid.NewGuid();
        }

        if (lookup.IdByName.TryGetValue(name, out var id)) return id;

        if (!settings.CreateMissingIngredients)
            errors.Add($"{label}: unknown ingredient '{written}'. Add it to the catalogue, or import with --create-missing.");

        if (!missing.TryGetValue(name, out var placeholder))
        {
            placeholder = new Ingredient
            {
                Id          = Guid.NewGuid(),
                Name        = name,
                DisplayName = written.Trim(),
                Category    = IngredientCategory.Other,
                DefaultUnit = null,
            };
            missing[name] = placeholder;
        }

        return placeholder.Id;
    }

    private static string Normalise(string? written) => written?.Trim().ToLowerInvariant() ?? string.Empty;

    private static void ReportRepeatedSourceUrls(RecipePackFile pack, List<string> errors)
    {
        var repeated = pack.Recipes
            .Where(recipe => !string.IsNullOrWhiteSpace(recipe?.SourceUrl))
            .GroupBy(recipe => recipe.SourceUrl!.Trim(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var group in repeated)
            errors.Add($"sourceUrl '{group.Key}' is used by {group.Count()} recipes in the pack: " +
                       string.Join(", ", group.Select(recipe => $"'{recipe.Name}'")) + ".");
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    private async Task<RecipePackImportResult> WriteAsync(
        IReadOnlyList<PlannedRecipe> planned,
        Dictionary<string, Ingredient> missing,
        RecipePackImportSettings settings,
        CancellationToken ct)
    {
        var imported = new List<string>();
        var replaced = new List<string>();
        var skipped  = new List<string>();
        var inUse    = new List<string>();
        var created  = new List<string>();

        var placeholders   = missing.Values.ToDictionary(ingredient => ingredient.Id);
        var copiedImages   = new List<string>();
        var replacedImages = new List<string>();

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

        try
        {
            var existing = await LoadExistingBySourceUrlAsync(planned, ct);

            foreach (var plan in planned)
            {
                if (plan.Request.SourceUrl is { } sourceUrl && existing.TryGetValue(sourceUrl, out var matches))
                {
                    if (!settings.ReplaceExisting)
                    {
                        skipped.Add(plan.Label);
                        continue;
                    }

                    if (matches.Any(match => match.InMealPlan))
                    {
                        inUse.Add(plan.Label);
                        continue;
                    }

                    // Cascade removes the ingredients, steps and step links.
                    var ids = matches.Select(match => match.Id).ToList();
                    await db.Recipes.Where(recipe => ids.Contains(recipe.Id)).ExecuteDeleteAsync(ct);
                    replacedImages.AddRange(matches.Select(match => match.ImageUrl).OfType<string>());
                    replaced.Add(plan.Label);
                }

                // Only entries a written recipe uses are created, so a skipped recipe adds nothing.
                foreach (var row in plan.Request.Ingredients)
                {
                    if (!placeholders.Remove(row.IngredientId, out var ingredient)) continue;
                    db.Ingredients.Add(ingredient);
                    created.Add(ingredient.Name);
                }
                await db.SaveChangesAsync(ct);

                var recipe = await recipeService.CreateAsync(plan.Request);

                // CreateRecipeRequest has no image field: the UI attaches one by upload afterwards.
                // Same here, inside the same transaction.
                if (plan.ImagePath is not null)
                {
                    var imageUrl = await imageService.ImportAsync(
                        plan.ImagePath, ImageFileNamePrefix + recipe.Id.ToString("N"), ct);
                    copiedImages.Add(imageUrl);

                    await db.Recipes
                        .Where(row => row.Id == recipe.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.ImageUrl, imageUrl), ct);
                }

                if (!replaced.Contains(plan.Label)) imported.Add(plan.Label);
            }

            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch
        {
            // The rows roll back with the transaction; the files have to be removed by hand.
            foreach (var imageUrl in copiedImages) imageService.Delete(imageUrl);
            db.ChangeTracker.Clear();
            throw;
        }

        // A replaced recipe's old image is removed only once the replacement is committed. Under a
        // caller's transaction that commit has not happened yet, so the file is left in place.
        if (ownsTransaction)
            foreach (var imageUrl in replacedImages) imageService.Delete(imageUrl);

        logger.LogInformation(
            "Recipe pack imported: {Imported} new, {Replaced} replaced, {Skipped} skipped as already " +
            "present, {InUse} left in place because a meal plan uses them, {Created} ingredients created.",
            imported.Count, replaced.Count, skipped.Count, inUse.Count, created.Count);

        return new RecipePackImportResult([], imported, replaced, skipped, inUse, created);
    }

    private sealed record ExistingRecipe(Guid Id, string? ImageUrl, bool InMealPlan);

    private async Task<Dictionary<string, List<ExistingRecipe>>> LoadExistingBySourceUrlAsync(
        IReadOnlyList<PlannedRecipe> planned, CancellationToken ct)
    {
        var sourceUrls = planned
            .Select(plan => plan.Request.SourceUrl)
            .OfType<string>()
            .ToList();

        if (sourceUrls.Count == 0) return [];

        var rows = await db.Recipes
            .Where(recipe => recipe.SourceUrl != null && sourceUrls.Contains(recipe.SourceUrl))
            .Select(recipe => new
            {
                recipe.Id,
                SourceUrl = recipe.SourceUrl!,
                recipe.ImageUrl,
                InMealPlan = db.MealPlanRecipes.Any(entry => entry.RecipeId == recipe.Id),
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.SourceUrl, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new ExistingRecipe(row.Id, row.ImageUrl, row.InMealPlan)).ToList(),
                StringComparer.Ordinal);
    }
}
