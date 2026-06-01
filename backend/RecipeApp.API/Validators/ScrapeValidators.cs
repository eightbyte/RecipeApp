using FluentValidation;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Validators;

public class ScrapeRecipeRequestValidator : AbstractValidator<ScrapeRecipeRequest>
{
    public ScrapeRecipeRequestValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("A valid http or https URL is required.")
            .Must(BeValidHttpUrl).WithMessage("A valid http or https URL is required.");
    }

    private static bool BeValidHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

public class ScrapeConfirmIngredientValidator : AbstractValidator<ScrapeConfirmIngredient>
{
    public ScrapeConfirmIngredientValidator()
    {
        RuleFor(x => x).Custom((ing, ctx) =>
        {
            if (!ing.IngredientId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(ing.NewIngredientName))
                    ctx.AddFailure(nameof(ScrapeConfirmIngredient.NewIngredientName),
                        "NewIngredientName is required when IngredientId is not provided.");
                if (string.IsNullOrWhiteSpace(ing.NewIngredientDisplayName))
                    ctx.AddFailure(nameof(ScrapeConfirmIngredient.NewIngredientDisplayName),
                        "NewIngredientDisplayName is required when IngredientId is not provided.");
                if (string.IsNullOrWhiteSpace(ing.Category))
                    ctx.AddFailure(nameof(ScrapeConfirmIngredient.Category),
                        "Category is required when IngredientId is not provided.");
            }
        });

        RuleFor(x => x.Category)
            .Must(c => c == null || IngredientCategory.IsValid(c))
            .WithMessage($"Category must be one of: {string.Join(", ", IngredientCategory.All)}")
            .When(x => !string.IsNullOrWhiteSpace(x.Category));

        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Notes).MaximumLength(200).When(x => x.Notes is not null);
    }
}

public class ScrapeConfirmRequestValidator : AbstractValidator<ScrapeConfirmRequest>
{
    public ScrapeConfirmRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Servings).InclusiveBetween(1, 100);
        RuleFor(x => x.SourceUrl).NotEmpty();
        RuleFor(x => x.Ingredients).NotEmpty().WithMessage("At least one ingredient is required.");
        RuleForEach(x => x.Ingredients).SetValidator(new ScrapeConfirmIngredientValidator());

        // Step ingredient indexes must be within bounds of the ingredients list
        RuleFor(x => x).Custom((req, ctx) =>
        {
            for (int s = 0; s < req.Steps.Count; s++)
            {
                var step = req.Steps[s];
                if (step.StepNumber < 1)
                    ctx.AddFailure($"Steps[{s}].StepNumber", "StepNumber must be ≥ 1.");
                if (string.IsNullOrWhiteSpace(step.Instruction))
                    ctx.AddFailure($"Steps[{s}].Instruction", "Instruction is required.");

                foreach (var idx in step.IngredientIndexes)
                {
                    if (idx < 0 || idx >= req.Ingredients.Count)
                        ctx.AddFailure($"Steps[{s}].IngredientIndexes",
                            $"Index {idx} is out of range for the ingredients list (0–{req.Ingredients.Count - 1}).");
                }
            }
        });
    }
}
