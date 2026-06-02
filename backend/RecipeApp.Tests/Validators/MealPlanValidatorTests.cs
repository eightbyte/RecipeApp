using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Validators;

public class MealPlanValidatorTests
{
    // ── CreateMealPlanRequestValidator ────────────────────────────────────────

    [Fact]
    public void CreateMealPlan_EmptyName_Fails()
    {
        var validator = new CreateMealPlanRequestValidator();
        var result = validator.TestValidate(new CreateMealPlanRequest(""));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateMealPlan_NameTooLong_Fails()
    {
        var validator = new CreateMealPlanRequestValidator();
        var result = validator.TestValidate(new CreateMealPlanRequest(new string('x', 201)));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateMealPlan_ValidName_Passes()
    {
        var validator = new CreateMealPlanRequestValidator();
        var result = validator.TestValidate(new CreateMealPlanRequest("Week of 2 June"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdateMealPlanRequestValidator ────────────────────────────────────────

    [Fact]
    public void UpdateMealPlan_EmptyName_Fails()
    {
        var validator = new UpdateMealPlanRequestValidator();
        var result = validator.TestValidate(new UpdateMealPlanRequest(""));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    // ── AddMealPlanRecipeRequestValidator ─────────────────────────────────────

    [Fact]
    public void AddMealPlanRecipe_EmptyRecipeId_Fails()
    {
        var validator = new AddMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new AddMealPlanRecipeRequest(Guid.Empty, null, null));
        result.ShouldHaveValidationErrorFor(x => x.RecipeId);
    }

    [Fact]
    public void AddMealPlanRecipe_InvalidPortionSize_Fails()
    {
        var validator = new AddMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new AddMealPlanRecipeRequest(Guid.NewGuid(), null, "INVALID"));
        result.ShouldHaveValidationErrorFor(x => x.PortionSize);
    }

    [Fact]
    public void AddMealPlanRecipe_NullPortionSize_Passes()
    {
        var validator = new AddMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new AddMealPlanRecipeRequest(Guid.NewGuid(), null, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AddMealPlanRecipe_ValidPortionSizes_Pass()
    {
        var validator = new AddMealPlanRecipeRequestValidator();
        foreach (var portion in API.Enums.PortionSize.All)
        {
            var result = validator.TestValidate(new AddMealPlanRecipeRequest(Guid.NewGuid(), null, portion));
            result.ShouldNotHaveAnyValidationErrors();
        }
    }

    // ── UpdateMealPlanRecipeRequestValidator ──────────────────────────────────

    [Fact]
    public void UpdateMealPlanRecipe_EmptyPortionSize_Fails()
    {
        var validator = new UpdateMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new UpdateMealPlanRecipeRequest(null, ""));
        result.ShouldHaveValidationErrorFor(x => x.PortionSize);
    }

    [Fact]
    public void UpdateMealPlanRecipe_InvalidPortionSize_Fails()
    {
        var validator = new UpdateMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new UpdateMealPlanRecipeRequest(null, "HUGE"));
        result.ShouldHaveValidationErrorFor(x => x.PortionSize);
    }

    [Fact]
    public void UpdateMealPlanRecipe_ValidRequest_Passes()
    {
        var validator = new UpdateMealPlanRecipeRequestValidator();
        var result = validator.TestValidate(new UpdateMealPlanRecipeRequest(new DateOnly(2026, 6, 5), "REGULAR"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
