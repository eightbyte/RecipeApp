using FluentValidation;
using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Validators;

public class AddCustomItemRequestValidator : AbstractValidator<AddCustomItemRequest>
{
    public AddCustomItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Item name is required.")
            .MaximumLength(200).WithMessage("Item name must not exceed 200 characters.");

        RuleFor(x => x.Category)
            .Must(c => c is null || IngredientCategory.IsValid(c))
            .WithMessage($"Category must be one of: {string.Join(", ", IngredientCategory.All)}.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).When(x => x.Amount.HasValue)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Unit)
            .MaximumLength(20).When(x => x.Unit is not null)
            .WithMessage("Unit must not exceed 20 characters.");
    }
}

public class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).When(x => x.Amount.HasValue)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Unit)
            .MaximumLength(20).When(x => x.Unit is not null)
            .WithMessage("Unit must not exceed 20 characters.");
    }
}
