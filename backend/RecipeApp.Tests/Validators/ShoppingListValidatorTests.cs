using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Enums;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Validators;

public class ShoppingListValidatorTests
{
    // ── AddCustomItemRequestValidator ─────────────────────────────────────────

    private readonly AddCustomItemRequestValidator _addValidator = new();

    [Fact]
    public void AddCustomItem_EmptyName_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("", null, null, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void AddCustomItem_NameOver200Chars_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest(new string('a', 201), null, null, null));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void AddCustomItem_ValidName_Passes()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("Bread", null, null, null));
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void AddCustomItem_InvalidCategory_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", null, null, "INVALID_CATEGORY"));
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void AddCustomItem_NullCategory_Passes()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", null, null, null));
        result.ShouldNotHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void AddCustomItem_ValidCategory_Passes()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", null, null, IngredientCategory.Dairy));
        result.ShouldNotHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void AddCustomItem_NegativeAmount_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", -1m, null, null));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void AddCustomItem_ZeroAmount_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", 0m, null, null));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void AddCustomItem_NullAmount_Passes()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", null, null, null));
        result.ShouldNotHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void AddCustomItem_UnitOver20Chars_Fails()
    {
        var result = _addValidator.TestValidate(new AddCustomItemRequest("X", null, new string('u', 21), null));
        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    // ── UpdateItemRequestValidator ────────────────────────────────────────────

    private readonly UpdateItemRequestValidator _updateValidator = new();

    [Fact]
    public void UpdateItem_NegativeAmount_Fails()
    {
        var result = _updateValidator.TestValidate(new UpdateItemRequest(true, -5m, null));
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void UpdateItem_NullAmount_Passes()
    {
        var result = _updateValidator.TestValidate(new UpdateItemRequest(true, null, null));
        result.ShouldNotHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void UpdateItem_UnitOver20Chars_Fails()
    {
        var result = _updateValidator.TestValidate(new UpdateItemRequest(false, null, new string('x', 21)));
        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void UpdateItem_ValidPayload_Passes()
    {
        var result = _updateValidator.TestValidate(new UpdateItemRequest(true, 1.5m, "kg"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
