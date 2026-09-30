using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stage 5 for one recipe (Phase 9 §12): re-validates the normalised artefact, prepares it, and
/// persists it through <c>ConfirmAsync</c> — the same code path a user-confirmed scrape takes, so
/// there is no parallel persistence logic.
///
/// <para>Scoped, like the <c>AppDbContext</c> it shares with <c>RecipeScrapeService</c>: the runner
/// opens a fresh scope per recipe, so one failed save cannot leave a poisoned change tracker behind
/// for the next.</para>
/// </summary>
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
    /// <param name="Request">The confirm request, ingredients resolved against the catalogue.</param>
    /// <param name="NonIngredientRowsRemoved">Heading and note rows dropped.</param>
    /// <param name="StaleSkipListLines">Skip-list lines the recipe did not contain.</param>
    /// <param name="AmountsDropped">Rows whose unexplained amount was dropped.</param>
    /// <param name="BelowQualityThreshold">Whether the §18 Q3 filter would exclude it.</param>
    public record PreparedSeedRecipe(
        ScrapeConfirmRequest Request,
        int NonIngredientRowsRemoved,
        IReadOnlyList<string> StaleSkipListLines,
        int AmountsDropped,
        bool BelowQualityThreshold);

    // ── Preparation (no database) ─────────────────────────────────────────────

    /// <summary>
    /// Everything Stage 5 decides about a recipe before touching the database, in the order it has
    /// to happen.
    /// </summary>
    /// <param name="recipe">The normalised artefact.</param>
    /// <param name="parsed">The parsed recipe it claims to be derived from.</param>
    /// <param name="parsedFingerprint">The fingerprint of that parsed file as it is on disk now.</param>
    /// <param name="lookup">The catalogue, loaded once per run.</param>
    /// <exception cref="SeedPersistException">The recipe must not be persisted.</exception>
    public PreparedSeedRecipe Prepare(
        NormalisedSeedRecipe recipe,
        ParsedSeedRecipe parsed,
        string parsedFingerprint,
        SeedCatalogueResolver.CatalogueLookup lookup)
    {
        // 1. Re-assert the artefact against its source (§24.4.2). Nothing re-validates normalised/
        //    between Stages 4 and 5, and a hand-edited file broke all three of these invariants
        //    while looking half-right. Checked on the rows as Stage 4 wrote them — before step 2
        //    removes any, because the pairing holds only for the full set.
        EnsureDerivedFrom(recipe, parsed, parsedFingerprint);

        // 2. Group headings and leaked notes are not ingredients.
        var skipped = SeedNonIngredientRows.Remove(recipe);
        var working = skipped.Recipe;

        // 3. Unexplained multi-number amounts (§24.3a) are stored unquantified, not persisted wrong.
        var (ingredients, amountsDropped) = DropUnexplainedAmounts(working);
        working = working with { Ingredients = ingredients };

        var belowThreshold = IsBelowQualityThreshold(working);
        if (belowThreshold && _options.ApplyQualityFilter)
            throw new SeedPersistException(SeedPersistFailure.BelowQualityThreshold,
                $"{working.Ingredients.Count} ingredients and {working.Steps.Count} steps; the filter " +
                $"requires {_options.QualityMinimumIngredients} and {_options.QualityMinimumSteps}.");

        // 4. The page notes have no column of their own and §19 rules out a migration, so they join
        //    the description rather than being lost.
        working = working with { Description = ComposeDescription(working) };
        EnsureWithinRecipeLimits(working);

        // 5. Catalogue resolution — a dictionary lookup, zero LLM calls (§12.1).
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
            request, skipped.Removed, skipped.Unmatched, amountsDropped, belowThreshold);
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

    /// <summary>
    /// Stores a row whose amount its own line cannot account for as unquantified.
    ///
    /// <para>The alternative choices were worse: persisting <c>13.333 cups</c> of vinaigrette puts it in
    /// every shopping list the recipe reaches, and excluding the recipe loses the other rows over
    /// one. <c>SourceAmount</c>/<c>SourceUnit</c> go with the amount, because they would record the
    /// misread rather than the source. The published line is appended to <c>Notes</c> so the
    /// quantity the cook needs is still on screen — <c>RecipeIngredient</c> has no column for the
    /// line itself.</para>
    /// </summary>
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

    /// <summary>
    /// Description, page notes, adapted-source credit and the attribution note, as paragraphs.
    /// Stage 3 kept them separate precisely so this could be decided here (§22.1).
    /// </summary>
    internal string? ComposeDescription(NormalisedSeedRecipe recipe)
    {
        var notes      = StripLinkList(recipe.Notes, _options.NotesLinkListMarker);
        var paragraphs = new[] { recipe.Description, notes, recipe.SourceCredit, _options.SeedAttributionNote }
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .Select(paragraph => paragraph!.Trim())
            .ToList();

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// Removes MyPlate's related-foods link list (<c>Learn more about: Garlic Turnips Onions</c>) from
    /// the page notes: navigation, not content. 969 of the 1,048 recipes with notes carry it, and for
    /// 529 it is all there is.
    ///
    /// <para>Only the marker's own line goes. It usually ends the notes, but on 23 recipes real content
    /// follows on a later line — a storage tip, a substitution, a footnote — and that is kept. Text
    /// before the marker on the same line is kept too.</para>
    /// </summary>
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

    /// <summary>
    /// <c>ConfirmAsync</c> validates nothing, so a recipe outside the edit form's limits would be
    /// importable but never saveable again from the UI.
    /// </summary>
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

    /// <summary>
    /// Writes a prepared recipe, and its photo if one was harvested, in one transaction.
    /// </summary>
    /// <param name="slug">Cache key, used to find the photo.</param>
    /// <param name="prepared">The output of <see cref="Prepare"/>.</param>
    /// <param name="replaceExisting">Delete a recipe with the same <c>SourceUrl</c> first (<c>--force</c>).</param>
    /// <returns>Whether an existing recipe was replaced, and whether a photo was attached.</returns>
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

        // ConfirmAsync has no image field — the scrape flow attaches photos by upload, afterwards.
        // Same here, inside the same transaction, so a recipe is never committed half-imported.
        var imagePath = cache.FindImage(slug);
        if (imagePath is not null)
        {
            var imageUrl = await imageService.ImportAsync(imagePath, _options.SeedImageFilePrefix + slug, ct);
            await db.Recipes
                .Where(recipe => recipe.Id == created.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(recipe => recipe.ImageUrl, imageUrl), ct);
        }

        await transaction.CommitAsync(ct);
        return (replaced, imagePath is not null);
    }

    /// <summary>
    /// Deletes the recipes carrying <paramref name="sourceUrl"/>; cascade removes their ingredients,
    /// steps and step links. Refuses when a meal plan references one.
    /// </summary>
    private async Task<bool> DeleteExistingAsync(string sourceUrl, CancellationToken ct)
    {
        var existing = await db.Recipes.Where(recipe => recipe.SourceUrl == sourceUrl).ToListAsync(ct);
        if (existing.Count == 0) return false;

        var ids    = existing.Select(recipe => recipe.Id).ToList();
        var inPlan = await db.MealPlanRecipes.AnyAsync(entry => ids.Contains(entry.RecipeId), ct);
        if (inPlan)
            throw new SeedPersistException(SeedPersistFailure.RecipeInUse,
                "a meal plan uses the existing recipe, so it was left in place rather than replaced");

        db.Recipes.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
