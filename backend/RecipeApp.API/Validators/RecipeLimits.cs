namespace RecipeApp.API.Validators;

/// <summary>
/// Field limits a stored recipe must satisfy to be editable, enforced by the validators at the endpoint and checked at persist time
/// by the seed import because <c>ConfirmAsync</c> validates nothing.
/// </summary>
public static class RecipeLimits
{
    public const int NameMaxLength        = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MinServings          = 1;
    public const int MaxServings          = 100;
    public const int IngredientNotesMaxLength = 500;
    public const int StepInstructionMaxLength = 2000;
    /// <summary>Caps the remote photo URL a scrape confirm downloads, not the stored path, which the server names itself.</summary>
    public const int ImageUrlMaxLength        = 2048;
}
