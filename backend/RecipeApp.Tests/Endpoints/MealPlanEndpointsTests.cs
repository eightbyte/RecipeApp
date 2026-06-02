using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace RecipeApp.Tests.Endpoints;

[Collection("Database")]
public class MealPlanEndpointsTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Client => db.CreateClient();

    private async Task<Guid> SeedIngredientAsync()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();
        return ing.Id;
    }

    private async Task<MealPlanDetailResponse> CreatePlanViaApi(string name = "Test Plan")
    {
        var response = await Client.PostAsJsonAsync("/api/v1/meal-plans", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MealPlanDetailResponse>())!;
    }

    private async Task<Guid> SeedRecipeAsync(Guid ingredientId, string name = "Test Recipe")
    {
        var request = TestDataBuilder.CreateRecipeRequest(ingredientId, name);
        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        response.EnsureSuccessStatusCode();
        var recipe = await response.Content.ReadFromJsonAsync<API.DTOs.Recipes.RecipeDetailResponse>();
        return recipe!.Id;
    }

    // ── GET /meal-plans/active ────────────────────────────────────────────────

    [Fact]
    public async Task GetActive_NoActivePlan_Returns404()
    {
        var response = await Client.GetAsync("/api/v1/meal-plans/active");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetActive_WithActivePlan_Returns200()
    {
        await CreatePlanViaApi("Active Plan");
        var response = await Client.GetAsync("/api/v1/meal-plans/active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await response.Content.ReadFromJsonAsync<MealPlanDetailResponse>();
        plan!.IsActive.Should().BeTrue();
    }

    // ── POST /meal-plans ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Valid_Returns201WithLocationHeader()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/meal-plans", new { name = "My Plan" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var plan = await response.Content.ReadFromJsonAsync<MealPlanDetailResponse>();
        plan!.Name.Should().Be("My Plan");
        plan.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/meal-plans", new { name = "" });
        // API returns 400 Bad Request for validation failures (Results.ValidationProblem default)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_DeactivatesPreviousActivePlan()
    {
        var first  = await CreatePlanViaApi("First");
        await CreatePlanViaApi("Second");

        var firstResponse = await Client.GetAsync($"/api/v1/meal-plans/{first.Id}");
        var firstPlan = await firstResponse.Content.ReadFromJsonAsync<MealPlanDetailResponse>();
        firstPlan!.IsActive.Should().BeFalse();
        firstPlan.ClosedAt.Should().NotBeNull();
    }

    // ── GET /meal-plans/{id} ──────────────────────────────────────────────────

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync($"/api/v1/meal-plans/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /meal-plans/{id} ──────────────────────────────────────────────────

    [Fact]
    public async Task Update_Valid_Returns200()
    {
        var plan     = await CreatePlanViaApi("Original");
        var response = await Client.PutAsJsonAsync($"/api/v1/meal-plans/{plan.Id}", new { name = "Renamed" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<MealPlanDetailResponse>();
        updated!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var response = await Client.PutAsJsonAsync($"/api/v1/meal-plans/{Guid.NewGuid()}", new { name = "X" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /meal-plans/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task Delete_Valid_Returns204_ThenGetReturns404()
    {
        var plan = await CreatePlanViaApi("To Delete");

        var deleteResponse = await Client.DeleteAsync($"/api/v1/meal-plans/{plan.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/v1/meal-plans/{plan.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var response = await Client.DeleteAsync($"/api/v1/meal-plans/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /meal-plans/{id}/recipes ─────────────────────────────────────────

    [Fact]
    public async Task AddRecipe_Valid_Returns201()
    {
        var ingId    = await SeedIngredientAsync();
        var recipeId = await SeedRecipeAsync(ingId);
        var plan     = await CreatePlanViaApi("Plan");

        var response = await Client.PostAsJsonAsync($"/api/v1/meal-plans/{plan.Id}/recipes",
            new { recipeId, scheduledDate = (string?)null, portionSize = "REGULAR" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var mpr = await response.Content.ReadFromJsonAsync<MealPlanRecipeResponse>();
        mpr!.RecipeId.Should().Be(recipeId);
        mpr.PortionSize.Should().Be("REGULAR");
    }

    [Fact]
    public async Task AddRecipe_UnknownPlan_Returns404()
    {
        var ingId    = await SeedIngredientAsync();
        var recipeId = await SeedRecipeAsync(ingId);

        var response = await Client.PostAsJsonAsync($"/api/v1/meal-plans/{Guid.NewGuid()}/recipes",
            new { recipeId });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddRecipe_UnknownRecipe_Returns404()
    {
        var plan = await CreatePlanViaApi("Plan");

        var response = await Client.PostAsJsonAsync($"/api/v1/meal-plans/{plan.Id}/recipes",
            new { recipeId = Guid.NewGuid() });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /meal-plans/{id}/recipes/{mprId} ──────────────────────────────────

    [Fact]
    public async Task UpdateRecipe_WrongPlanId_Returns404()
    {
        var ingId    = await SeedIngredientAsync();
        var recipeId = await SeedRecipeAsync(ingId);
        var plan     = await CreatePlanViaApi("Plan");

        var addResponse = await Client.PostAsJsonAsync($"/api/v1/meal-plans/{plan.Id}/recipes",
            new { recipeId });
        var mpr = await addResponse.Content.ReadFromJsonAsync<MealPlanRecipeResponse>();

        var response = await Client.PutAsJsonAsync($"/api/v1/meal-plans/{Guid.NewGuid()}/recipes/{mpr!.Id}",
            new { portionSize = "DOUBLE" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /meal-plans/{id}/recipes/{mprId} ───────────────────────────────

    [Fact]
    public async Task DeleteRecipe_WrongPlanId_Returns404()
    {
        var ingId    = await SeedIngredientAsync();
        var recipeId = await SeedRecipeAsync(ingId);
        var plan     = await CreatePlanViaApi("Plan");

        var addResponse = await Client.PostAsJsonAsync($"/api/v1/meal-plans/{plan.Id}/recipes",
            new { recipeId });
        var mpr = await addResponse.Content.ReadFromJsonAsync<MealPlanRecipeResponse>();

        var response = await Client.DeleteAsync($"/api/v1/meal-plans/{Guid.NewGuid()}/recipes/{mpr!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /meal-plans/{id}/suggestions ──────────────────────────────────────

    [Fact]
    public async Task GetSuggestions_UnknownPlan_Returns404()
    {
        var response = await Client.GetAsync($"/api/v1/meal-plans/{Guid.NewGuid()}/suggestions");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSuggestions_ValidPlan_Returns200List()
    {
        var plan     = await CreatePlanViaApi("Plan");
        var response = await Client.GetAsync($"/api/v1/meal-plans/{plan.Id}/suggestions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<SuggestionResponse>>();
        list.Should().NotBeNull();
    }
}
