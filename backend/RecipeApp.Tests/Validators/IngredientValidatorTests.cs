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

    [Theory]
    [InlineData("teaspoon")]
    [InlineData("Cup")]
    [InlineData("clove")]
    public void Create_NonCanonicalDefaultUnit_HasErrorOnDefaultUnit(string unit)
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, unit));
        result.ShouldHaveValidationErrorFor(x => x.DefaultUnit);
    }

    [Theory]
    [MemberData(nameof(AllUnits))]
    public void Create_EachStorableDefaultUnit_HasNoError(string unit)
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, unit));
        result.ShouldNotHaveValidationErrorFor(x => x.DefaultUnit);
    }

    public static IEnumerable<object[]> AllUnits =>
        MeasurementUnit.All.Select(u => new object[] { u });

    // ── Density ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_NullDensity_HasNoError()
    {
        // Null is load-bearing: "no reliable density known", not "not filled in yet".
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("spinach", "Spinach", IngredientCategory.Produce, "g"));
        result.ShouldNotHaveValidationErrorFor(x => x.GramsPerMillilitre);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.2167)]
    [InlineData(3)]
    public void Create_PlausibleDensity_HasNoError(decimal density)
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, "g", density));
        result.ShouldNotHaveValidationErrorFor(x => x.GramsPerMillilitre);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.009)]
    [InlineData(3.01)]
    [InlineData(1000)]
    public void Create_ImplausibleDensity_HasErrorOnDensity(decimal density)
    {
        var result = _createValidator.TestValidate(
            new CreateIngredientRequest("flour", "Plain Flour", IngredientCategory.DryGoods, "g", density));
        result.ShouldHaveValidationErrorFor(x => x.GramsPerMillilitre);
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

    [Fact]
    public void Update_NonCanonicalDefaultUnit_HasErrorOnDefaultUnit()
    {
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("Plain Flour", IngredientCategory.DryGoods, "teaspoon"));
        result.ShouldHaveValidationErrorFor(x => x.DefaultUnit);
    }

    [Fact]
    public void Update_PlausibleDensity_HasNoErrors()
    {
        // Correcting a density is the supported repair path when a conversion turns out wrong.
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("Plain Flour", IngredientCategory.DryGoods, "g", 0.5m));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_ImplausibleDensity_HasErrorOnDensity()
    {
        var result = _updateValidator.TestValidate(
            new UpdateIngredientRequest("Plain Flour", IngredientCategory.DryGoods, "g", 12m));
        result.ShouldHaveValidationErrorFor(x => x.GramsPerMillilitre);
    }
}
