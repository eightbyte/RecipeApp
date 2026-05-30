using FluentAssertions;
using RecipeApp.API.Services;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services;

[Collection("Database")]
public class RecipeServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── GetListAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_ReturnsAllRecipes()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Ingredients.Add(TestDataBuilder.Ingredient());
        var r1 = TestDataBuilder.Recipe("Pasta");
        var r2 = TestDataBuilder.Recipe("Soup");
        var r3 = TestDataBuilder.Recipe("Salad");
        ctx.Recipes.AddRange(r1, r2, r3);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync(null, null, null, null);

        list.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetListAsync_SearchByName_CaseInsensitive()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Recipes.Add(TestDataBuilder.Recipe("Spaghetti Bolognese"));
        ctx.Recipes.Add(TestDataBuilder.Recipe("Tomato Soup"));
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync("spag", null, null, null);

        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Spaghetti Bolognese");
    }

    [Fact]
    public async Task GetListAsync_SearchNoMatch_ReturnsEmpty()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Recipes.Add(TestDataBuilder.Recipe("Pasta"));
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync("xyz-no-match", null, null, null);

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_ExcludeRecentDays_ExcludesRecentlyCookedRecipe()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe(lastCookedAt: DateTime.UtcNow.AddDays(-3));
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync(null, null, 7, null);

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_ExcludeRecentDays_IncludesRecipeCookedOutsideWindow()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Recipes.Add(TestDataBuilder.Recipe(lastCookedAt: DateTime.UtcNow.AddDays(-8)));
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync(null, null, 7, null);

        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetListAsync_FilterByLastCookedBefore_ReturnsMatchingRecipe()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Recipes.Add(TestDataBuilder.Recipe(lastCookedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync(null, null, null,
            lastCookedBefore: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetListAsync_NeverCookedWithExcludeWindow_IsReturned()
    {
        await using var ctx = db.CreateDbContext();
        ctx.Recipes.Add(TestDataBuilder.Recipe(lastCookedAt: null));
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var list = await svc.GetListAsync(null, null, 7, null);

        list.Should().HaveCount(1);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingRecipe_ReturnsFullDetail()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);

        var recipe = TestDataBuilder.Recipe("Full Recipe");
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        var ri = new API.Models.RecipeIngredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            IngredientId = ing.Id,
            Amount = 100m,
            Unit = "g",
            DisplayOrder = 0,
        };
        ctx.RecipeIngredients.Add(ri);
        ctx.RecipeSteps.Add(new API.Models.RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            StepNumber = 1,
            Instruction = "Mix everything",
        });
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var detail = await svc.GetByIdAsync(recipe.Id);

        detail.Should().NotBeNull();
        detail!.Ingredients.Should().HaveCount(1);
        detail.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_IngredientsOrderedByDisplayOrder()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        var recipe = TestDataBuilder.Recipe();
        ctx.Recipes.Add(recipe);
        ctx.RecipeIngredients.AddRange(
            new API.Models.RecipeIngredient { Id = Guid.NewGuid(), RecipeId = recipe.Id, IngredientId = ing.Id, Amount = 1m, Unit = "g", DisplayOrder = 3 },
            new API.Models.RecipeIngredient { Id = Guid.NewGuid(), RecipeId = recipe.Id, IngredientId = ing.Id, Amount = 1m, Unit = "g", DisplayOrder = 1 },
            new API.Models.RecipeIngredient { Id = Guid.NewGuid(), RecipeId = recipe.Id, IngredientId = ing.Id, Amount = 1m, Unit = "g", DisplayOrder = 2 }
        );
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var detail = await svc.GetByIdAsync(recipe.Id);

        detail!.Ingredients.Select(i => i.DisplayOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetByIdAsync_StepsOrderedByStepNumber()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe();
        ctx.Recipes.Add(recipe);
        ctx.RecipeSteps.AddRange(
            new API.Models.RecipeStep { Id = Guid.NewGuid(), RecipeId = recipe.Id, StepNumber = 2, Instruction = "B" },
            new API.Models.RecipeStep { Id = Guid.NewGuid(), RecipeId = recipe.Id, StepNumber = 1, Instruction = "A" }
        );
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var detail = await svc.GetByIdAsync(recipe.Id);

        detail!.Steps.Select(s => s.StepNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new RecipeService(ctx);
        var result = await svc.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsPersistableDetail()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var request = TestDataBuilder.CreateRecipeRequest(ing.Id, name: "New Recipe", ingredientCount: 3, stepCount: 2);
        var result = await svc.CreateAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("New Recipe");
        result.Ingredients.Should().HaveCount(3);
        result.Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_SetsUtcTimestamps()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var svc = new RecipeService(ctx);
        var result = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var after = DateTime.UtcNow;

        result.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.UpdatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task CreateAsync_StepIngredientLink_IsCreated()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var request = new API.DTOs.Recipes.CreateRecipeRequest(
            "Test", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 100m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Step 1", [0])]);

        var svc = new RecipeService(ctx);
        var result = await svc.CreateAsync(request);

        result.Steps[0].RecipeIngredientIds.Should().HaveCount(1);
        result.Steps[0].RecipeIngredientIds[0].Should().Be(result.Ingredients[0].Id);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ReplacesIngredients()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var created = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, ingredientCount: 2));

        var updateRequest = TestDataBuilder.UpdateRecipeRequest(ing.Id, ingredientCount: 1);
        var updated = await svc.UpdateAsync(created.Id, updateRequest);

        updated!.Ingredients.Should().HaveCount(1);
        ctx.RecipeIngredients.Count(ri => ri.RecipeId == created.Id).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesSteps()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var created = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, stepCount: 3));

        var updateRequest = TestDataBuilder.UpdateRecipeRequest(ing.Id, stepCount: 2);
        await svc.UpdateAsync(created.Id, updateRequest);

        ctx.RecipeSteps.Count(rs => rs.RecipeId == created.Id).Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_RefreshesUpdatedAt()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var created = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var originalUpdatedAt = created.UpdatedAt;

        await Task.Delay(10); // ensure timestamp difference
        await svc.UpdateAsync(created.Id, TestDataBuilder.UpdateRecipeRequest(ing.Id));

        var raw = await ctx.Recipes.FindAsync(created.Id);
        raw!.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentId_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var result = await svc.UpdateAsync(Guid.NewGuid(), TestDataBuilder.UpdateRecipeRequest(ing.Id));

        result.Should().BeNull();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingRecipe_RemovesFromDb()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe();
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var deleted = await svc.DeleteAsync(recipe.Id);

        deleted.Should().BeTrue();
        ctx.Recipes.Find(recipe.Id).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_CascadesToIngredients()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var created = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, ingredientCount: 2));
        await svc.DeleteAsync(created.Id);

        ctx.RecipeIngredients.Count(ri => ri.RecipeId == created.Id).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_CascadesToSteps()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc = new RecipeService(ctx);
        var created = await svc.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, stepCount: 2));
        await svc.DeleteAsync(created.Id);

        ctx.RecipeSteps.Count(rs => rs.RecipeId == created.Id).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFalse()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new RecipeService(ctx);
        var result = await svc.DeleteAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    // ── MarkCookedAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task MarkCookedAsync_SetsLastCookedAt()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe(lastCookedAt: null);
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var svc = new RecipeService(ctx);
        var result = await svc.MarkCookedAsync(recipe.Id);
        var after = DateTime.UtcNow;

        result!.LastCookedAt.Should().NotBeNull();
        result.LastCookedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task MarkCookedAsync_RefreshesUpdatedAt()
    {
        await using var ctx = db.CreateDbContext();
        var recipe = TestDataBuilder.Recipe();
        ctx.Recipes.Add(recipe);
        await ctx.SaveChangesAsync();

        await Task.Delay(10);
        var svc = new RecipeService(ctx);
        await svc.MarkCookedAsync(recipe.Id);

        var raw = await ctx.Recipes.FindAsync(recipe.Id);
        raw!.UpdatedAt.Should().BeAfter(recipe.UpdatedAt);
    }

    [Fact]
    public async Task MarkCookedAsync_NonExistentId_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new RecipeService(ctx);
        var result = await svc.MarkCookedAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
