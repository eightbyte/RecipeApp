using FluentAssertions;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.DTOs.Recipes;
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
    public async Task ListAll_IncludesAliases()
    {
        await using (var ctx = db.CreateDbContext())
        {
            var flour = TestDataBuilder.Ingredient("all-purpose flour", "All-Purpose Flour", IngredientCategory.BakingSpices);
            flour.Aliases = ["flour", "plain flour"];
            ctx.Ingredients.Add(flour);
            await ctx.SaveChangesAsync();
        }

        var list = await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients");

        list!.Single().Aliases.Should().Equal("flour", "plain flour");
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
    public async Task GetCategories_Returns200WithEveryCategoryInAisleOrder()
    {
        var response = await Client.GetAsync("/api/v1/ingredients/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Order, not just membership: the list is the shopping list's aisle order.
        var categories = await response.Content.ReadFromJsonAsync<List<string>>();
        categories.Should().Equal(IngredientCategory.All);
    }

    [Fact]
    public async Task Create_Valid_Returns201WithBody()
    {
        var request = new CreateIngredientRequest(
            " Caster Sugar ", IngredientCategory.BakingSpices, "g", 0.85m, IsStaple: true, Aliases: ["Superfine Sugar"]);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        body!.Name.Should().Be("caster sugar");
        body.DisplayName.Should().Be("Caster Sugar");
        body.Aliases.Should().Equal("superfine sugar");
        body.GramsPerMillilitre.Should().Be(0.85m);
        body.IsStaple.Should().BeTrue();
    }

    [Theory]
    [InlineData("FLOUR", new string[0])]
    [InlineData("Plain Flour", new string[0])]
    [InlineData("Bread Flour", new[] { "flour" })]
    [InlineData("Bread Flour", new[] { "plain flour" })]
    public async Task Create_NameOrAliasTakenByAnotherIngredient_Returns409NamingIt(string displayName, string[] aliases)
    {
        await SeedWithAliases("flour", "Flour", "plain flour");

        var request = new CreateIngredientRequest(displayName, IngredientCategory.DryGoods, null, Aliases: aliases);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ConflictBody>())!.Message.Should().Contain("Flour");
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400()
    {
        var request = new CreateIngredientRequest("", IngredientCategory.DryGoods, null);
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients", request);

        // Results.ValidationProblem emits RFC 7807 ValidationProblemDetails with status 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    public async Task Update_Rename_TakesTheNameFromTheDisplayNameAndSavesTheAliasesNormalised()
    {
        var seeded = await SeedIngredient("plain flour", "Plain Flour");
        var request = new UpdateIngredientRequest(
            " All-Purpose Flour ", IngredientCategory.DryGoods, "g", Aliases: ["plain flour", " Flour ", "flour"]);

        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        body!.Name.Should().Be("all-purpose flour");
        body.DisplayName.Should().Be("All-Purpose Flour");
        body.Aliases.Should().Equal("flour", "plain flour");
    }

    [Fact]
    public async Task Update_WithoutAliases_KeepsTheExistingAliases()
    {
        await using (var ctx = db.CreateDbContext())
        {
            var onion = TestDataBuilder.Ingredient("onion", "Onion", IngredientCategory.Produce);
            onion.Aliases = ["onions"];
            ctx.Ingredients.Add(onion);
            await ctx.SaveChangesAsync();
        }
        var seeded = (await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients"))!.Single();

        var request = new UpdateIngredientRequest("Onion", IngredientCategory.Produce, null);
        var body = await (await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", request))
            .Content.ReadFromJsonAsync<IngredientResponse>();

        body!.Aliases.Should().Equal("onions");
    }

    private async Task<Guid> SeedWithAliases(string name, string displayName, params string[] aliases)
    {
        await using var ctx = db.CreateDbContext();
        var ingredient = TestDataBuilder.Ingredient(name, displayName);
        ingredient.Aliases = aliases;
        ctx.Ingredients.Add(ingredient);
        await ctx.SaveChangesAsync();
        return ingredient.Id;
    }

    private record ConflictBody(string Message);

    [Theory]
    // A new name that is another Ingredient's name, or its alias.
    [InlineData("Spring Onion", new string[0])]
    [InlineData("Scallion", new string[0])]
    // A new alias that is another Ingredient's name, or its alias.
    [InlineData("Shallot", new[] { "spring onion" })]
    [InlineData("Shallot", new[] { "Scallion" })]
    public async Task Update_NameOrAliasTakenByAnotherIngredient_Returns409NamingIt(
        string displayName, string[] aliases)
    {
        await SeedWithAliases("spring onion", "Spring Onion", "scallion");
        var shallot = await SeedWithAliases("shallot", "Shallot");

        var request = new UpdateIngredientRequest(displayName, IngredientCategory.Produce, null, Aliases: aliases);
        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{shallot}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ConflictBody>())!.Message.Should().Contain("Spring Onion");

        var unchanged = await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{shallot}");
        unchanged!.Name.Should().Be("shallot");
        unchanged.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_AClashTheIngredientAlreadyHad_DoesNotBlockTheSave()
    {
        // A legacy row named like the curated entry's alias, which predates this rule.
        await SeedWithAliases("onions", "Onions");
        var onion = await SeedWithAliases("onion", "Onion", "onions");

        var request = new UpdateIngredientRequest("Onion", IngredientCategory.Produce, null, Aliases: ["onions", "brown onion"]);
        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{onion}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_KeepingItsOwnNameAndAliases_IsNotAClash()
    {
        var onion = await SeedWithAliases("onion", "Onion", "onions");

        var request = new UpdateIngredientRequest("ONION", IngredientCategory.Produce, null, Aliases: ["onions"]);
        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{onion}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_SetsAndClearsTheStapleFlag()
    {
        var seeded = await SeedIngredient("salt", "Salt", IngredientCategory.BakingSpices);
        seeded.IsStaple.Should().BeFalse();

        var makeStaple = new UpdateIngredientRequest("Salt", IngredientCategory.BakingSpices, null, IsStaple: true);
        var madeStaple = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", makeStaple);
        (await madeStaple.Content.ReadFromJsonAsync<IngredientResponse>())!.IsStaple.Should().BeTrue();

        var fetched = await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{seeded.Id}");
        fetched!.IsStaple.Should().BeTrue();

        var clearStaple = new UpdateIngredientRequest("Salt", IngredientCategory.BakingSpices, null, IsStaple: false);
        var clearedStaple = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", clearStaple);
        (await clearedStaple.Content.ReadFromJsonAsync<IngredientResponse>())!.IsStaple.Should().BeFalse();
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var request = new UpdateIngredientRequest("Anything", IngredientCategory.Other, null);
        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{Guid.NewGuid()}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_InvalidBody_Returns400()
    {
        var seeded = await SeedIngredient();
        var request = new UpdateIngredientRequest("", IngredientCategory.DryGoods, null);

        var response = await Client.PutAsJsonAsync($"/api/v1/ingredients/{seeded.Id}", request);
        // Results.ValidationProblem emits RFC 7807 ValidationProblemDetails with status 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Hand-set Density ──────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ClearingADensity_SurvivesTheStartupDensitySeed()
    {
        var flour = await SeedWithAliases("plain flour", "Plain Flour");
        await using (var ctx = db.CreateDbContext())
            (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(1);

        var cleared = new UpdateIngredientRequest("Plain Flour", IngredientCategory.BakingSpices, "g", GramsPerMillilitre: null);
        var body = await (await Client.PutAsJsonAsync($"/api/v1/ingredients/{flour}", cleared))
            .Content.ReadFromJsonAsync<IngredientResponse>();
        body!.HasHandSetDensity.Should().BeTrue();

        // The seed runs at every startup, so this is the restart.
        await using (var ctx = db.CreateDbContext())
            (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(0);

        var fetched = await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{flour}");
        fetched!.GramsPerMillilitre.Should().BeNull();
    }

    [Fact]
    public async Task Create_WithADensity_MarksItHandSet()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients",
            new CreateIngredientRequest("Rice", IngredientCategory.GrainsRice, "g", 0.8m));

        (await response.Content.ReadFromJsonAsync<IngredientResponse>())!.HasHandSetDensity.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithoutADensity_LeavesItForTheCuratedSeed()
    {
        // The recipe form's quick add sends no density, which is no decision about one.
        var response = await Client.PostAsJsonAsync("/api/v1/ingredients",
            new CreateIngredientRequest("Rice", IngredientCategory.GrainsRice, "g"));
        var created = await response.Content.ReadFromJsonAsync<IngredientResponse>();
        created!.HasHandSetDensity.Should().BeFalse();

        await using (var ctx = db.CreateDbContext())
            (await IngredientDensitySeeder.SeedAsync(ctx)).Should().Be(1);
    }

    // ── Removing and restoring ────────────────────────────────────────────────

    private Task<HttpResponseMessage> Remove(Guid id) => Client.DeleteAsync($"/api/v1/ingredients/{id}");

    private Task<HttpResponseMessage> Restore(Guid id) => Client.PostAsync($"/api/v1/ingredients/{id}/restore", null);

    private async Task<Guid> CreateRecipeUsing(Guid ingredientId, int rows = 1)
    {
        var request  = TestDataBuilder.CreateRecipeRequest(ingredientId, ingredientCount: rows);
        var response = await Client.PostAsJsonAsync("/api/v1/recipes", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecipeDetailResponse>())!.Id;
    }

    [Fact]
    public async Task Remove_HidesItFromTheListUnlessRemovedOnesAreAskedFor()
    {
        var onion = await SeedWithAliases("onion", "Onion");
        await SeedWithAliases("garlic", "Garlic");

        (await Remove(onion)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var active = await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients");
        active!.Select(i => i.Name).Should().Equal("garlic");

        var searched = await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients?search=oni");
        searched.Should().BeEmpty();

        var all = await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients?includeRemoved=true");
        all!.Select(i => i.Name).Should().Equal("garlic", "onion");
        all!.Single(i => i.Name == "onion").RemovedAt.Should().NotBeNull();
        all!.Single(i => i.Name == "garlic").RemovedAt.Should().BeNull();

        var fetched = await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{onion}");
        fetched!.RemovedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_KeepsTheIngredientInRecipesThatUseIt()
    {
        var onion  = await SeedWithAliases("onion", "Onion");
        var recipe = await CreateRecipeUsing(onion);

        await Remove(onion);

        var detail = await Client.GetFromJsonAsync<RecipeDetailResponse>($"/api/v1/recipes/{recipe}");
        detail!.Ingredients.Single().IngredientDisplayName.Should().Be("Onion");
    }

    [Fact]
    public async Task Remove_ReleasesItsNameAndAliasesForANewIngredient()
    {
        var onion = await SeedWithAliases("onion", "Onion", "brown onion");
        await Remove(onion);

        var sameName = await Client.PostAsJsonAsync("/api/v1/ingredients",
            new CreateIngredientRequest("Onion", IngredientCategory.Produce, null));
        sameName.StatusCode.Should().Be(HttpStatusCode.Created);

        var sameAlias = await Client.PostAsJsonAsync("/api/v1/ingredients",
            new CreateIngredientRequest("Brown Onion", IngredientCategory.Produce, null));
        sameAlias.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Restore_MakesItActiveAgain()
    {
        var onion = await SeedWithAliases("onion", "Onion");
        await Remove(onion);

        var response = await Restore(onion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<IngredientResponse>())!.RemovedAt.Should().BeNull();
        var active = await Client.GetFromJsonAsync<List<IngredientResponse>>("/api/v1/ingredients");
        active!.Select(i => i.Name).Should().Equal("onion");
    }

    [Theory]
    // Its name, or one of its aliases, taken by an Ingredient added since.
    [InlineData("Onion", new string[0])]
    [InlineData("Shallot", new[] { "onion" })]
    [InlineData("Red Onion", new string[0])]
    public async Task Restore_ANameOrAliasTakenSince_Returns409NamingTheIngredientThatTookIt(
        string newDisplayName, string[] newAliases)
    {
        var onion = await SeedWithAliases("onion", "Onion", "red onion");
        await Remove(onion);
        var taken = await Client.PostAsJsonAsync("/api/v1/ingredients",
            new CreateIngredientRequest(newDisplayName, IngredientCategory.Produce, null, Aliases: newAliases));
        taken.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Restore(onion);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ConflictBody>())!.Message.Should().Contain(newDisplayName);
        (await Client.GetFromJsonAsync<IngredientResponse>($"/api/v1/ingredients/{onion}"))!
            .RemovedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveAndRestore_UnknownIngredient_Return404()
    {
        (await Remove(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Restore(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Usage_CountsTheRecipesThatUseIt()
    {
        var onion = await SeedWithAliases("onion", "Onion");
        await CreateRecipeUsing(onion, rows: 2);
        await CreateRecipeUsing(onion);
        await SeedWithAliases("garlic", "Garlic");

        var usage = await Client.GetFromJsonAsync<IngredientUsageResponse>($"/api/v1/ingredients/{onion}/usage");

        usage!.RecipeCount.Should().Be(2);
    }

    [Fact]
    public async Task Usage_UnknownIngredient_Returns404()
    {
        var response = await Client.GetAsync($"/api/v1/ingredients/{Guid.NewGuid()}/usage");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
