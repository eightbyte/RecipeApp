using FluentValidation;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Validators;

public class RecipeIngredientRequestValidator : AbstractValidator<RecipeIngredientRequest>
{
    public RecipeIngredientRequestValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty();

        // Amount and Unit are null together or set together: a half-set pair is a half-written row, and zero is still rejected as a wrong quantity.
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .When(x => x.Amount.HasValue)
            .WithMessage("Amount must be greater than zero, or null when no quantity is stated.");

        RuleFor(x => x.Unit)
            .NotEmpty()
            .Must(MeasurementUnit.IsValid)
            .WithMessage($"Unit must be one of: {string.Join(", ", MeasurementUnit.All)}")
            .When(x => x.Amount.HasValue);

        RuleFor(x => x.Unit)
            .Null()
            .WithMessage("Unit must be null when no amount is stated.")
            .When(x => !x.Amount.HasValue);

        RuleFor(x => x.SourceAmount).GreaterThan(0).When(x => x.SourceAmount.HasValue);
        RuleFor(x => x.SourceUnit).NotEmpty().MaximumLength(32).When(x => x.SourceUnit is not null);
        RuleFor(x => x.Notes).MaximumLength(RecipeLimits.IngredientNotesMaxLength).When(x => x.Notes is not null);
    }
}

public class RecipeStepRequestValidator : AbstractValidator<RecipeStepRequest>
{
    public RecipeStepRequestValidator()
    {
        RuleFor(x => x.StepNumber).GreaterThan(0);
        RuleFor(x => x.Instruction).NotEmpty().MaximumLength(RecipeLimits.StepInstructionMaxLength);
    }
}

public class CreateRecipeValidator : AbstractValidator<CreateRecipeRequest>
{
    public CreateRecipeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(RecipeLimits.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(RecipeLimits.DescriptionMaxLength).When(x => x.Description is not null);
        RuleFor(x => x.Servings).InclusiveBetween(RecipeLimits.MinServings, RecipeLimits.MaxServings);
        RuleFor(x => x.Ingredients).NotEmpty().WithMessage("A recipe must have at least one ingredient.");
        RuleForEach(x => x.Ingredients).SetValidator(new RecipeIngredientRequestValidator());
        RuleForEach(x => x.Steps).SetValidator(new RecipeStepRequestValidator());

        // Step ingredient indexes must point into the ingredients list
        RuleFor(x => x).Custom((req, ctx) =>
        {
            for (int i = 0; i < req.Steps.Count; i++)
            {
                foreach (var idx in req.Steps[i].IngredientIndexes)
                {
                    if (idx < 0 || idx >= req.Ingredients.Count)
                        ctx.AddFailure($"Steps[{i}].IngredientIndexes",
                            $"Index {idx} is out of range for the ingredients list (0–{req.Ingredients.Count - 1}).");
                }
            }
        });
    }
}

public class UpdateRecipeValidator : AbstractValidator<UpdateRecipeRequest>
{
    public UpdateRecipeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(RecipeLimits.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(RecipeLimits.DescriptionMaxLength).When(x => x.Description is not null);
        RuleFor(x => x.Servings).InclusiveBetween(RecipeLimits.MinServings, RecipeLimits.MaxServings);
        RuleFor(x => x.Ingredients).NotEmpty().WithMessage("A recipe must have at least one ingredient.");
        RuleForEach(x => x.Ingredients).SetValidator(new RecipeIngredientRequestValidator());
        RuleForEach(x => x.Steps).SetValidator(new RecipeStepRequestValidator());

        RuleFor(x => x).Custom((req, ctx) =>
        {
            for (int i = 0; i < req.Steps.Count; i++)
            {
                foreach (var idx in req.Steps[i].IngredientIndexes)
                {
                    if (idx < 0 || idx >= req.Ingredients.Count)
                        ctx.AddFailure($"Steps[{i}].IngredientIndexes",
                            $"Index {idx} is out of range for the ingredients list (0–{req.Ingredients.Count - 1}).");
                }
            }
        });
    }
}

public class RecipeListQueryValidator : AbstractValidator<RecipeListQuery>
{
    public RecipeListQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort != null && RecipeSort.IsValid(sort))
            .WithMessage($"Sort must be one of: {string.Join(", ", RecipeSort.All)}")
            .When(x => x.Sort is not null);

        RuleFor(x => x.Direction)
            .Must(direction => direction != null && SortDirection.IsValid(direction))
            .WithMessage($"Direction must be one of: {string.Join(", ", SortDirection.All)}")
            .When(x => x.Direction is not null);
    }
}
