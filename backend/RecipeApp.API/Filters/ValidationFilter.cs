using FluentValidation;

namespace RecipeApp.API.Filters;

/// <summary>
/// Validates the endpoint's <typeparamref name="TRequest"/> argument and short-circuits with an
/// RFC 7807 validation problem when it fails. Replaces the hand-copied validate-and-return block
/// that previously opened every mutating endpoint.
/// </summary>
/// <remarks>
/// Validators are registered scoped by <c>AddValidatorsFromAssemblyContaining</c>, and
/// <c>AddEndpointFilter&lt;TFilterType&gt;</c> builds the filter per request from
/// <c>HttpContext.RequestServices</c> — so the constructor-injected validator resolves from the
/// request scope, not the root provider.
/// </remarks>
public class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Endpoint has no argument of type {typeof(TRequest).Name} to validate.");

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        return result.IsValid
            ? await next(context)
            : Results.ValidationProblem(result.ToDictionary());
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Validates <typeparamref name="TRequest"/> and documents the 400 response.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
        => builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
