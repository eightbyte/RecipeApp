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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r.Id));

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

    // ── Aggregation — planned Servings ────────────────────────────────────────

    [Theory]
    [InlineData(4, 200, 6, 300)]
    [InlineData(4, 200, 2, 100)]
    [InlineData(3, 300, 2, 200)]
    [InlineData(4, 200, 4, 200)]
    public async Task GetActiveAsync_ScalesByPlannedServingsOverRecipeServings(
        int recipeServings, int recipeGrams, int plannedServings, int expectedGrams)
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var r = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Recipe", null, null, recipeServings,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, recipeGrams, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r.Id, servings: plannedServings));

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        list!.Items[0].Amount.Should().Be(expectedGrams);
    }

    [Fact]
    public async Task GetActiveAsync_RecipeServingsEditedAfterPlanning_KeepsThePlannedCount()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeId = await CreateRecipeAsync(new RecipeService(ctx), "Stew", ing.Id, (200m, "g"));
        var mealSvc  = new MealPlanService(ctx);
        var plan     = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId, servings: 6));

        var recipe = await ctx.Recipes.FindAsync(recipeId);
        recipe!.Servings = 8;
        await ctx.SaveChangesAsync();

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        list!.Items[0].Amount.Should().Be(150m);
        list.Items[0].Sources.Single().Servings.Should().Be(6);
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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r1.Id));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();

        var item = list!.Items.Single();
        item.Amount.Should().Be(5m);
        item.Unit.Should().Be("pcs");
        item.NeedsReview.Should().BeFalse();
    }

    // ── Aggregation — dimensioned spoons and cups ─────────────────────────────

    /// <summary>Builds an active plan whose recipes each contribute one row for the same ingredient, then returns the generated shopping list.</summary>
    private async Task<ShoppingListResponse> BuildListForAsync(
        decimal? gramsPerMillilitre,
        params (decimal? Amount, string? Unit)[] rows)
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient(gramsPerMillilitre: gramsPerMillilitre);
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var mealSvc   = new MealPlanService(ctx);
        var plan      = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));

        for (var i = 0; i < rows.Length; i++)
        {
            var recipe = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
                $"R{i}", null, null, 4,
                [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, rows[i].Amount, rows[i].Unit, null, 0)],
                [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
            await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipe.Id));
        }

        return (await new ShoppingListService(ctx).GetActiveAsync())!;
    }

    [Fact]
    public async Task Aggregation_ConsolidatesTablespoonsWithMillilitres()
    {
        // Regression: tsp and tbsp once had no dimension, so two physically identical measurements produced two rows and a spurious review flag.
        var list = await BuildListForAsync(null, (1m, "tbsp"), (15m, "ml"));

        var item = list.Items.Single();
        item.Amount.Should().Be(30m);
        item.Unit.Should().Be("ml");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_SumsTeaspoonsInTeaspoons()
    {
        // Every contributing row uses the same unit, so there is nothing to convert.
        var list = await BuildListForAsync(null, (1m, "tbsp"), (2m, "tbsp"));

        var item = list.Items.Single();
        item.Amount.Should().Be(3m);
        item.Unit.Should().Be("tbsp");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_CupPlusGrams_WithDensity_ResolvesToOneMassRow()
    {
        // 1 cup x 240 ml x 0.5 g/ml = 120 g, plus 120 g = 240 g.
        var list = await BuildListForAsync(0.5m, (1m, "cup"), (120m, "g"));

        var item = list.Items.Single();
        item.Amount.Should().Be(240m);
        item.Unit.Should().Be("g");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_TeaspoonPlusGrams_WithDensity_ResolvesToOneMassRow()
    {
        // Density applies to every volume unit here, unlike the import policy, because a shopping total is a quantity to buy.
        var list = await BuildListForAsync(1.2167m, (1m, "tsp"), (10m, "g"));

        var item = list.Items.Single();
        item.Unit.Should().Be("g");
        item.Amount.Should().BeApproximately(16.08m, 0.01m);
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_CupsWithoutDensity_StayCups()
    {
        // No fabricated mass for a cup with no density, so 2 cups plus 1 cup is 3 cups.
        var list = await BuildListForAsync(null, (2m, "cup"), (1m, "cup"));

        var item = list.Items.Single();
        item.Amount.Should().Be(3m);
        item.Unit.Should().Be("cup");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_CupPlusGrams_WithoutDensity_NeedsReview()
    {
        // Genuinely irreconcilable: nothing on the catalogue can relate a cup of this to a gram.
        var list = await BuildListForAsync(null, (1m, "cup"), (100m, "g"));

        list.Items.Should().HaveCount(2);
        list.Items.Should().AllSatisfy(i => i.NeedsReview.Should().BeTrue());
    }

    [Fact]
    public async Task Aggregation_PiecesPlusGrams_StayFlaggedEvenWithDensity()
    {
        // A density cannot help: pcs has no fixed size, so the shopper has to decide.
        var list = await BuildListForAsync(0.5m, (2m, "pcs"), (150m, "g"));

        list.Items.Should().HaveCount(2);
        list.Items.Should().AllSatisfy(i => i.NeedsReview.Should().BeTrue());
    }

    [Fact]
    public async Task Aggregation_MixedVolumeUnitsWithDensity_PromotesToKilograms()
    {
        // 2 cup (480 ml) + 1 L = 1480 ml x 0.8 g/ml = 1184 g, plus 100 g = 1284 g -> 1.284 kg.
        var list = await BuildListForAsync(0.8m, (2m, "cup"), (1m, "L"), (100m, "g"));

        var item = list.Items.Single();
        item.Unit.Should().Be("kg");
        item.Amount.Should().BeApproximately(1.284m, 0.001m);
        item.NeedsReview.Should().BeFalse();
    }

    // ── Aggregation — unquantified ingredients ────────────────────────────────

    [Fact]
    public async Task Aggregation_OnlyEverUnquantified_YieldsOneAmountLessRow()
    {
        // The shopper still has to buy the salt, so it becomes an amount-less row rather than being dropped.
        var list = await BuildListForAsync(null, (null, null), (null, null));

        var item = list.Items.Single();
        item.Amount.Should().BeNull();
        item.Unit.Should().BeNull();
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_QuantifiedAndUnquantified_KeepsTheQuantifiedTotalAlone()
    {
        // The unquantified row must not be summed as a zero, which would claim the plan needs none of it.
        var list = await BuildListForAsync(null, (200m, "g"), (null, null));

        var item = list.Items.Single();
        item.Amount.Should().Be(200m);
        item.Unit.Should().Be("g");
        item.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Aggregation_UnquantifiedRow_DoesNotFlagAnIncompatibleMixForReview()
    {
        // An absent amount is not a third dimension, so the unquantified row neither adds a row nor changes the review flag.
        var list = await BuildListForAsync(null, (150m, "g"), (2m, "pcs"), (null, null));

        list.Items.Should().HaveCount(2);
        list.Items.Should().AllSatisfy(i => i.NeedsReview.Should().BeTrue());
        list.Items.Should().AllSatisfy(i => i.Amount.Should().NotBeNull());
    }

    [Fact]
    public async Task Aggregation_UnquantifiedRow_IsNeverStoredAsZero()
    {
        // A zero anywhere would mean a fabricated quantity got through.
        var list = await BuildListForAsync(null, (null, null));

        list.Items.Should().NotContain(i => i.Amount == 0m);
    }

    // ── Item Sources ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sources_TwoPlannedMealsWithDifferentServings_ListsEachScaled()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var pancakes = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Pancakes", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 200m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        var waffles = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            "Waffles", null, null, 4,
            [new API.DTOs.Recipes.RecipeIngredientRequest(ing.Id, 300m, "g", null, 0)],
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));

        var mealSvc = new MealPlanService(ctx);
        var plan = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddPlannedMealAsync(plan.Id,
            TestDataBuilder.AddPlannedMealRequest(pancakes.Id, servings: 2));
        await mealSvc.AddPlannedMealAsync(plan.Id,
            TestDataBuilder.AddPlannedMealRequest(waffles.Id, servings: 8));

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        var item = list!.Items.Single();
        item.Amount.Should().Be(700m);
        item.Sources.Should().SatisfyRespectively(
            first =>
            {
                first.RecipeId.Should().Be(pancakes.Id);
                first.RecipeName.Should().Be("Pancakes");
                first.Servings.Should().Be(2);
                first.Amount.Should().Be(100m);
                first.Unit.Should().Be("g");
                first.ShareAmount.Should().BeNull();
                first.ShareUnit.Should().BeNull();
            },
            second =>
            {
                second.RecipeId.Should().Be(waffles.Id);
                second.RecipeName.Should().Be("Waffles");
                second.Servings.Should().Be(8);
                second.Amount.Should().Be(600m);
                second.Unit.Should().Be("g");
            });
    }

    [Fact]
    public async Task Sources_TablespoonAndCupInOneVolumeRow_EachCarryItsShareInMillilitres()
    {
        // 2 tbsp (30 ml) + 1 cup (240 ml) = 270 ml, so the shares add up to the row the recipes' own units don't.
        var list = await BuildListForAsync(null, (2m, "tbsp"), (1m, "cup"));

        var item = list.Items.Single();
        item.Amount.Should().Be(270m);
        item.Unit.Should().Be("ml");
        item.Sources.Select(s => (s.RecipeName, s.Amount, s.Unit, s.ShareAmount, s.ShareUnit))
            .Should().Equal(("R0", 2m, "tbsp", 30m, "ml"), ("R1", 1m, "cup", 240m, "ml"));
    }

    [Fact]
    public async Task Sources_VolumeMergedIntoMassByDensity_SharesInGrams()
    {
        // 1 cup x 240 ml x 0.5 g/ml = 120 g, plus 120 g = 240 g.
        var list = await BuildListForAsync(0.5m, (1m, "cup"), (120m, "g"));

        list.Items.Single().Sources.Select(s => (s.Amount, s.Unit, s.ShareAmount, s.ShareUnit))
            .Should().Equal((1m, "cup", 120m, "g"), (120m, "g", null, null));
    }

    [Fact]
    public async Task Sources_RowPromotedToLitres_SharesInMillilitres()
    {
        // 750 ml + 0.5 L = 1.25 L, so the shares stay in the base unit the total was summed in.
        var list = await BuildListForAsync(null, (750m, "ml"), (0.5m, "L"));

        var item = list.Items.Single();
        item.Unit.Should().Be("L");
        item.Sources.Select(s => (s.Amount, s.Unit, s.ShareAmount, s.ShareUnit))
            .Should().Equal((750m, "ml", null, null), (0.5m, "L", 500m, "ml"));
    }

    [Fact]
    public async Task Sources_SplitRows_EachListOnlyTheMealsThatFedThem()
    {
        var list = await BuildListForAsync(null, (2m, "pcs"), (100m, "g"));

        list.Items.Single(i => i.Unit == "pcs").Sources.Should().ContainSingle(s => s.RecipeName == "R0");
        list.Items.Single(i => i.Unit == "g").Sources.Should().ContainSingle(s => s.RecipeName == "R1");
    }

    [Fact]
    public async Task Sources_UnquantifiedMeal_IsListedWithoutAnAmount()
    {
        var list = await BuildListForAsync(null, (200m, "g"), (null, null));

        list.Items.Single().Sources.Select(s => (s.RecipeName, s.Amount, s.Unit, s.ShareAmount, s.ShareUnit))
            .Should().Equal(("R0", 200m, "g", null, null), ("R1", null, null, null, null));
    }

    [Fact]
    public async Task Sources_OnlyEverUnquantified_ListsEveryMealOnTheAmountLessRow()
    {
        var list = await BuildListForAsync(null, (null, null), (null, null));

        list.Items.Single().Sources.Select(s => s.RecipeName).Should().Equal("R0", "R1");
    }

    [Fact]
    public async Task Sources_UnquantifiedMeal_AppearsOnEverySplitRow()
    {
        var list = await BuildListForAsync(null, (150m, "g"), (2m, "pcs"), (null, null));

        list.Items.Should().HaveCount(2);
        list.Items.Should().AllSatisfy(i =>
            i.Sources.Should().ContainSingle(s => s.RecipeName == "R2" && s.Amount == null));
    }

    /// <summary>Creates a recipe of the given lines for one ingredient.</summary>
    private static async Task<Guid> CreateRecipeAsync(
        RecipeService recipeSvc, string name, Guid ingredientId, params (decimal? Amount, string? Unit)[] lines)
    {
        var recipe = await recipeSvc.CreateAsync(new API.DTOs.Recipes.CreateRecipeRequest(
            name, null, null, 4,
            lines.Select((l, i) => new API.DTOs.Recipes.RecipeIngredientRequest(ingredientId, l.Amount, l.Unit, null, i)).ToList(),
            [new API.DTOs.Recipes.RecipeStepRequest(1, "Cook", [])]));
        return recipe.Id;
    }

    [Fact]
    public async Task Sources_FollowTheMealPlan_DatedByDateThenUnscheduledByPlanOrder()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeSvc = new RecipeService(ctx);
        var mealSvc   = new MealPlanService(ctx);
        var plan      = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        foreach (var (name, date) in new (string, DateOnly?)[]
                 {
                     ("Unscheduled first", null),
                     ("Wednesday", new DateOnly(2026, 6, 3)),
                     ("Unscheduled second", null),
                     ("Monday", new DateOnly(2026, 6, 1)),
                 })
        {
            var recipeId = await CreateRecipeAsync(recipeSvc, name, ing.Id, (100m, "g"));
            await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId, date));
        }

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        var sources = list!.Items.Single().Sources;
        sources.Select(s => s.RecipeName)
            .Should().Equal("Monday", "Wednesday", "Unscheduled first", "Unscheduled second");
        sources[0].ScheduledDate.Should().Be(new DateOnly(2026, 6, 1));
        sources[2].ScheduledDate.Should().BeNull();
    }

    [Fact]
    public async Task Sources_SameRecipePlannedTwice_IsTwoSources()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient();
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeId = await CreateRecipeAsync(new RecipeService(ctx), "Pancakes", ing.Id, (100m, "g"));
        var mealSvc  = new MealPlanService(ctx);
        var plan     = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId, servings: 8));

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        list!.Items.Single().Sources.Select(s => (s.RecipeName, s.Amount))
            .Should().Equal(("Pancakes", 100m), ("Pancakes", 200m));
    }

    [Fact]
    public async Task Sources_OneMealUsingTheIngredientTwice_MergesLinesInTheSameUnitOnly()
    {
        await using var ctx = db.CreateDbContext();
        var ing = TestDataBuilder.Ingredient(gramsPerMillilitre: 0.5m);
        ctx.Ingredients.Add(ing);
        await ctx.SaveChangesAsync();

        var recipeId = await CreateRecipeAsync(new RecipeService(ctx), "Cake", ing.Id,
            (2m, "tbsp"), (1m, "tbsp"), (50m, "g"), (null, null));
        var mealSvc = new MealPlanService(ctx);
        var plan    = await mealSvc.CreateAsync(TestDataBuilder.CreateMealPlanRequest("Plan"));
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId));

        var list = await new ShoppingListService(ctx).GetActiveAsync();

        // 3 tbsp = 45 ml x 0.5 g/ml = 22.5 g; the unquantified line adds nothing for a meal already listed.
        list!.Items.Single().Sources.Select(s => (s.Amount, s.Unit, s.ShareAmount, s.ShareUnit))
            .Should().BeEquivalentTo([(3m, "tbsp", 22.5m, "g"), (50m, "g", (decimal?)null, (string?)null)]);
    }

    [Fact]
    public async Task Sources_RemovingAPlannedMeal_LeavesTheListsSourcesIntact()
    {
        var (_, recipe, plan) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        await svc.GetActiveAsync();

        var mealSvc = new MealPlanService(ctx);
        var planned = (await mealSvc.GetByIdAsync(plan.Id))!.PlannedMeals.Single();
        await mealSvc.RemovePlannedMealAsync(plan.Id, planned.Id);

        var list = await svc.GetActiveAsync();
        list!.IsStale.Should().BeTrue();
        list.Items.Single().Sources.Should().ContainSingle(s => s.RecipeId == recipe.Id && s.RecipeName == "Test Recipe");
    }

    [Fact]
    public async Task Sources_RecipeDeletedAfterGeneration_KeepsItsNameWithoutALink()
    {
        var (_, recipe, plan) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        await svc.GetActiveAsync();

        var mealSvc = new MealPlanService(ctx);
        var planned = (await mealSvc.GetByIdAsync(plan.Id))!.PlannedMeals.Single();
        await mealSvc.RemovePlannedMealAsync(plan.Id, planned.Id);
        await new RecipeService(ctx).DeleteAsync(recipe.Id);

        var list = await svc.GetActiveAsync();
        var source = list!.Items.Single().Sources.Single();
        source.RecipeId.Should().BeNull();
        source.RecipeName.Should().Be("Test Recipe");
    }

    [Fact]
    public async Task Sources_RegenerateReplacesThem_AndCustomItemsHaveNone()
    {
        var (ing, _, plan) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        await svc.AddCustomItemAsync(list!.Id, new AddCustomItemRequest("Bread", null, null, null));

        var recipeId = await CreateRecipeAsync(new RecipeService(ctx), "Second", ing.Id, (50m, "g"));
        await new MealPlanService(ctx).AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(recipeId));

        var regenerated = await svc.RegenerateActiveAsync();

        regenerated!.Items.Single(i => !i.IsCustom).Sources.Select(s => s.RecipeName)
            .Should().Equal("Test Recipe", "Second");
        regenerated.Items.Single(i => i.IsCustom).Sources.Should().BeEmpty();
        ctx.ShoppingListItemSources.Should().HaveCount(2);
    }

    [Fact]
    public async Task Sources_EditingAnItemsAmount_LeavesItsSourcesUnchanged()
    {
        var (_, _, _) = await SeedActivePlanWithRecipeAsync(amount: 200m);

        await using var ctx = db.CreateDbContext();
        var svc  = new ShoppingListService(ctx);
        var list = await svc.GetActiveAsync();
        var item = list!.Items.Single();

        var updated = await svc.UpdateItemAsync(list.Id, item.Id, new UpdateItemRequest(true, 500m, "g"));

        updated!.Amount.Should().Be(500m);
        updated.Sources.Should().ContainSingle(s => s.Amount == 200m && s.Unit == "g");
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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r.Id));

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
    public async Task IsStale_TrueAfterPlannedMealMutation()
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
        await mealSvc.AddPlannedMealAsync(plan.Id, TestDataBuilder.AddPlannedMealRequest(r2.Id));

        var refreshed = await svc.GetActiveAsync();
        refreshed!.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task IsStale_TrueAfterPlannedServingsChange()
    {
        var (_, _, plan) = await SeedActivePlanWithRecipeAsync();

        await using var ctx = db.CreateDbContext();
        var svc = new ShoppingListService(ctx);
        await svc.GetActiveAsync();
        await Task.Delay(10);

        var mealSvc     = new MealPlanService(ctx);
        var plannedMeal = (await mealSvc.GetByIdAsync(plan.Id))!.PlannedMeals.Single();
        await mealSvc.UpdatePlannedMealAsync(plan.Id, plannedMeal.Id,
            new API.DTOs.MealPlans.UpdatePlannedMealRequest(plannedMeal.ScheduledDate, plannedMeal.Servings + 1));

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
