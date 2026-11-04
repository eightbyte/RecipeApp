using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;
using RecipeApp.Tests.Infrastructure;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RecipeApp.Tests.Filters;

/// <summary>Covers <c>ValidationFilter&lt;TRequest&gt;</c>, which must reject exactly what the old inline validate-and-return block rejected and let through exactly what it let through.</summary>
[Collection("Database")]
public class ValidationFilterTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>The document name AddOpenApi() registers by default; Scalar renders it at /scalar/v1.</summary>
    private const string OpenApiDocumentName = "v1";

    private HttpClient Client => db.CreateClient();

    private static CreateIngredientRequest ValidCreate(string name = "flour") =>
        new(name, "Plain Flour", IngredientCategory.DryGoods, MeasurementUnit.Gram);

    // ── Rejection: the filter short-circuits with RFC 7807 ────────────────────

    [Fact]
    public async Task Post_InvalidBody_Returns400ProblemDetailsKeyedByProperty()
    {
        // Empty Name violates CreateIngredientValidator's NotEmpty rule.
        var response = await Client.PostAsJsonAsync(
            "/api/v1/ingredients",
            ValidCreate() with { Name = "" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestContext.Current.CancellationToken);

        // ValidationResult.ToDictionary() keys by property name, the half of the response contract FluentValidation owns.
        problem!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Errors.Should().ContainKey(nameof(CreateIngredientRequest.Name));
    }

    [Fact]
    public async Task Put_InvalidBody_Returns400ProblemDetailsKeyedByProperty()
    {
        var seeded = await SeedIngredientAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/ingredients/{seeded.Id}",
            new UpdateIngredientRequest("", IngredientCategory.DryGoods, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestContext.Current.CancellationToken);

        problem!.Errors.Should().ContainKey(nameof(UpdateIngredientRequest.DisplayName));
    }

    [Fact]
    public async Task InvalidBody_DoesNotReachTheHandler()
    {
        // A filter that ran the handler anyway would still 400 on the way out, so the absence of the side effect proves the short-circuit.
        await Client.PostAsJsonAsync(
            "/api/v1/ingredients",
            ValidCreate() with { Name = "" },
            TestContext.Current.CancellationToken);

        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Should().BeEmpty();
    }

    // ── Acceptance: valid requests still reach their handler ──────────────────

    [Fact]
    public async Task Post_ValidBody_ReachesHandlerAndReturns201()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/ingredients", ValidCreate(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<IngredientResponse>(
            TestContext.Current.CancellationToken);
        created!.Name.Should().Be("flour");
    }

    [Fact]
    public async Task Put_ValidBody_ReachesHandlerAndReturns200()
    {
        var seeded = await SeedIngredientAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/ingredients/{seeded.Id}",
            new UpdateIngredientRequest("Strong Flour", IngredientCategory.DryGoods, MeasurementUnit.Gram),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<IngredientResponse>(
            TestContext.Current.CancellationToken);
        body!.DisplayName.Should().Be("Strong Flour");
    }

    [Fact]
    public async Task ValidatorResolvesPerRequest_AcrossConsecutiveRequests()
    {
        // Validators are scoped, so building the filter from the root provider or capturing one scope would fail, which three requests through one client cover.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await Client.PostAsJsonAsync(
                "/api/v1/ingredients",
                ValidCreate() with { Name = "" },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    // ── OpenAPI: the 400 is declared, not just returned ───────────────────────

    [Fact]
    public async Task GeneratedOpenApiDocument_DeclaresThe400OnExactlyTheValidatedEndpoints()
    {
        // Asserted against the document Scalar renders, because every validated endpoint must document the 400 and nothing else may.
        var expected = DiscoverValidatedOperations();

        expected.Should().HaveCount(14,
            "the thirteen mutating endpoints and the recipe list query each take a request type with a registered validator");

        var document = await db.Factory.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(OpenApiDocumentName)
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        var documented =
            from path in document.Paths
            from operation in path.Value.Operations ?? []
            where operation.Value.Responses?.ContainsKey(
                StatusCodes.Status400BadRequest.ToString(CultureInfo.InvariantCulture)) == true
            select (Method: operation.Key.ToString(), Path: path.Key);

        documented.Should().BeEquivalentTo(expected);
    }

    /// <summary>The endpoints that take a request type with a registered validator, as OpenAPI (method, path) pairs, discovered so a future endpoint missing <c>.WithValidation&lt;T&gt;()</c> fails this test.</summary>
    private List<(string Method, string Path)> DiscoverValidatedOperations()
    {
        using var scope = db.Factory.Services.CreateScope();

        return db.Factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => (endpoint.Metadata.GetMetadata<MethodInfo>()?.GetParameters() ?? [])
                .Any(parameter => parameter.ParameterType.IsClass &&
                                  scope.ServiceProvider.GetService(
                                      typeof(IValidator<>).MakeGenericType(parameter.ParameterType)) is not null))
            .SelectMany(
                endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [],
                (endpoint, method) => (method, ToOpenApiPath(endpoint.RoutePattern.RawText!)))
            .ToList();
    }

    /// <summary>Converts a route pattern with inline constraints and a trailing slash to the OpenAPI form, such as <c>/api/v1/ingredients/{id}</c>.</summary>
    private static string ToOpenApiPath(string routePattern)
    {
        var withoutConstraints = Regex.Replace(routePattern, @"\{(\w+)[^}]*\}", "{$1}");
        return withoutConstraints.Length > 1
            ? withoutConstraints.TrimEnd('/')
            : withoutConstraints;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IngredientResponse> SeedIngredientAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/ingredients", ValidCreate(), TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<IngredientResponse>(
            TestContext.Current.CancellationToken))!;
    }
}
