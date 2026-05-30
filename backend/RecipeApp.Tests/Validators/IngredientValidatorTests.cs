using FluentValidation.TestHelper;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Validators;

public class IngredientValidatorTests
{
    private readonly CreateIngredientValidator _createValidator = new();
    private readonly UpdateIngredientValidator _updateValidator = new();

    // ── CreateIngredientValidator ─────────────────────────────────────────────

    [Fact]
    public void Create_Valid_HasNoErrors()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_EmptyName_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("", "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_UppercaseName_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("Flour", "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_LeadingWhitespaceName_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest(" flour", "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_NameExceeds200Chars_HasErrorOnName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest(new string('a', 201), "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_EmptyDisplayName_HasErrorOnDisplayName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Create_DisplayNameExceeds200Chars_HasErrorOnDisplayName()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", new string('A', 201), IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Create_InvalidCategory_HasErrorOnCategory()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", "JUNK", null));
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Theory]
    [MemberData(nameof(AllCategories))]
    public void Create_EachValidCategory_HasNoError(string category)
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", category, null));
        result.ShouldNotHaveValidationErrorFor(x => x.Category);
    }

    public static IEnumerable<object[]> AllCategories =>
        IngredientCategory.All.Select(c => new object[] { c });

    [Fact]
    public void Create_DefaultUnitExceeds20Chars_HasErrorOnDefaultUnit()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, new string('x', 21)));
        result.ShouldHaveValidationErrorFor(x => x.DefaultUnit);
    }

    [Fact]
    public void Create_NullDefaultUnit_HasNoError()
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldNotHaveValidationErrorFor(x => x.DefaultUnit);
    }

    // ── UpdateIngredientValidator ─────────────────────────────────────────────

    [Fact]
    public void Update_Valid_HasNoErrors()
    {
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("Plain Flour", IngredientCategory.DryGoods, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_EmptyDisplayName_HasErrorOnDisplayName()
    {
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("", IngredientCategory.DryGoods, null));
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Update_InvalidCategory_HasErrorOnCategory()
    {
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("Plain Flour", "INVALID", null));
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }
}
