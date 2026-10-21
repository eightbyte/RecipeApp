using FluentValidation;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;

using static RecipeApp.API.Validators.IngredientValidationRules;

namespace RecipeApp.API.Validators;

/// <summary>Rules shared by the create and update ingredient validators.</summary>
internal static class IngredientValidationRules
{
    /// <summary>Nothing in a kitchen is meaningfully less dense than puffed cereal.</summary>
    public const decimal MinDensity = 0.01m;

    /// <summary>Nothing in a kitchen is denser than about 2.2 g/ml (salt, sugar syrups).</summary>
    public const decimal MaxDensity = 3m;

    public static readonly string DensityMessage =
        $"GramsPerMillilitre must be between {MinDensity} and {MaxDensity}.";

    public static readonly string DefaultUnitMessage =
        $"DefaultUnit must be one of: {string.Join(", ", Enums.MeasurementUnit.All)}";
}

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
            .Must(MeasurementUnit.IsValid)
            .WithMessage(DefaultUnitMessage)
            .When(x => x.DefaultUnit is not null);

        RuleFor(x => x.GramsPerMillilitre)
            .InclusiveBetween(MinDensity, MaxDensity)
            .WithMessage(DensityMessage)
            .When(x => x.GramsPerMillilitre.HasValue);
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

        RuleFor(x => x.DefaultUnit)
            .Must(MeasurementUnit.IsValid)
            .WithMessage(DefaultUnitMessage)
            .When(x => x.DefaultUnit is not null);

        RuleFor(x => x.GramsPerMillilitre)
            .InclusiveBetween(MinDensity, MaxDensity)
            .WithMessage(DensityMessage)
            .When(x => x.GramsPerMillilitre.HasValue);
    }
}
