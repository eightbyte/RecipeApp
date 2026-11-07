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

    // ── AddPlannedMealRequestValidator ─────────────────────────────────────

    [Fact]
    public void AddPlannedMeal_EmptyRecipeId_Fails()
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.Empty, null, null));
        result.ShouldHaveValidationErrorFor(x => x.RecipeId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AddPlannedMeal_ServingsOutOfRange_Fails(int servings)
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, servings));
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Fact]
    public void AddPlannedMeal_NullServings_Passes()
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void AddPlannedMeal_ServingsInRange_Passes(int servings)
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, servings));
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdatePlannedMealRequestValidator ──────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void UpdatePlannedMeal_ServingsOutOfRange_Fails(int servings)
    {
        var validator = new UpdatePlannedMealRequestValidator();
        var result = validator.TestValidate(new UpdatePlannedMealRequest(null, servings));
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void UpdatePlannedMeal_ValidRequest_Passes(int servings)
    {
        var validator = new UpdatePlannedMealRequestValidator();
        var result = validator.TestValidate(new UpdatePlannedMealRequest(new DateOnly(2026, 6, 5), servings));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
