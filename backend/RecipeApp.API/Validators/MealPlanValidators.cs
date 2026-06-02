using FluentValidation;
using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.Enums;

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

public class AddMealPlanRecipeRequestValidator : AbstractValidator<AddMealPlanRecipeRequest>
{
    public AddMealPlanRecipeRequestValidator()
    {
        RuleFor(x => x.RecipeId).NotEmpty();
        RuleFor(x => x.PortionSize)
            .Must(p => p != null && PortionSize.IsValid(p))
            .WithMessage($"PortionSize must be one of: {string.Join(", ", PortionSize.All)}")
            .When(x => x.PortionSize is not null);
    }
}

public class UpdateMealPlanRecipeRequestValidator : AbstractValidator<UpdateMealPlanRecipeRequest>
{
    public UpdateMealPlanRecipeRequestValidator()
    {
        RuleFor(x => x.PortionSize)
            .NotEmpty()
            .Must(PortionSize.IsValid)
            .WithMessage($"PortionSize must be one of: {string.Join(", ", PortionSize.All)}");
    }
}
