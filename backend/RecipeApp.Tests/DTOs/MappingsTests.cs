using FluentAssertions;
using RecipeApp.API.DTOs;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.Tests.DTOs;

// Meal plan mapping helpers
file static class MealPlanTestHelpers
{
    public static MealPlan MakeMealPlan(string name = "Test Plan", bool isActive = true) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        IsActive  = isActive,
        CreatedAt = DateTime.UtcNow,
        Recipes   = [],
    };

    public static Recipe MakeRecipe(string name = "Test Recipe") => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Servings  = 4,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Ingredients = [],
        Steps       = [],
    };

    public static MealPlanRecipe MakeMealPlanRecipe(MealPlan plan, Recipe recipe, DateOnly? scheduledDate = null, int displayOrder = 1) => new()
    {
        Id            = Guid.NewGuid(),
        MealPlanId    = plan.Id,
        MealPlan      = plan,
        RecipeId      = recipe.Id,
        Recipe        = recipe,
        ScheduledDate = scheduledDate,
        PortionSize   = PortionSize.Regular,
        DisplayOrder  = displayOrder,
    };
}

public class MappingsTests
{
    private static Ingredient MakeIngredient(string name = "flour", string displayName = "Flour") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayName = displayName,
        Category = IngredientCategory.DryGoods,
        CreatedAt = DateTime.UtcNow,
    };

    private static RecipeIngredient MakeRecipeIngredient(Ingredient ingredient, int displayOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        IngredientId = ingredient.Id,
        Ingredient = ingredient,
        Amount = 100m,
        Unit = "g",
        DisplayOrder = displayOrder,
        StepIngredients = [],
    };

    // ── Ingredient.ToResponse ──────────────────────────────────────────────────

    [Fact]
    public void Ingredient_ToResponse_MapsAllFields()
    {
        var ing = MakeIngredient();
        var result = ing.ToResponse();

        result.Id.Should().Be(ing.Id);
        result.Name.Should().Be(ing.Name);
        result.DisplayName.Should().Be(ing.DisplayName);
        result.Category.Should().Be(ing.Category);
        result.DefaultUnit.Should().Be(ing.DefaultUnit);
        result.CreatedAt.Should().Be(ing.CreatedAt);
    }

    // ── Recipe.ToListItem ──────────────────────────────────────────────────────

    [Fact]
    public void Recipe_ToListItem_MapsAllScalarFields()
    {
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Name = "Pasta",
            Description = "Simple pasta",
            Servings = 4,
            Ingredients = [MakeRecipeIngredient(MakeIngredient()), MakeRecipeIngredient(MakeIngredient("salt", "Salt"))],
            CreatedAt = DateTime.UtcNow,
        };

        var result = recipe.ToListItem();

        result.Id.Should().Be(recipe.Id);
        result.Name.Should().Be(recipe.Name);
        result.Description.Should().Be(recipe.Description);
        result.Servings.Should().Be(recipe.Servings);
        result.IngredientCount.Should().Be(2);
    }

    [Fact]
    public void Recipe_ToListItem_NoIngredients_IngredientCountZero()
    {
        var recipe = new Recipe { Id = Guid.NewGuid(), Name = "Empty", Servings = 2, Ingredients = [] };
        recipe.ToListItem().IngredientCount.Should().Be(0);
    }

    [Fact]
    public void Recipe_ToListItem_NullLastCookedAt_PassesThroughNull()
    {
        var recipe = new Recipe { Id = Guid.NewGuid(), Name = "X", Servings = 2, LastCookedAt = null, Ingredients = [] };
        recipe.ToListItem().LastCookedAt.Should().BeNull();
    }

    // ── Recipe.ToDetail ────────────────────────────────────────────────────────

    [Fact]
    public void Recipe_ToDetail_IngredientsOrderedByDisplayOrder()
    {
        var ing = MakeIngredient();
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Servings = 4,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Ingredients = [
                MakeRecipeIngredient(ing, displayOrder: 3),
                MakeRecipeIngredient(ing, displayOrder: 1),
                MakeRecipeIngredient(ing, displayOrder: 2),
            ],
            Steps = [],
        };

        var detail = recipe.ToDetail();

        detail.Ingredients[0].DisplayOrder.Should().Be(1);
        detail.Ingredients[1].DisplayOrder.Should().Be(2);
        detail.Ingredients[2].DisplayOrder.Should().Be(3);
    }

    [Fact]
    public void Recipe_ToDetail_StepsOrderedByStepNumber()
    {
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Servings = 4,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Ingredients = [],
            Steps = [
                new RecipeStep { Id = Guid.NewGuid(), StepNumber = 2, Instruction = "Second", StepIngredients = [] },
                new RecipeStep { Id = Guid.NewGuid(), StepNumber = 1, Instruction = "First", StepIngredients = [] },
            ],
        };

        var detail = recipe.ToDetail();

        detail.Steps[0].StepNumber.Should().Be(1);
        detail.Steps[1].StepNumber.Should().Be(2);
    }

    // ── RecipeIngredient.ToResponse ────────────────────────────────────────────

    [Fact]
    public void RecipeIngredient_ToResponse_MapsNavigationFields()
    {
        var ing = MakeIngredient("tomato", "Tomato");
        var ri = MakeRecipeIngredient(ing);

        var result = ri.ToResponse();

        result.IngredientName.Should().Be("tomato");
        result.IngredientDisplayName.Should().Be("Tomato");
        result.Category.Should().Be(IngredientCategory.DryGoods);
        result.Amount.Should().Be(100m);
        result.Unit.Should().Be("g");
    }

    // ── RecipeStep.ToResponse ─────────────────────────────────────────────────

    [Fact]
    public void RecipeStep_ToResponse_MapsRecipeIngredientIds()
    {
        var ri1 = Guid.NewGuid();
        var ri2 = Guid.NewGuid();
        var step = new RecipeStep
        {
            Id = Guid.NewGuid(),
            StepNumber = 1,
            Instruction = "Do something",
            StepIngredients = [
                new RecipeStepIngredient { RecipeIngredientId = ri1 },
                new RecipeStepIngredient { RecipeIngredientId = ri2 },
            ],
        };

        var result = step.ToResponse();

        result.RecipeIngredientIds.Should().Contain(ri1);
        result.RecipeIngredientIds.Should().Contain(ri2);
        result.RecipeIngredientIds.Should().HaveCount(2);
    }

    // ── MealPlan.ToListItem ───────────────────────────────────────────────────

    [Fact]
    public void MealPlan_ToListItem_MapsScalarFields()
    {
        var plan = MealPlanTestHelpers.MakeMealPlan("Week Plan");
        var result = plan.ToListItem();

        result.Id.Should().Be(plan.Id);
        result.Name.Should().Be("Week Plan");
        result.IsActive.Should().BeTrue();
        result.RecipeCount.Should().Be(0);
        result.FirstScheduledDate.Should().BeNull();
        result.LastScheduledDate.Should().BeNull();
    }

    [Fact]
    public void MealPlan_ToListItem_ComputesDateRange()
    {
        var plan = MealPlanTestHelpers.MakeMealPlan();
        var recipe = MealPlanTestHelpers.MakeRecipe();
        plan.Recipes = [
            MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 2)),
            MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 5)),
            MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, null),
        ];

        var result = plan.ToListItem();

        result.FirstScheduledDate.Should().Be(new DateOnly(2026, 6, 2));
        result.LastScheduledDate.Should().Be(new DateOnly(2026, 6, 5));
        result.RecipeCount.Should().Be(3);
    }

    // ── MealPlan.ToDetail ─────────────────────────────────────────────────────

    [Fact]
    public void MealPlan_ToDetail_DatedRecipesBeforeUndated()
    {
        var plan   = MealPlanTestHelpers.MakeMealPlan();
        var recipe = MealPlanTestHelpers.MakeRecipe();
        var undated = MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, null, displayOrder: 1);
        var dated   = MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 5), displayOrder: 2);
        plan.Recipes = [undated, dated];

        var result = plan.ToDetail();

        result.Recipes[0].ScheduledDate.Should().Be(new DateOnly(2026, 6, 5));
        result.Recipes[1].ScheduledDate.Should().BeNull();
    }

    [Fact]
    public void MealPlan_ToDetail_DatedRecipesSortedAscending()
    {
        var plan   = MealPlanTestHelpers.MakeMealPlan();
        var recipe = MealPlanTestHelpers.MakeRecipe();
        var later  = MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 10), displayOrder: 1);
        var earlier = MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 2), displayOrder: 2);
        plan.Recipes = [later, earlier];

        var result = plan.ToDetail();

        result.Recipes[0].ScheduledDate.Should().Be(new DateOnly(2026, 6, 2));
        result.Recipes[1].ScheduledDate.Should().Be(new DateOnly(2026, 6, 10));
    }

    // ── MealPlanRecipe.ToResponse ─────────────────────────────────────────────

    [Fact]
    public void MealPlanRecipe_ToResponse_FlattensRecipeFields()
    {
        var plan   = MealPlanTestHelpers.MakeMealPlan();
        var recipe = MealPlanTestHelpers.MakeRecipe("Pasta");
        recipe.ImageUrl     = "/uploads/pasta.jpg";
        recipe.LastCookedAt = DateTime.UtcNow;
        var mpr = MealPlanTestHelpers.MakeMealPlanRecipe(plan, recipe, new DateOnly(2026, 6, 5));

        var result = mpr.ToResponse();

        result.RecipeName.Should().Be("Pasta");
        result.RecipeImageUrl.Should().Be("/uploads/pasta.jpg");
        result.RecipeLastCookedAt.Should().Be(recipe.LastCookedAt);
        result.ScheduledDate.Should().Be(new DateOnly(2026, 6, 5));
        result.PortionSize.Should().Be(PortionSize.Regular);
    }
}
