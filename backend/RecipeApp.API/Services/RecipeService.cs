using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Services;

public class RecipeService(AppDbContext db)
{
    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<RecipeListPageResponse> GetListAsync(RecipeListQuery listQuery)
    {
        var recipes = db.Recipes
            .Include(r => r.Ingredients)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(listQuery.Search))
            recipes = recipes.Where(r => EF.Functions.ILike(r.Name, $"%{listQuery.Search}%"));

        if (!string.IsNullOrWhiteSpace(listQuery.Ingredient))
            recipes = recipes.Where(r => r.Ingredients.Any(ri =>
                EF.Functions.ILike(ri.Ingredient.Name, $"%{listQuery.Ingredient.ToLower()}%")));

        if (listQuery.ExcludeRecentDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-listQuery.ExcludeRecentDays.Value);
            recipes = recipes.Where(r => r.LastCookedAt == null || r.LastCookedAt < cutoff);
        }

        if (listQuery.LastCookedBefore.HasValue)
            recipes = recipes.Where(r => r.LastCookedAt == null || r.LastCookedAt < listQuery.LastCookedBefore);

        var totalCount = await recipes.CountAsync();

        var page     = listQuery.Page ?? 1;
        var pageSize = listQuery.PageSize ?? RecipeListPaging.DefaultPageSize;
        var pageOfRecipes = await OrderForList(
                recipes,
                listQuery.Sort ?? RecipeSort.Default,
                listQuery.Direction ?? SortDirection.Default)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new RecipeListPageResponse(pageOfRecipes.Select(r => r.ToListItem()).ToList(), totalCount);
    }

    /// <summary>Orders by the chosen field, then by tie-breakers that never flip with direction so the order is stable across pages.</summary>
    private static IOrderedQueryable<Recipe> OrderForList(IQueryable<Recipe> recipes, string sort, string direction)
    {
        var ascending = direction == SortDirection.Ascending;

        return sort switch
        {
            RecipeSort.Name => recipes
                .OrderByDirection(r => r.Name.ToLower(), ascending)
                .ThenByDescending(r => r.CreatedAt)
                .ThenBy(r => r.Id),
            RecipeSort.DateAdded => ThenByNameThenId(recipes
                .OrderByDirection(r => r.CreatedAt, ascending)),
            // Ordering by "has been cooked" first counts never-cooked recipes as least recent, whatever the database's null ordering.
            RecipeSort.LastCooked => ThenByNameThenId(recipes
                .OrderByDirection(r => r.LastCookedAt != null, ascending)
                .ThenByDirection(r => r.LastCookedAt, ascending)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sort), sort, $"Sort must be one of: {string.Join(", ", RecipeSort.All)}"),
        };
    }

    private static IOrderedQueryable<Recipe> ThenByNameThenId(IOrderedQueryable<Recipe> orderedRecipes) =>
        orderedRecipes.ThenBy(r => r.Name.ToLower()).ThenBy(r => r.Id);

    public async Task<RecipeDetailResponse?> GetByIdAsync(Guid id)
    {
        var recipe = await LoadFullRecipeAsync(id);
        return recipe?.ToDetail();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public async Task<RecipeDetailResponse> CreateAsync(CreateRecipeRequest request)
    {
        var recipe = new Recipe
        {
            Id          = Guid.NewGuid(),
            Name        = request.Name.Trim(),
            Description = request.Description?.Trim(),
            SourceUrl   = request.SourceUrl?.Trim(),
            Servings    = request.Servings,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.Recipes.Add(recipe);

        var ingredients = CreateIngredients(recipe.Id, request.Ingredients);
        CreateSteps(recipe.Id, request.Steps, ingredients);

        await db.SaveChangesAsync();
        return (await GetByIdAsync(recipe.Id))!;
    }

    public async Task<RecipeDetailResponse?> UpdateAsync(Guid id, UpdateRecipeRequest request)
    {
        var recipe = await db.Recipes.FindAsync(id);
        if (recipe is null) return null;

        // Replace strategy: delete all children, then re-create
        db.RecipeIngredients.RemoveRange(db.RecipeIngredients.Where(ri => ri.RecipeId == id));
        db.RecipeSteps.RemoveRange(db.RecipeSteps.Where(rs => rs.RecipeId == id));

        recipe.Name        = request.Name.Trim();
        recipe.Description = request.Description?.Trim();
        recipe.SourceUrl   = request.SourceUrl?.Trim();
        recipe.Servings    = request.Servings;
        recipe.UpdatedAt   = DateTime.UtcNow;

        var ingredients = CreateIngredients(recipe.Id, request.Ingredients);
        CreateSteps(recipe.Id, request.Steps, ingredients);

        await db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var recipe = await db.Recipes.FindAsync(id);
        if (recipe is null) return false;
        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<RecipeDetailResponse?> MarkCookedAsync(Guid id)
    {
        var recipe = await db.Recipes.FindAsync(id);
        if (recipe is null) return null;
        recipe.LastCookedAt = DateTime.UtcNow;
        recipe.UpdatedAt    = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Recipe?> LoadFullRecipeAsync(Guid id) =>
        await db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Ingredients).ThenInclude(ri => ri.StepIngredients)
            .Include(r => r.Steps).ThenInclude(rs => rs.StepIngredients)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

    private List<RecipeIngredient> CreateIngredients(
        Guid recipeId, IEnumerable<RecipeIngredientRequest> requests)
    {
        var result = new List<RecipeIngredient>();
        foreach (var req in requests.OrderBy(r => r.DisplayOrder))
        {
            var (amount, unit) = RecipeIngredient.ToStoredMeasurement(req.Amount, req.Unit);
            var ri = new RecipeIngredient
            {
                Id           = Guid.NewGuid(),
                RecipeId     = recipeId,
                IngredientId = req.IngredientId,
                Amount       = amount,
                Unit         = unit,
                SourceAmount = req.SourceAmount,
                SourceUnit   = req.SourceUnit?.Trim(),
                Notes        = req.Notes?.Trim(),
                DisplayOrder = req.DisplayOrder,
            };
            db.RecipeIngredients.Add(ri);
            result.Add(ri);
        }
        return result;
    }

    private void CreateSteps(
        Guid recipeId,
        IEnumerable<RecipeStepRequest> requests,
        IReadOnlyList<RecipeIngredient> ingredients)
    {
        foreach (var req in requests.OrderBy(s => s.StepNumber))
        {
            var step = new RecipeStep
            {
                Id          = Guid.NewGuid(),
                RecipeId    = recipeId,
                StepNumber  = req.StepNumber,
                Instruction = req.Instruction.Trim(),
            };
            db.RecipeSteps.Add(step);

            foreach (var idx in req.IngredientIndexes.Distinct())
            {
                if (idx >= 0 && idx < ingredients.Count)
                    db.RecipeStepIngredients.Add(new RecipeStepIngredient
                    {
                        StepId             = step.Id,
                        RecipeIngredientId = ingredients[idx].Id,
                    });
            }
        }
    }
}
