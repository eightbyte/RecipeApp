using FluentAssertions;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace RecipeApp.Tests.Endpoints;

[Collection("Database")]
public class RecipesEndpointTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Client => db.CreateClient();

    private async Task<Guid> SeedIngredientAsync(string name = "flour")
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient(name, char.ToUpper(name[0]) + name[1..]);
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();
        return ing.Id;
    }

    private async Task<RecipeDetailResponse> CreateRecipeViaApi(Guid ingredientId, string name = "Test Recipe")
    {
        var request = TestDataBuilder.CreateRecipeRequest(ingredientId, name, stepCount: 1);
        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecipeDetailResponse>())!;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAll_Returns200WithArray()
    {
        var ingId = await SeedIngredientAsync();
        await CreateRecipeViaApi(ingId, "Pasta");
        await CreateRecipeViaApi(ingId, "Soup");

        var response = await Client.GetAsync("/api/v1/recipes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<RecipeListItemResponse>>();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListWithNameSearch_FiltersResults()
    {
        var ingId = await SeedIngredientAsync();
        await CreateRecipeViaApi(ingId, "Bolognese");
        await CreateRecipeViaApi(ingId, "Tomato Soup");

        var response = await Client.GetAsync("/api/v1/recipes?search=bolognese");
        var list = await response.Content.ReadFromJsonAsync<List<RecipeListItemResponse>>();

        list.Should().HaveCount(1);
        list![0].Name.Should().Be("Bolognese");
    }

    [Fact]
    public async Task ListWithExcludeRecentDays_ExcludesRecent()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe("Old recipe", lastCookedAt: DateTime.UtcNow.AddDays(-3));
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        var response = await Client.GetAsync("/api/v1/recipes?excludeRecentDays=7");
        var list = await response.Content.ReadFromJsonAsync<List<RecipeListItemResponse>>();

        list.Should().BeEmpty();
    }

    // ── Get by ID ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingRecipe_Returns200WithDetail()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId);

        var response = await Client.GetAsync($"/api/v1/recipes/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.Id.Should().Be(created.Id);
        body.Ingredients.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync($"/api/v1/recipes/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Valid_Returns201WithBody()
    {
        var ingId = await SeedIngredientAsync();
        var request = TestDataBuilder.CreateRecipeRequest(ingId, "Brand New Recipe");

        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.Name.Should().Be("Brand New Recipe");
    }

    [Fact]
    public async Task Create_NoIngredients_Returns400()
    {
        var ingId = await SeedIngredientAsync();
        var request = new CreateRecipeRequest("Test", null, null, 4, [], []);

        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        // Results.ValidationProblem emits RFC 7807 ValidationProblemDetails with status 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_OutOfBoundsStepIndex_Returns400()
    {
        var ingId = await SeedIngredientAsync();
        var request = new CreateRecipeRequest(
            "Test", null, null, 4,
            [new RecipeIngredientRequest(ingId, 100m, "g", null, 0)],
            [new RecipeStepRequest(1, "Step", [1])]);

        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        // Results.ValidationProblem emits RFC 7807 ValidationProblemDetails with status 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Valid_Returns200WithUpdatedBody()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId, "Original Name");

        var updateRequest = TestDataBuilder.UpdateRecipeRequest(ingId, "Updated Name");
        var response = await Client.PutAsJsonAsync($"/api/v1/recipes/{created.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var ingId = await SeedIngredientAsync();
        var request = TestDataBuilder.UpdateRecipeRequest(ingId);

        var response = await Client.PutAsJsonAsync($"/api/v1/recipes/{Guid.NewGuid()}", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Valid_Returns204AndSubsequentGetReturns404()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId);

        var deleteResponse = await Client.DeleteAsync($"/api/v1/recipes/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/v1/recipes/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var response = await Client.DeleteAsync($"/api/v1/recipes/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Upload image ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadImage_TooBig_Returns400()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId);

        var content = new MultipartFormDataContent();
        var fileBytes = new byte[11 * 1024 * 1024]; // 11 MB
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "photo.jpg");

        var response = await Client.PostAsync($"/api/v1/recipes/{created.Id}/image", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadImage_BadType_Returns400()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[100]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
        content.Add(fileContent, "file", "photo.gif");

        var response = await Client.PostAsync($"/api/v1/recipes/{created.Id}/image", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Mark cooked ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkCooked_ExistingRecipe_Returns200WithLastCookedAtSet()
    {
        var ingId = await SeedIngredientAsync();
        var created = await CreateRecipeViaApi(ingId);

        var response = await Client.PostAsync($"/api/v1/recipes/{created.Id}/cook", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RecipeDetailResponse>();
        body!.LastCookedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkCooked_NotFound_Returns404()
    {
        var response = await Client.PostAsync($"/api/v1/recipes/{Guid.NewGuid()}/cook", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
