using FluentValidation;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Validators;

public class CreateIngredientValidator : AbstractValidator<CreateIngredientRequest>
{
    public CreateIngredientValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200)
            .Must(n => n == n.Trim().ToLower())
            .WithMessage("Name must be lowercase and trimmed (it is used as a normalised lookup key).");

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(IngredientCategory.IsValid)
            .WithMessage($"Category must be one of: {string.Join(", ", IngredientCategory.All)}");

        RuleFor(x => x.DefaultUnit)
            .MaximumLength(20)
            .When(x => x.DefaultUnit is not null);
    }
}

public class UpdateIngredientValidator : AbstractValidator<UpdateIngredientRequest>
{
    public UpdateIngredientValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(IngredientCategory.IsValid)
            .WithMessage($"Category must be one of: {string.Join(", ", IngredientCategory.All)}");
    }
}
