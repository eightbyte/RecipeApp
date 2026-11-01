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

public class AddPlannedMealRequestValidator : AbstractValidator<AddPlannedMealRequest>
{
    public AddPlannedMealRequestValidator()
    {
        RuleFor(x => x.RecipeId).NotEmpty();
        RuleFor(x => x.BatchSize)
            .Must(batchSize => batchSize != null && BatchSize.IsValid(batchSize))
            .WithMessage($"BatchSize must be one of: {string.Join(", ", BatchSize.All)}")
            .When(x => x.BatchSize is not null);
    }
}

public class UpdatePlannedMealRequestValidator : AbstractValidator<UpdatePlannedMealRequest>
{
    public UpdatePlannedMealRequestValidator()
    {
        RuleFor(x => x.BatchSize)
            .NotEmpty()
            .Must(BatchSize.IsValid)
            .WithMessage($"BatchSize must be one of: {string.Join(", ", BatchSize.All)}");
    }
}
