using FluentAssertions;
using FluentValidation.TestHelper;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Validators;

public class RecipeValidatorTests
{
    private readonly RecipeIngredientRequestValidator _ingredientValidator = new();
    private readonly RecipeStepRequestValidator _stepValidator = new();
    private readonly CreateRecipeValidator _createValidator = new();
    private readonly UpdateRecipeValidator _updateValidator = new();

    // ── RecipeIngredientRequestValidator ──────────────────────────────────────

    [Fact]
    public void Ingredient_Valid_HasNoErrors()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_ZeroAmount_HasErrorOnAmount()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 0m, "g", null, 0));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Ingredient_NegativeAmount_HasErrorOnAmount()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), -1m, "g", null, 0));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Ingredient_InvalidUnit_HasErrorOnUnit()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 100m, "oz", null, 0));
        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Theory]
    [InlineData("g")]
    [InlineData("kg")]
    [InlineData("ml")]
    [InlineData("L")]
    [InlineData("pcs")]
    [InlineData("tsp")]
    [InlineData("tbsp")]
    public void Ingredient_EachValidUnit_HasNoError(string unit)
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 100m, unit, null, 0));
        result.ShouldNotHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void Ingredient_NotesExceeds500Chars_HasErrorOnNotes()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", new string('x', 501), 0));
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Ingredient_NullNotes_HasNoError()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0));
        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Ingredient_EmptyGuidIngredientId_HasErrorOnIngredientId()
    {
        var result = _ingredientValidator.TestValidate(
            new RecipeIngredientRequest(Guid.Empty, 100m, "g", null, 0));
        result.ShouldHaveValidationErrorFor(x => x.IngredientId);
    }

    // ── RecipeStepRequestValidator ────────────────────────────────────────────

    [Fact]
    public void Step_Valid_HasNoErrors()
    {
        var result = _stepValidator.TestValidate(
            new RecipeStepRequest(1, "Boil water", []));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Step_ZeroStepNumber_HasErrorOnStepNumber()
    {
        var result = _stepValidator.TestValidate(
            new RecipeStepRequest(0, "Boil water", []));
        result.ShouldHaveValidationErrorFor(x => x.StepNumber);
    }

    [Fact]
    public void Step_NegativeStepNumber_HasErrorOnStepNumber()
    {
        var result = _stepValidator.TestValidate(
            new RecipeStepRequest(-1, "Boil water", []));
        result.ShouldHaveValidationErrorFor(x => x.StepNumber);
    }

    [Fact]
    public void Step_EmptyInstruction_HasErrorOnInstruction()
    {
        var result = _stepValidator.TestValidate(
            new RecipeStepRequest(1, "", []));
        result.ShouldHaveValidationErrorFor(x => x.Instruction);
    }

    [Fact]
    public void Step_InstructionExceeds2000Chars_HasErrorOnInstruction()
    {
        var result = _stepValidator.TestValidate(
            new RecipeStepRequest(1, new string('x', 2001), []));
        result.ShouldHaveValidationErrorFor(x => x.Instruction);
    }

    // ── CreateRecipeValidator ─────────────────────────────────────────────────

    private static CreateRecipeRequest MinimalValidRequest(
        string name = "Test Recipe",
        int servings = 4) =>
        new(name, null, null, servings,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step 1", [])]);

    [Fact]
    public void Create_ValidMinimal_HasNoErrors()
    {
        var result = _createValidator.TestValidate(MinimalValidRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_EmptyName_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(MinimalValidRequest(name: ""));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_NameExceeds200Chars_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(MinimalValidRequest(name: new string('x', 201)));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_DescriptionExceeds2000Chars_HasErrorOnDescription()
    {
        var req = new CreateRecipeRequest(
            "Test", new string('x', 2001), null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            []);
        var result = _createValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Create_ServingsZero_HasErrorOnServings()
    {
        var result = _createValidator.TestValidate(MinimalValidRequest(servings: 0));
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Fact]
    public void Create_Servings101_HasErrorOnServings()
    {
        var result = _createValidator.TestValidate(MinimalValidRequest(servings: 101));
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Create_ServingsBoundaryValues_HasNoError(int servings)
    {
        var result = _createValidator.TestValidate(MinimalValidRequest(servings: servings));
        result.ShouldNotHaveValidationErrorFor(x => x.Servings);
    }

    [Fact]
    public void Create_EmptyIngredients_HasErrorOnIngredients()
    {
        var req = new CreateRecipeRequest(
            "Test", null, null, 4, [],
            [new RecipeStepRequest(1, "Step 1", [])]);
        var result = _createValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(x => x.Ingredients);
    }

    [Fact]
    public void Create_StepValidIngredientIndex_HasNoErrors()
    {
        var req = new CreateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [0])]);
        var result = _createValidator.TestValidate(req);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_StepOutOfBoundsIngredientIndex_HasError()
    {
        var req = new CreateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [1])]);
        var result = _createValidator.TestValidate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("IngredientIndexes"));
    }

    [Fact]
    public void Create_StepNegativeIngredientIndex_HasError()
    {
        var req = new CreateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [-1])]);
        var result = _createValidator.TestValidate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("IngredientIndexes"));
    }

    [Fact]
    public void Create_StepEmptyIngredientIndexes_HasNoErrors()
    {
        var req = new CreateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [])]);
        var result = _createValidator.TestValidate(req);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdateRecipeValidator — smoke tests ───────────────────────────────────

    private static UpdateRecipeRequest MinimalValidUpdateRequest() =>
        new("Test Recipe", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step 1", [])]);

    [Fact]
    public void Update_ValidMinimal_HasNoErrors()
    {
        var result = _updateValidator.TestValidate(MinimalValidUpdateRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_EmptyName_HasErrorOnName()
    {
        var req = new UpdateRecipeRequest("", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            []);
        var result = _updateValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Update_ServingsZero_HasErrorOnServings()
    {
        var req = new UpdateRecipeRequest("Test", null, null, 0,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            []);
        var result = _updateValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Fact]
    public void Update_EmptyIngredients_HasErrorOnIngredients()
    {
        var req = new UpdateRecipeRequest("Test", null, null, 4, [], []);
        var result = _updateValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(x => x.Ingredients);
    }

    [Fact]
    public void Update_StepOutOfBoundsIndex_HasError()
    {
        var req = new UpdateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(Guid.NewGuid(), 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [1])]);
        var result = _updateValidator.TestValidate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("IngredientIndexes"));
    }
}
