namespace RecipeApp.API.Services.RecipePacks;

/// <summary>
/// A recipe pack (Phase 10 §4.4): a JSON file of recipes in one fixed format that any source can
/// produce. <c>seed-data/sample-recipes.json</c> is the first; <c>import-recipes</c> imports any
/// other.
///
/// <para><b>Two versions, two jobs</b>, the same split as the ingredient catalogue.
/// <see cref="FormatVersion"/> is the file's shape, and the loader refuses one it cannot read.
/// <see cref="ContentVersion"/> is raised on every edit to the pack and never refused. The
/// first-run seeder records it in the ledger.</para>
/// </summary>
public record RecipePackFile
{
    /// <summary>The only file shape this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }

    public required int ContentVersion { get; init; }

    public required IReadOnlyList<RecipePackRecipe> Recipes { get; init; }
}

/// <summary>
/// One recipe in a pack. Parameters with a default may be left out of the file; the rest are
/// required, so a forgotten field is an error rather than a silent default.
/// </summary>
/// <param name="SourceUrl">
/// Optional. The duplicate key on re-import: an existing recipe with the same value is skipped, or
/// replaced with <c>--force</c>. A recipe without one is always inserted.
/// </param>
/// <param name="Image">Optional. A path relative to the pack file.</param>
/// <param name="Ingredients">Rows in display order. Each names a catalogue entry by name or alias.</param>
/// <param name="Steps">Steps in order. The step number is the position.</param>
public record RecipePackRecipe(
    string Name,
    int Servings,
    IReadOnlyList<RecipePackIngredient> Ingredients,
    IReadOnlyList<RecipePackStep> Steps,
    string? Description = null,
    string? SourceUrl = null,
    string? Image = null);

/// <summary>
/// One ingredient row. <paramref name="Amount"/> and <paramref name="Unit"/> are required even
/// when null, so an unquantified row says so rather than having forgotten its amount.
/// </summary>
/// <param name="Ingredient">A catalogue entry's name or alias.</param>
/// <param name="Amount">Null when no quantity is stated. Never zero.</param>
/// <param name="Unit">A canonical <see cref="Enums.MeasurementUnit"/>, null exactly when <paramref name="Amount"/> is.</param>
public record RecipePackIngredient(
    string Ingredient,
    decimal? Amount,
    string? Unit,
    string? Notes = null);

/// <summary>One step.</summary>
/// <param name="Ingredients">
/// Ingredients the step uses, by name or alias rather than by index. Each links every row in the
/// recipe that resolves to the same catalogue entry.
/// </param>
public record RecipePackStep(
    string Instruction,
    IReadOnlyList<string> Ingredients);
