using FluentValidation;
using RecipeApp.API.DTOs.MealPlans;

namespace RecipeApp.API.Validators;

public class CreateMealPlanRequestValidator : AbstractValidator<CreateMealPlanRequest>
{
    public CreateMealPlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class UpdateMealPlanRequestValidator : AbstractValidator<UpdateMealPlanRequest>
{
    public UpdateMealPlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class AddPlannedMealRequestValidator : AbstractValidator<AddPlannedMealRequest>
{
    public AddPlannedMealRequestValidator()
    {
        RuleFor(x => x.RecipeId).NotEmpty();
        RuleFor(x => x.Servings).InclusiveBetween(RecipeLimits.MinServings, RecipeLimits.MaxServings);
    }
}

public class UpdatePlannedMealRequestValidator : AbstractValidator<UpdatePlannedMealRequest>
{
    public UpdatePlannedMealRequestValidator()
    {
        RuleFor(x => x.Servings).InclusiveBetween(RecipeLimits.MinServings, RecipeLimits.MaxServings);
    }
}
