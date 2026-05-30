using FluentAssertions;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Enums;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace RecipeApp.Tests.Endpoints;

[Collection("Database")]
public class IngredientsEndpointTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Client => db.CreateClient();

    private async Task<IngredientResponse> SeedIngredient(
        string name = "flour",
        string displayName = "Flour",
        string category = IngredientCategory.DryGoods)
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient(name, displayName, category);
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var response = await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{ing.Id}");
        return response!;
    }

    [Fact]
    public async Task ListAll_Returns200WithArray()
    {
        await SeedIngredient("flour", "Flour");
        await SeedIngredient("salt", "Salt", IngredientCategory.Other);

        var response = await Client.GetAsync("/api/v1/ingredients");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<IngredientResponse>>();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListWithSearch_FiltersResults()
    {
        await SeedIngredient("flour", "Flour");
        await SeedIngredient("salt", "Salt", IngredientCategory.Other);

        var response = await Client.GetAsync("/api/v1/ingredients?search=fl");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<IngredientResponse>>();
        list.Should().HaveCount(1);
        list![0].Name.Should().Be("flour");
    }

    [Fact]
    public async Task ListEmptySearch_ReturnsAll()
    {
        await SeedIngredient("flour", "Flour");
        await SeedIngredient("salt", "Salt", IngredientCategory.Other);

        var response = await Client.GetAsync("/api/v1/ingredients?search=");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<IngredientResponse>>();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetById_ExistingIngredient_Returns200()
    {
        var seeded = await SeedIngredient();

        var response = await Client.GetAsync($"/api/v1/ingredients/{seeded.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        result!.Name.Should().Be("flour");
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync($"/api/v1/ingredients/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCategories_Returns200WithAll10()
    {
        var response = await Client.GetAsync("/api/v1/ingredients/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<List<string>>();
        categories.Should().HaveCount(10);
        categories.Should().Contain(IngredientCategory.All);
    }

    [Fact]
    public async Task Create_Valid_Returns201WithBody()
    {
        var request = new CreateIngredientRequest("sugar", "Sugar", IngredientCategory.DryGoods, null);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        body!.Name.Should().Be("sugar");
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        await SeedIngredient("flour", "Flour");

        var request = new CreateIngredientRequest("flour", "Different Flour", IngredientCategory.DryGoods, null);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_InvalidBody_Returns422()
    {
        var request = new CreateIngredientRequest("", "Flour", IngredientCategory.DryGoods, null);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_Valid_Returns200WithUpdatedBody()
    {
        var seeded = await SeedIngredient("flour", "Plain Flour");
        var request = new UpdateIngredientRequest("Strong Flour", IngredientCategory.DryGoods, "g");

        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        body!.DisplayName.Should().Be("Strong Flour");
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var request = new UpdateIngredientRequest("Anything", IngredientCategory.Other, null);
        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{Guid.NewGuid()}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_InvalidBody_Returns422()
    {
        var seeded = await SeedIngredient();
        var request = new UpdateIngredientRequest("", IngredientCategory.DryGoods, null);

        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
