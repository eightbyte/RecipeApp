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

    [Fact]
    public void AddPlannedMeal_InvalidBatchSize_Fails()
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, "INVALID"));
        result.ShouldHaveValidationErrorFor(x => x.BatchSize);
    }

    [Fact]
    public void AddPlannedMeal_NullBatchSize_Passes()
    {
        var validator = new AddPlannedMealRequestValidator();
        var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AddPlannedMeal_ValidBatchSizes_Pass()
    {
        var validator = new AddPlannedMealRequestValidator();
        foreach (var batchSize in API.Enums.BatchSize.All)
        {
            var result = validator.TestValidate(new AddPlannedMealRequest(Guid.NewGuid(), null, batchSize));
            result.ShouldNotHaveAnyValidationErrors();
        }
    }

    // ── UpdatePlannedMealRequestValidator ──────────────────────────────────

    [Fact]
    public void UpdatePlannedMeal_EmptyBatchSize_Fails()
    {
        var validator = new UpdatePlannedMealRequestValidator();
        var result = validator.TestValidate(new UpdatePlannedMealRequest(null, ""));
        result.ShouldHaveValidationErrorFor(x => x.BatchSize);
    }

    [Fact]
    public void UpdatePlannedMeal_InvalidBatchSize_Fails()
    {
        var validator = new UpdatePlannedMealRequestValidator();
        var result = validator.TestValidate(new UpdatePlannedMealRequest(null, "HUGE"));
        result.ShouldHaveValidationErrorFor(x => x.BatchSize);
    }

    [Fact]
    public void UpdatePlannedMeal_ValidRequest_Passes()
    {
        var validator = new UpdatePlannedMealRequestValidator();
        var result = validator.TestValidate(new UpdatePlannedMealRequest(new DateOnly(2026, 6, 5), "REGULAR"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
