namespace RecipeApp.API.Services.RecipePacks;

/// <summary>
/// A recipe pack: a JSON file of recipes in one fixed format that any source can produce, where <see cref="FormatVersion"/> is the
/// file's shape and <see cref="ContentVersion"/> is raised on every edit and never refused.
/// </summary>
public record RecipePackFile
{
    /// <summary>The only file shape this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }

    public required int ContentVersion { get; init; }

    public required IReadOnlyList<RecipePackRecipe> Recipes { get; init; }
}

/// <summary>One recipe in a pack, where parameters with a default may be left out of the file and the rest are required so a forgotten field is an error.</summary>
/// <param name="SourceUrl">Optional duplicate key on re-import: an existing recipe with the same value is skipped, or replaced with <c>--force</c>.</param>
/// <param name="Image">Optional path relative to the pack file.</param>
public record RecipePackRecipe(
    string Name,
    int Servings,
    IReadOnlyList<RecipePackIngredient> Ingredients,
    IReadOnlyList<RecipePackStep> Steps,
    string? Description = null,
    string? SourceUrl = null,
    string? Image = null);

/// <summary>One ingredient row, where <paramref name="Amount"/> and <paramref name="Unit"/> are required even when null so an unquantified row says so.</summary>
/// <param name="Ingredient">A catalogue entry's name or alias.</param>
/// <param name="Unit">A canonical <see cref="Enums.MeasurementUnit"/>, null when <paramref name="Amount"/> is, and null beside an amount for a count, which loads as <c>pcs</c>.</param>
public record RecipePackIngredient(
    string Ingredient,
    decimal? Amount,
    string? Unit,
    string? Notes = null);

/// <summary>One step.</summary>
/// <param name="Ingredients">Ingredients the step uses by name or alias rather than index, each linking every recipe row that resolves to the same catalogue entry.</param>
public record RecipePackStep(
    string Instruction,
    IReadOnlyList<string> Ingredients);
