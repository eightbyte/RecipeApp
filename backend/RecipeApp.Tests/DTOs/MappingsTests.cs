using FluentAssertions;
using RecipeApp.API.DTOs;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.Tests.DTOs;

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
}
