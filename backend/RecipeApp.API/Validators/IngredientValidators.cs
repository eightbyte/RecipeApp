using System.Linq.Expressions;
using FluentValidation;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;

using static RecipeApp.API.Validators.IngredientValidationRules;

namespace RecipeApp.API.Validators;

/// <summary>Rules shared by the create and update ingredient validators.</summary>
internal static class IngredientValidationRules
{
    /// <summary>The longest display name, and so the longest name or alias.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Nothing in a kitchen is meaningfully less dense than puffed cereal.</summary>
    public const decimal MinDensity = 0.01m;

    /// <summary>Nothing in a kitchen is denser than about 2.2 g/ml (salt, sugar syrups).</summary>
    public const decimal MaxDensity = 3m;

    public static readonly string DensityMessage =
        $"GramsPerMillilitre must be between {MinDensity} and {MaxDensity}.";

    public static readonly string DefaultUnitMessage =
        $"DefaultUnit must be one of: {string.Join(", ", Enums.MeasurementUnit.All)}";

    public static readonly string CategoryMessage =
        $"Category must be one of: {string.Join(", ", IngredientCategory.All)}";

    /// <summary>Adds the rules every hand edit of an Ingredient shares, given how to read each field from the request.</summary>
    public static void AddIngredientRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> displayName,
        Expression<Func<T, string>> category,
        Expression<Func<T, string?>> defaultUnit,
        Expression<Func<T, decimal?>> density,
        Expression<Func<T, IEnumerable<string>?>> aliases)
    {
        // NotEmpty also refuses whitespace, which would normalise to a blank Name.
        validator.RuleFor(displayName).NotEmpty().MaximumLength(NameMaxLength);

        validator.RuleFor(category).NotEmpty().Must(IngredientCategory.IsValid).WithMessage(CategoryMessage);

        validator.RuleFor(defaultUnit)
            .Must(unit => unit is null || MeasurementUnit.IsValid(unit))
            .WithMessage(DefaultUnitMessage);

        validator.RuleFor(density)
            .Must(value => value is null or >= MinDensity and <= MaxDensity)
            .WithMessage(DensityMessage);

        validator.RuleForEach(aliases).NotNull().MaximumLength(NameMaxLength);
    }
}

public class CreateIngredientValidator : AbstractValidator<CreateIngredientRequest>
{
    public CreateIngredientValidator() =>
        this.AddIngredientRules(x => x.DisplayName, x => x.Category, x => x.DefaultUnit, x => x.GramsPerMillilitre, x => x.Aliases);
}

public class UpdateIngredientValidator : AbstractValidator<UpdateIngredientRequest>
{
    public UpdateIngredientValidator() =>
        this.AddIngredientRules(x => x.DisplayName, x => x.Category, x => x.DefaultUnit, x => x.GramsPerMillilitre, x => x.Aliases);
}
