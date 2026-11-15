using FluentValidation;
using RecipeApp.API.DTOs.Llm;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Validators;

public class LlmUsageQueryValidator : AbstractValidator<LlmUsageQuery>
{
    public LlmUsageQueryValidator()
    {
        RuleFor(x => x.Operation)
            .Must(operation => Enum.GetNames<LlmOperation>().Contains(operation))
            .WithMessage($"Operation must be one of: {string.Join(", ", Enum.GetNames<LlmOperation>())}")
            .When(x => x.Operation is not null);

        RuleFor(x => x.Provider)
            .Must(provider => LlmProviderNames.TryParse(provider, out _))
            .WithMessage($"Provider must be one of: {string.Join(", ", LlmProviderNames.All)}")
            .When(x => x.Provider is not null);

        RuleFor(x => x.To)
            .GreaterThan(x => x.From)
            .WithMessage("To must be later than From.")
            .When(x => x.From is not null && x.To is not null);

        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, LlmUsagePaging.MaxPageSize);
    }
}
