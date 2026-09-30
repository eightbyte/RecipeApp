namespace RecipeApp.API.Validators;

/// <summary>
/// Field limits a stored recipe must satisfy to be editable. The create and update validators
/// enforce them at the endpoint; the Phase 9 seed import checks them at persist time, because
/// <c>ConfirmAsync</c> validates nothing and a recipe imported beyond them could be viewed but never
/// saved again from the UI.
/// </summary>
public static class RecipeLimits
{
    public const int NameMaxLength        = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MinServings          = 1;
    public const int MaxServings          = 100;
    public const int IngredientNotesMaxLength = 500;
    public const int StepInstructionMaxLength = 2000;
}
