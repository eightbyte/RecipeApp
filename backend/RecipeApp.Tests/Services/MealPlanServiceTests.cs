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
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var active = await svc.GetActiveAsync();

        active.Should().NotBeNull();
        active!.Id.Should().Be(plan.Id);
        active.PlannedMeals.Should().HaveCount(1);
        active.PlannedMeals[0].RecipeName.Should().Be("Pasta");
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

    // ── AddPlannedMealAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task AddPlannedMealAsync_IncreasesDisplayOrder()
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

        var plannedMeal1 = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        var plannedMeal2 = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

        plannedMeal1!.DisplayOrder.Should().Be(1);
        plannedMeal2!.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public async Task AddPlannedMealAsync_SameRecipeTwiceIsAllowed()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, "Pasta"));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.PlannedMeals.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddPlannedMealAsync_UnknownPlan_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);

        var result = await svc.AddPlannedMealAsync(Guid.NewGuid(), TestDataBuilder.AddPlannedMealRequest(recipe.Id));
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddPlannedMealAsync_UnknownRecipe_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var svc  = new MealPlanService(ctx);
        var plan = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var result = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(Guid.NewGuid()));
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddPlannedMealAsync_WithoutServings_TakesTheRecipesServings()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, servings: 6));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var plannedMeal = await svc.AddPlannedMealAsync(plan.Id, new API.DTOs.MealPlans.AddPlannedMealRequest(recipe.Id, null, null));
        plannedMeal!.Servings.Should().Be(6);
    }

    [Fact]
    public async Task AddPlannedMealAsync_WithServings_KeepsThem()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id, servings: 6));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        var plannedMeal = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id, servings: 9));
        plannedMeal!.Servings.Should().Be(9);
    }

    // ── UpdatePlannedMealAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePlannedMealAsync_UpdatesDateAndServings()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var plannedMeal    = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var date = new DateOnly(2026, 6, 5);
        var updated = await svc.UpdatePlannedMealAsync(plan.Id, plannedMeal!.Id, new API.DTOs.MealPlans.UpdatePlannedMealRequest(date, 7));

        updated!.ScheduledDate.Should().Be(date);
        updated.Servings.Should().Be(7);
    }

    [Fact]
    public async Task UpdatePlannedMealAsync_WrongPlanId_ReturnsNull()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var plannedMeal    = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var result = await svc.UpdatePlannedMealAsync(Guid.NewGuid(), plannedMeal!.Id,
            new API.DTOs.MealPlans.UpdatePlannedMealRequest(null, 4));

        result.Should().BeNull();
    }

    // ── RemovePlannedMealAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RemovePlannedMealAsync_RemovesFromPlan()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var plannedMeal    = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var removed = await svc.RemovePlannedMealAsync(plan.Id, plannedMeal!.Id);

        removed.Should().BeTrue();
        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.PlannedMeals.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePlannedMealAsync_WrongPlanId_ReturnsFalse()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        var plannedMeal    = await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        var result = await svc.RemovePlannedMealAsync(Guid.NewGuid(), plannedMeal!.Id);
        result.Should().BeFalse();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_CascadesPlannedMeals()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipe = await new RecipeService(ctx).CreateAsync(TestDataBuilder.CreateRecipeRequest(ing.Id));
        var svc    = new MealPlanService(ctx);
        var plan   = await svc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));

        await svc.DeleteAsync(plan.Id);

        ctx.PlannedMeals.Count(plannedMeal => plannedMeal.MealPlanId == plan.Id).Should().Be(0);
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
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        await svc.AddPlannedMealAsync(plan.Id, new API.DTOs.MealPlans.AddPlannedMealRequest(r2.Id, new DateOnly(2026, 6, 5), null));

        var detail = await svc.GetByIdAsync(plan.Id);
        detail!.PlannedMeals[0].RecipeName.Should().Be("Dated");
        detail.PlannedMeals[1].RecipeName.Should().Be("Undated");
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
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));

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
        await svc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(planRecipe.Id));

        var suggestions = await svc.GetSuggestionsAsync(plan.Id);

        suggestions.Should().NotBeNull();
        suggestions![0].OverlapCount.Should().BeGreaterThan(suggestions[1].OverlapCount);
    }
}
