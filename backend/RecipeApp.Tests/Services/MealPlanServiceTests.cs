using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services;

[Collection("Database")]
public class MealPlanServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DeactivatesPreviousActivePlan()
    {
        await using var ctx = db.CreateDbContext();
        var existing = TestDataBuilder.MealPlan("Old Plan", isActive: true);
        ctx.MealPlans.Add(existing);
        await ctx.SaveChangesAsync();

        var svc = new MealPlanService(ctx);
        await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("New Plan"));

        var oldPlan = await ctx.MealPlans.FindAsync(existing.Id);
        oldPlan!.IsActive.Should().BeFalse();
        oldPlan.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_NewPlanIsActive()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);
        var result = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("My Plan"));

        result.IsActive.Should().BeTrue();
        result.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_AtMostOneActivePlanExists()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);

        await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan A"));
        await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan B"));
        await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan C"));

        var activePlans = ctx.MealPlans.Count(p => p.IsActive);
        activePlans.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_SetsUtcCreatedAt()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);
        var before = DateTime.UtcNow;
        var result = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var after = DateTime.UtcNow;

        result.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── GetActiveAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_ReturnsActivePlanWithRecipes()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var svc    = new MealPlanService(ctx);
        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Pasta"));
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Active Plan"));
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var active = await svc.GetActiveAsync();

        active.Should().NotBeNull();
        active!.Id.Should().Be(plan.Id);
        active.Recipes.Should().HaveCount(1);
        active.Recipes[0].RecipeName.Should().Be("Pasta");
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsNullWhenNoActivePlan()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);
        var result = await svc.GetActiveAsync();
        result.Should().BeNull();
    }

    // ── GetListAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_ActivePlanIsFirst()
    {
        await using var ctx = db.CreateDbContext();
        var inactive = TestDataBuilder.MealPlan("Old", isActive: false, createdAt: DateTime.UtcNow.AddDays(-1));
        ctx.MealPlans.Add(inactive);
        await ctx.SaveChangesAsync();

        var svc = new MealPlanService(ctx);
        await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Active"));

        var list = await svc.GetListAsync();
        list[0].IsActive.Should().BeTrue();
    }

    // ── AddRecipeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRecipeAsync_IncreasesDisplayOrder()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeService = new RecipeService(ctx);
        var r1 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "R1"));
        var r2 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "R2"));

        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var mpr1 = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        var mpr2 = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r2.Id));

        mpr1!.DisplayOrder.Should().Be(1);
        mpr2!.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public async Task AddRecipeAsync_SameRecipeTwiceIsAllowed()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Pasta"));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.Recipes.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddRecipeAsync_UnknownPlan_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);

        var result = await svc.AddRecipeAsync(Guid.NewGuid(), TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddRecipeAsync_UnknownRecipe_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var result = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(Guid.NewGuid()));
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddRecipeAsync_DefaultsToRegularPortion()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var mpr = await svc.AddRecipeAsync(plan.Id, new API.DTOs.MealPlans.AddMealPlanRecipeRequest(recipe.Id, null, null));
        mpr!.PortionSize.Should().Be(PortionSize.Regular);
    }

    // ── UpdateRecipeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecipeAsync_UpdatesDateAndPortion()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var mpr    = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var date = new DateOnly(2026, 6, 5);
        var updated = await svc.UpdateRecipeAsync(plan.Id, mpr!.Id, new API.DTOs.MealPlans.UpdateMealPlanRecipeRequest(date, PortionSize.Double));

        updated!.ScheduledDate.Should().Be(date);
        updated.PortionSize.Should().Be(PortionSize.Double);
    }

    [Fact]
    public async Task UpdateRecipeAsync_WrongPlanId_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var mpr    = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var result = await svc.UpdateRecipeAsync(Guid.NewGuid(), mpr!.Id,
            new API.DTOs.MealPlans.UpdateMealPlanRecipeRequest(null, PortionSize.Regular));

        result.Should().BeNull();
    }

    // ── RemoveRecipeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRecipeAsync_RemovesFromPlan()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var mpr    = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var removed = await svc.RemoveRecipeAsync(plan.Id, mpr!.Id);

        removed.Should().BeTrue();
        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.Recipes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveRecipeAsync_WrongPlanId_ReturnsFalse()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var mpr    = await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        var result = await svc.RemoveRecipeAsync(Guid.NewGuid(), mpr!.Id);
        result.Should().BeFalse();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_CascadesMealPlanRecipes()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(recipe.Id));

        await svc.DeleteAsync(plan.Id);

        ctx.MealPlanRecipes.Count(mpr => mpr.MealPlanId == plan.Id).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFalse()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);
        var result = await svc.DeleteAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    // ── Detail sort order ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DatedRecipesBeforeUndated()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeService = new RecipeService(ctx);
        var r1 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Undated"));
        var r2 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Dated"));

        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        // Add undated first, dated second
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));
        await svc.AddRecipeAsync(plan.Id, new API.DTOs.MealPlans.AddMealPlanRecipeRequest(r2.Id, new DateOnly(2026, 6, 5), null));

        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.Recipes[0].RecipeName.Should().Be("Dated");
        detail.Recipes[1].RecipeName.Should().Be("Undated");
    }

    // ── GetSuggestionsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestionsAsync_UnknownPlan_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc = new MealPlanService(ctx);
        var result = await svc.GetSuggestionsAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSuggestionsAsync_EmptyPlan_ReturnsEmptyList()
    {
        await using var ctx = db.CreateDbContext();
        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var result = await svc.GetSuggestionsAsync(plan.Id);
        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetSuggestionsAsync_ExcludesRecipesAlreadyInPlan()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient("onion", "Onion");
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeService = new RecipeService(ctx);
        var r1 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Soup"));
        var r2 = await recipeService.CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Stew"));

        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(r1.Id));

        var suggestions = await svc.GetSuggestionsAsync(plan.Id);

        suggestions.Should().NotBeNull();
        suggestions!.Should().NotContain(s => s.RecipeId == r1.Id);
        suggestions.Should().Contain(s => s.RecipeId == r2.Id);
    }

    [Fact]
    public async Task GetSuggestionsAsync_RankedByOverlapCount()
    {
        await using var ctx = db.CreateDbContext();
        var ing1 = TestDataBuilder.Ingredient("onion", "Onion");
        var ing2 = TestDataBuilder.Ingredient("carrot", "Carrot");
        ctx.Ingredients.AddRange(ing1, ing2);
        await ctx.SaveChangesAsync();

        var recipeService = new RecipeService(ctx);

        // Plan recipe: uses both onion and carrot
        var planRecipe = await recipeService.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Base", null, null, 4,
            [
                new API.DTOs.Recipes.RecipeIngredientRequest(ing1.Id, 100m, "g", null, 0),
                new API.DTOs.Recipes.RecipeIngredientRequest(ing2.Id, 50m, "g", null, 1),
            ],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        // Candidate 1: uses both onion and carrot (overlap = 2)
        var candidate2 = await recipeService.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Double Overlap", null, null, 4,
            [
                new API.DTOs.Recipes.RecipeIngredientRequest(ing1.Id, 100m, "g", null, 0),
                new API.DTOs.Recipes.RecipeIngredientRequest(ing2.Id, 50m, "g", null, 1),
            ],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        // Candidate 2: uses only onion (overlap = 1)
        var candidate1 = await recipeService.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Single Overlap", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing1.Id, 100m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await svc.AddRecipeAsync(plan.Id, TestDataBuilder.AddMealPlanRecipeRequest(planRecipe.Id));

        var suggestions = await svc.GetSuggestionsAsync(plan.Id);

        suggestions.Should().NotBeNull();
        suggestions![0].OverlapCount.Should().BeGreaterThan(suggestions[1].OverlapCount);
    }
}
