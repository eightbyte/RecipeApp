using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services;

[Collection("Database")]
public class ShoppingListServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(Ingredient ing, API.Models.Recipe recipe, MealPlan plan)> SeedActivePlanWithRecipeAsync(
        decimal amount = 200m, string unit = "g",
        string category = IngredientCategory.Produce)
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient(category: category);
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Test Recipe", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, amount, unit, null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Active Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r.Id));

        var ingredient = await ctx.Ingredients.FindAsync(ing.Id);
        var recipe     = await ctx.Recipes.FindAsync(Guid.Parse(r.Id.ToString()));
        var mealPlan   = await ctx.MealPlans.FindAsync(plan.Id);

        return (ingredient!, recipe!, mealPlan!);
    }

    // ── GetActiveAsync — auto-generate ────────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_NoActivePlan_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        var result = await svc.GetActiveAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_AutoGeneratesWhenNoListExists()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        var result = await svc.GetActiveAsync();

        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        ctx.ShoppingLists.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsExistingListOnSubsequentCall()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);

        var first  = await svc.GetActiveAsync();
        var second = await svc.GetActiveAsync();

        second!.Id.Should().Be(first!.Id);
        ctx.ShoppingLists.Should().HaveCount(1);
    }

    // ── Aggregation — portion multipliers ────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_AppliesHalfMultiplier()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Recipe", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 200m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id,
            new API.DTOs.MealPlans.AddMealPlanRecipeRequest(r.Id, null, PortionSize.Half));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        list!.Items[0].Amount.Should().Be(100m);
    }

    [Fact]
    public async Task GetActiveAsync_AppliesDoubleMultiplier()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Recipe", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 300m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id,
            new API.DTOs.MealPlans.AddMealPlanRecipeRequest(r.Id, null, PortionSize.Double));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        list!.Items[0].Amount.Should().Be(600m);
    }

    // ── Unit consolidation ────────────────────────────────────────────────────

    [Fact]
    public async Task Aggregation_ConsolidatesGramsAndKilograms()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r1 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R1", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 500m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        var r2 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R2", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 1m, "kg", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        // 500g + 1kg = 1500g → presented as 1.5 kg
        var item = list!.Items.Single();
        item.Amount.Should().Be(1.5m);
        item.Unit.Should().Be("kg");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_ConsolidatesMlAndLitres()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r1 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R1", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 750m, "ml", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        var r2 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R2", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 0.5m, "L", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        // 750ml + 500ml = 1250ml → presented as 1.25 L
        var item = list!.Items.Single();
        item.Amount.Should().Be(1.25m);
        item.Unit.Should().Be("L");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_IncompatibleUnits_ProducesSeparateNeedsReviewRows()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r1 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R1", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 2m, "pcs", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        var r2 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R2", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 100m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        list!.Items.Should().HaveCount(2);
        list.Items.Should().AllSatisfy(i => i.NeedsReview.Should().BeTrue());
    }

    [Fact]
    public async Task Aggregation_PcsRowsSummedSeparately()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r1 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R1", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 3m, "pcs", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        var r2 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R2", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 2m, "pcs", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var item = list!.Items.Single();
        item.Amount.Should().Be(5m);
        item.Unit.Should().Be("pcs");
        item.NeedsReview.Should().BeFalse();
    }

    // ── Category ordering ─────────────────────────────────────────────────────

    [Fact]
    public async Task Aggregation_CategoryOrderFollowsIngredientCategoryAll()
    {
        await using var ctx = db.CreateDbContext();
        var dairy   = TestDataBuilder.Ingredient("milk",   "Milk",   IngredientCategory.Dairy);
        var produce = TestDataBuilder.Ingredient("carrot", "Carrot", IngredientCategory.Produce);
        ctx.Ingredients.AddRange(dairy, produce);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Recipe", null, null, 4,
            [
                new API.DTOs.Recipes.RecipeIngredientRequest(dairy.Id,   200m, "ml", null, 0),
                new API.DTOs.Recipes.RecipeIngredientRequest(produce.Id, 100m, "g",  null, 1),
            ],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        // PRODUCE comes before DAIRY in IngredientCategory.All
        list!.Items[0].Category.Should().Be(IngredientCategory.Produce);
        list.Items[1].Category.Should().Be(IngredientCategory.Dairy);
    }

    // ── IsStale detection ─────────────────────────────────────────────────────

    [Fact]
    public async Task IsStale_FalseImmediatelyAfterGeneration()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        list!.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task IsStale_TrueAfterMealPlanRecipeMutation()
    {
        var (ing, _, plan) = await SeedActivePlanWithRecipeAsync();

        // Generate list
        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        await svc.GetActiveAsync();

        // Wait a tick so UpdatedAt > GeneratedAt
        await Task.Delay(10);

        // Add another recipe to bump MealPlan.UpdatedAt
        var recipeSvc = new RecipeService(ctx);
        var r2 = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "R2", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 50m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        await mealSvc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        var refreshed = await svc.GetActiveAsync();
        refreshed!.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task IsStale_CheckingItemDoesNotMakeListStale()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var item = list!.Items[0];
        await svc.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest(true, null, null));

        var refreshed = await svc.GetActiveAsync();
        refreshed!.IsStale.Should().BeFalse();
    }

    // ── RegenerateActiveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateActiveAsync_ReplacesRecipeDerivedItems()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync(amount: 100m);

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var originalItemId = list!.Items[0].Id;

        await svc.RegenerateActiveAsync();
        var regenerated = await svc.GetActiveAsync();

        regenerated!.Items[0].Id.Should().NotBe(originalItemId);
    }

    [Fact]
    public async Task RegenerateActiveAsync_PreservesCustomItems()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("Bread", null, null, null));

        await svc.RegenerateActiveAsync();
        var regenerated = await svc.GetActiveAsync();

        regenerated!.Items.Should().Contain(i => i.IsCustom && i.DisplayName == "Bread");
    }

    [Fact]
    public async Task RegenerateActiveAsync_UpdatesGeneratedAt()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var originalGeneratedAt = list!.GeneratedAt;

        await Task.Delay(10);
        await svc.RegenerateActiveAsync();
        var regenerated = await svc.GetActiveAsync();

        regenerated!.GeneratedAt.Should().BeAfter(originalGeneratedAt);
    }

    // ── AddCustomItemAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AddCustomItemAsync_AddsItemWithIsCustomTrue()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var item = await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("Salt", 1m, "tsp", IngredientCategory.Condiments));

        item.Should().NotBeNull();
        item!.IsCustom.Should().BeTrue();
        item.DisplayName.Should().Be("Salt");
        item.Category.Should().Be(IngredientCategory.Condiments);
    }

    [Fact]
    public async Task AddCustomItemAsync_DefaultsCategoryToOther()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var item = await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("Misc", null, null, null));

        item!.Category.Should().Be(IngredientCategory.Other);
    }

    // ── UpdateItemAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItemAsync_TogglesCheckedState()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var item = list!.Items[0];

        var updated = await svc.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest(true, null, null));

        updated!.IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateItemAsync_WrongList_ReturnsNull()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var result = await svc.UpdateItemAsync(Guid.NewGuid(), list!.Items[0].Id, new UpdateItemRequest(true, null, null));
        result.Should().BeNull();
    }

    // ── DeleteItemAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteItemAsync_CustomItem_ReturnsDeleted()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var custom = await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("Beer", null, null, null));

        var result = await svc.DeleteItemAsync(list.Id, custom!.Id);
        result.Should().Be(DeleteItemResult.Deleted);
    }

    [Fact]
    public async Task DeleteItemAsync_RecipeDerivedItem_ReturnsForbidden()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var recipeItem = list!.Items.First(i => !i.IsCustom);

        var result = await svc.DeleteItemAsync(list.Id, recipeItem.Id);
        result.Should().Be(DeleteItemResult.Forbidden);
    }

    [Fact]
    public async Task DeleteItemAsync_WrongList_ReturnsNotFound()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var custom = await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("X", null, null, null));

        var result = await svc.DeleteItemAsync(Guid.NewGuid(), custom!.Id);
        result.Should().Be(DeleteItemResult.NotFound);
    }

    // ── Cascade delete ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingMealPlan_CascadesShoppingList()
    {
        var (_, _, plan) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        await svc.GetActiveAsync(); // generate list

        var mealSvc = new MealPlanService(ctx);
        await mealSvc.DeleteAsync(plan.Id);

        ctx.ShoppingLists.Should().BeEmpty();
        ctx.ShoppingListItems.Should().BeEmpty();
    }
}
