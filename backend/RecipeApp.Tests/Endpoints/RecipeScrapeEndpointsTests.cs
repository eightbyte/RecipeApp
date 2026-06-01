using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace RecipeApp.Tests.Endpoints;

[Collection("Database")]
public class RecipeScrapeEndpointsTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Create a client that replaces IRecipeScrapeService with a fake
    private HttpClient CreateClientWithFake(IRecipeScrapeService fake)
    {
        var factory = db.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecipeScrapeService>();
                services.AddScoped<IRecipeScrapeService>(_ => fake);
            });
        });
        return factory.CreateClient();
    }

    private static ScrapePreviewResponse MakePreview() => new(
        Name:        "Pasta Bolognese",
        Description: "A classic Italian meat sauce",
        Servings:    4,
        SourceUrl:   "https://example.com/bolognese",
        Ingredients:
        [
            new ScrapePreviewIngredient(
                IngredientId:      null,
                Name:              "beef mince",
                DisplayName:       "Beef Mince",
                Amount:            500m,
                Unit:              "g",
                Notes:             null,
                IsNew:             true,
                SuggestedCategory: IngredientCategory.MeatSeafood,
                DisplayOrder:      0)
        ],
        Steps:
        [
            new ScrapePreviewStep(1, "Brown the mince in a pan.", [0])
        ]
    );

    // ── POST /recipes/scrape ──────────────────────────────────────────────────

    [Fact]
    public async Task Scrape_ValidUrl_Returns200WithPreview()
    {
        var fake   = new FakeRecipeScrapeService(MakePreview());
        var client = CreateClientWithFake(fake);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/recipe"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>();
        body!.Name.Should().Be("Pasta Bolognese");
        body.Ingredients.Should().HaveCount(1);
        body.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task Scrape_InvalidUrl_Returns400()
    {
        var fake   = new FakeRecipeScrapeService(MakePreview());
        var client = CreateClientWithFake(fake);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("not-a-url"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scrape_EmptyUrl_Returns400()
    {
        var fake   = new FakeRecipeScrapeService(MakePreview());
        var client = CreateClientWithFake(fake);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scrape_ServiceThrowsFetchError_Returns422()
    {
        var fake   = new FakeRecipeScrapeService(null, throwError: RecipeScrapeError.FetchTimeout);
        var client = CreateClientWithFake(fake);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/recipe"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Scrape_ServiceThrowsNoContent_Returns422()
    {
        var fake   = new FakeRecipeScrapeService(null, throwError: RecipeScrapeError.NoContent);
        var client = CreateClientWithFake(fake);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape",
            new ScrapeRecipeRequest("https://example.com/recipe"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /recipes/scrape/confirm ──────────────────────────────────────────

    [Fact]
    public async Task Confirm_WithNewIngredient_Returns201WithLocationHeader()
    {
        // Use real service (no fake) so it actually persists to DB
        var client  = db.CreateClient();
        var request = BuildConfirmRequest(ingredientId: null, newName: "beef mince",
            newDisplayName: "Beef Mince", category: IngredientCategory.MeatSeafood);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/confirm", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.Name.Should().Be("Bolognese");
        body.Ingredients.Should().HaveCount(1);
        body.Ingredients[0].IngredientDisplayName.Should().Be("Beef Mince");
    }

    [Fact]
    public async Task Confirm_WithExistingIngredient_Returns201()
    {
        await using var ctx    = db.CreateDbContext();
        var ingredient = TestDataBuilder.Ingredient("onion", "Onion", IngredientCategory.Produce);
        ctx.Ingredients.Add(ingredient);
        await ctx.SaveChangesAsync();

        var client  = db.CreateClient();
        var request = BuildConfirmRequest(ingredientId: ingredient.Id);

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/confirm", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.Ingredients[0].IngredientId.Should().Be(ingredient.Id);
    }

    [Fact]
    public async Task Confirm_EmptyName_Returns400()
    {
        var client  = db.CreateClient();
        var request = BuildConfirmRequest(ingredientId: null, newName: "flour",
            newDisplayName: "Flour", category: IngredientCategory.DryGoods,
            recipeName: "");

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/confirm", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_NewIngredientMissingCategory_Returns400()
    {
        var client = db.CreateClient();
        var request = new ScrapeConfirmRequest(
            Name:        "Test Recipe",
            Description: null,
            SourceUrl:   "https://example.com",
            Servings:    4,
            Ingredients: [new ScrapeConfirmIngredient(
                IngredientId:             null,
                NewIngredientName:        "flour",
                NewIngredientDisplayName: "Flour",
                Category:                 null,   // missing
                Amount:                   100m,
                Unit:                     "g",
                Notes:                    null,
                DisplayOrder:             0)],
            Steps: [new ScrapeConfirmStep(1, "Mix", [])]
        );

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/confirm", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScrapeConfirmRequest BuildConfirmRequest(
        Guid? ingredientId = null,
        string? newName = null,
        string? newDisplayName = null,
        string? category = null,
        string recipeName = "Bolognese") => new(
            Name:        recipeName,
            Description: null,
            SourceUrl:   "https://example.com/bolognese",
            Servings:    4,
            Ingredients: [new ScrapeConfirmIngredient(
                IngredientId:             ingredientId,
                NewIngredientName:        ingredientId == null ? newName : null,
                NewIngredientDisplayName: ingredientId == null ? newDisplayName : null,
                Category:                 ingredientId == null ? category : null,
                Amount:                   500m,
                Unit:                     "g",
                Notes:                    null,
                DisplayOrder:             0)],
            Steps: [new ScrapeConfirmStep(1, "Brown the mince.", [0])]
        );
}

// ── Fake service for endpoint tests ──────────────────────────────────────────

internal class FakeRecipeScrapeService(
    ScrapePreviewResponse? preview,
    RecipeScrapeError? throwError = null) : IRecipeScrapeService
{
    public Task<ScrapePreviewResponse> ScrapeAsync(string url, CancellationToken ct = default)
    {
        if (throwError.HasValue)
            throw new RecipeScrapeException(throwError.Value, $"Fake error: {throwError.Value}");

        return Task.FromResult(preview!);
    }

    public Task<RecipeDetailResponse> ConfirmAsync(
        ScrapeConfirmRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Use real service for confirm tests.");
}
