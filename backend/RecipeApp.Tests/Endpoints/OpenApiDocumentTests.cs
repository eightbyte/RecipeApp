using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.Tests.Infrastructure;
using System.Text.RegularExpressions;

namespace RecipeApp.Tests.Endpoints;

/// <summary>Checks the document Scalar renders, which <c>[AsParameters]</c> changes silently by naming parameters after record properties.</summary>
[Collection("Database")]
public partial class OpenApiDocumentTests(DatabaseFixture db)
{
    /// <summary>The document name AddOpenApi() registers by default; Scalar renders it at /scalar/v1.</summary>
    private const string OpenApiDocumentName = "v1";

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCase();

    [Fact]
    public async Task EveryParameter_IsNamedInCamelCase()
    {
        var document = await db.Factory.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(OpenApiDocumentName)
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        var parameters = (
            from path in document.Paths
            from operation in path.Value.Operations ?? []
            from parameter in operation.Value.Parameters ?? []
            select (Operation: $"{operation.Key} {path.Key}", parameter.Name)).ToList();

        parameters.Should().Contain(parameter => parameter.Name == "sort",
            "the recipe list's [AsParameters] query is the case this guards");
        parameters.Where(parameter => !CamelCase().IsMatch(parameter.Name ?? ""))
            .Should().BeEmpty("the frontend and the API docs use camelCase parameter names");
    }
}
