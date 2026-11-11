using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Stage 5 for one recipe: re-validates the normalised artefact, prepares it and persists it through <c>ConfirmAsync</c>, the same path a user-confirmed scrape takes.</summary>
public class SeedRecipePersister(
    AppDbContext db,
    SeedCatalogueResolver resolver,
    IRecipeScrapeService scrapeService,
    ImageService imageService,
    SeedCacheStore cache,
    IOptions<RecipeSeedingOptions> options,
    ILogger<SeedRecipePersister> logger)
{
    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>A recipe ready to hand to <c>ConfirmAsync</c>, with what preparing it changed.</summary>
    public record PreparedSeedRecipe(
        ScrapeConfirmRequest Request,
        int NonIngredientRowsRemoved,
        IReadOnlyList<string> StaleSkipListLines,
        int RowsReassigned,
        IReadOnlyList<string> StaleRowOverrideLines,
        int AmountsDropped,
        bool BelowQualityThreshold);

    // ── Preparation (no database) ─────────────────────────────────────────────

    /// <summary>Everything Stage 5 decides about a recipe before touching the database, in the order it has to happen.</summary>
    /// <exception cref="SeedPersistException">The recipe must not be persisted.</exception>
    public PreparedSeedRecipe Prepare(
        NormalisedSeedRecipe recipe,
        ParsedSeedRecipe parsed,
        string parsedFingerprint,
        SeedCatalogueResolver.CatalogueLookup lookup)
    {
        // 1. Re-assert the artefact against its source on the rows as Stage 4 wrote them, because nothing else re-validates
        //    normalised/ and a hand edit can look half-right.
        EnsureDerivedFrom(recipe, parsed, parsedFingerprint);

        // 2. Drop group headings and leaked notes, then move rows the normaliser named less specifically than their line.
        var skipped    = SeedNonIngredientRows.Remove(recipe);
        var overridden = SeedIngredientRowOverrides.Apply(skipped.Recipe);
        var working    = overridden.Recipe;

        // 3. Store unexplained multi-number amounts as unquantified rather than persist them wrong.
        var (ingredients, amountsDropped) = DropUnexplainedAmounts(working);
        working = working with { Ingredients = ingredients };

        var belowThreshold = IsBelowQualityThreshold(working);
        if (belowThreshold && _options.ApplyQualityFilter)
            throw new SeedPersistException(SeedPersistFailure.BelowQualityThreshold,
                $"{working.Ingredients.Count} ingredients and {working.Steps.Count} steps; the filter " +
                $"requires {_options.QualityMinimumIngredients} and {_options.QualityMinimumSteps}.");

        // 4. The page notes have no column of their own, so they join the description rather than being lost.
        working = working with { Description = ComposeDescription(working) };
        EnsureWithinRecipeLimits(working);

        // 5. Catalogue resolution is a dictionary lookup with zero LLM calls.
        ScrapeConfirmRequest request;
        try
        {
            request = resolver.ToConfirmRequest(working, lookup);
        }
        catch (SeedCatalogueResolutionException ex)
        {
            throw new SeedPersistException(SeedPersistFailure.CatalogueMiss,
                $"no catalogue entry for {string.Join(", ", ex.UnresolvedNames.Select(name => $"'{name}'"))}");
        }

        return new PreparedSeedRecipe(
            request, skipped.Removed, skipped.Unmatched, overridden.Reassigned, overridden.Unmatched,
            amountsDropped, belowThreshold);
    }

    private static void EnsureDerivedFrom(
        NormalisedSeedRecipe recipe, ParsedSeedRecipe parsed, string parsedFingerprint)
    {
        if (recipe.ParsedFingerprint != parsedFingerprint)
            throw new SeedPersistException(SeedPersistFailure.StaleArtefact,
                "the parsed recipe changed after this was normalised; re-run --normalise for it");

        if (recipe.Ingredients.Count != parsed.Ingredients.Count)
            throw new SeedPersistException(SeedPersistFailure.IngredientCountMismatch,
                $"{recipe.Ingredients.Count} ingredient rows against {parsed.Ingredients.Count} parsed " +
                "lines; the artefact was edited outside the pipeline — re-run --normalise --force --slug");

        foreach (var step in recipe.Steps)
            foreach (var index in step.IngredientIndexes)
                if (index < 0 || index >= recipe.Ingredients.Count)
                    throw new SeedPersistException(SeedPersistFailure.IngredientIndexOutOfRange,
                        $"step {step.StepNumber} points at ingredient {index} of {recipe.Ingredients.Count}");
    }

    /// <summary>Stores a row whose amount its own line cannot account for as unquantified, appending the published line to <c>Notes</c> so the cook still sees the quantity.</summary>
    private (IReadOnlyList<NormalisedSeedIngredient> Ingredients, int Dropped) DropUnexplainedAmounts(
        NormalisedSeedRecipe recipe)
    {
        var dropped = 0;
        var rows = recipe.Ingredients
            .Select(row =>
            {
                if (SeedQuantityGate.IsAmountExplainedByLine(row.SourceText, row.SourceAmount)) return row;

                dropped++;
                logger.LogInformation(
                    "{Slug}: stored '{Line}' without an amount — the model read {Amount} {Unit}, which " +
                    "no number on the line accounts for.",
                    recipe.Slug, row.SourceText, row.SourceAmount, row.SourceUnit);

                var published = $"as published: {row.SourceText}";
                return row with
                {
                    Amount       = null,
                    Unit         = null,
                    SourceAmount = null,
                    SourceUnit   = null,
                    Notes        = string.IsNullOrWhiteSpace(row.Notes) ? published : $"{row.Notes}; {published}",
                };
            })
            .ToList();

        return (rows, dropped);
    }

    private bool IsBelowQualityThreshold(NormalisedSeedRecipe recipe) =>
        recipe.Ingredients.Count < _options.QualityMinimumIngredients
        || recipe.Steps.Count < _options.QualityMinimumSteps;

    /// <summary>Description, page notes, adapted-source credit and the attribution note, as paragraphs.</summary>
    internal string? ComposeDescription(NormalisedSeedRecipe recipe)
    {
        var notes      = StripLinkList(recipe.Notes, _options.NotesLinkListMarker);
        var paragraphs = new[] { recipe.Description, notes, recipe.SourceCredit, _options.SeedAttributionNote }
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .Select(paragraph => paragraph!.Trim())
            .ToList();

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    /// <summary>Removes MyPlate's related-foods link list from the page notes, dropping only the marker's own line because real content can follow it.</summary>
    /// <returns>The remaining notes, or null when the link list was all there was.</returns>
    internal static string? StripLinkList(string? notes, string? marker)
    {
        if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrEmpty(marker)) return notes;

        var start = notes.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return notes;

        var lineEnd = notes.IndexOf('\n', start);
        var before  = notes[..start].TrimEnd();
        var after   = lineEnd < 0 ? string.Empty : notes[lineEnd..].Trim();

        var remaining = string.Join("\n\n", new[] { before, after }.Where(part => part.Length > 0));
        return remaining.Length == 0 ? null : remaining;
    }

    /// <summary><c>ConfirmAsync</c> validates nothing, so this keeps a recipe within the edit form's limits or it could never be saved again from the UI.</summary>
    private static void EnsureWithinRecipeLimits(NormalisedSeedRecipe recipe)
    {
        var problems = new List<string>();

        if (recipe.Name.Length > RecipeLimits.NameMaxLength)
            problems.Add($"name is {recipe.Name.Length} characters (limit {RecipeLimits.NameMaxLength})");

        if (recipe.Description?.Length > RecipeLimits.DescriptionMaxLength)
            problems.Add($"description is {recipe.Description.Length} characters (limit {RecipeLimits.DescriptionMaxLength})");

        if (recipe.Servings is < RecipeLimits.MinServings or > RecipeLimits.MaxServings)
            problems.Add($"serves {recipe.Servings} (allowed {RecipeLimits.MinServings}–{RecipeLimits.MaxServings})");

        foreach (var row in recipe.Ingredients.Where(row => row.Notes?.Length > RecipeLimits.IngredientNotesMaxLength))
            problems.Add($"notes on '{row.Name}' are {row.Notes!.Length} characters (limit {RecipeLimits.IngredientNotesMaxLength})");

        foreach (var step in recipe.Steps.Where(step => step.Instruction.Length > RecipeLimits.StepInstructionMaxLength))
            problems.Add($"step {step.StepNumber} is {step.Instruction.Length} characters (limit {RecipeLimits.StepInstructionMaxLength})");

        if (problems.Count > 0)
            throw new SeedPersistException(SeedPersistFailure.ExceedsRecipeLimits, string.Join("; ", problems));
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>Writes a prepared recipe, and its photo if one was harvested, in one transaction.</summary>
    /// <param name="replaceExisting">Delete a recipe with the same <c>SourceUrl</c> first (<c>--force</c>).</param>
    /// <exception cref="SeedPersistException">The existing recipe is in a meal plan.</exception>
    public async Task<(bool Replaced, bool HasImage)> PersistAsync(
        string slug, PreparedSeedRecipe prepared, bool replaceExisting, CancellationToken ct = default)
    {
        var sourceUrl = prepared.Request.SourceUrl;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var replaced = false;
        if (replaceExisting)
            replaced = await DeleteExistingAsync(sourceUrl, ct);

        var created = await scrapeService.ConfirmAsync(prepared.Request, ct);

        // ConfirmAsync only downloads a remote photo, so the harvested one on disk is attached here inside the same transaction and a recipe is never committed half-imported.
        var imagePath = cache.FindImage(slug);
        if (imagePath is not null)
        {
            var imageUrl = await imageService.ImportAsync(imagePath, _options.SeedImageFilePrefix + slug, ct);
            await db.Recipes
                .Where(recipe => recipe.Id == created.Recipe.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(recipe => recipe.ImageUrl, imageUrl), ct);
        }

        await transaction.CommitAsync(ct);
        return (replaced, imagePath is not null);
    }

    /// <summary>Deletes the recipes carrying <paramref name="sourceUrl"/> with their dependents, refusing when a meal plan references one.</summary>
    private async Task<bool> DeleteExistingAsync(string sourceUrl, CancellationToken ct)
    {
        var existing = await db.Recipes.Where(recipe => recipe.SourceUrl == sourceUrl).ToListAsync(ct);
        if (existing.Count == 0) return false;

        var ids    = existing.Select(recipe => recipe.Id).ToList();
        var inPlan = await db.PlannedMeals.AnyAsync(entry => ids.Contains(entry.RecipeId), ct);
        if (inPlan)
            throw new SeedPersistException(SeedPersistFailure.RecipeInUse,
                "a meal plan uses the existing recipe, so it was left in place rather than replaced");

        db.Recipes.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
