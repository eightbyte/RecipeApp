using FluentValidation;
using RecipeApp.API.DTOs.Recipes;

namespace RecipeApp.API.Validators;

public class RecipeIngredientRequestValidator : AbstractValidator<RecipeIngredientRequest>
{
    private static readonly string[] ValidUnits =
        ["g", "kg", "ml", "L", "pcs", "tsp", "tbsp"];

    public RecipeIngredientRequestValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Unit)
            .NotEmpty()
            .Must(u => ValidUnits.Contains(u))
            .WithMessage($"Unit must be one of: {string.Join(", ", ValidUnits)}");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}

public class RecipeStepRequestValidator : AbstractValidator<RecipeStepRequest>
{
    public RecipeStepRequestValidator()
    {
        RuleFor(x => x.StepNumber).GreaterThan(0);
        RuleFor(x => x.Instruction).NotEmpty().MaximumLength(2000);
    }
}

public class CreateRecipeValidator : AbstractValidator<CreateRecipeRequest>
{
    public CreateRecipeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Servings).InclusiveBetween(1, 100);
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
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Servings).InclusiveBetween(1, 100);
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
