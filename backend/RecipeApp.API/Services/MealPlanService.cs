using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

public class MealPlanService(AppDbContext db)
{
    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<MealPlanListItemResponse>> GetListAsync()
    {
        var plans = await db.MealPlans
            .Include(p => p.Recipes)
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

        return plans.Select(p => p.ToListItem()).ToList();
    }

    public async Task<MealPlanDetailResponse?> GetActiveAsync()
    {
        var plan = await db.MealPlans
            .Include(p => p.Recipes).ThenInclude(mpr => mpr.Recipe)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive);

        return plan?.ToDetail();
    }

    public async Task<MealPlanDetailResponse?> GetByIdAsync(Guid id)
    {
        var plan = await db.MealPlans
            .Include(p => p.Recipes).ThenInclude(mpr => mpr.Recipe)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        return plan?.ToDetail();
    }

    public async Task<List<SuggestionResponse>?> GetSuggestionsAsync(Guid planId)
    {
        var planExists = await db.MealPlans.AnyAsync(p => p.Id == planId);
        if (!planExists) return null;

        // Get distinct ingredient IDs used by all recipes in this plan
        var planIngredientIds = await db.MealPlanRecipes
            .Where(mpr => mpr.MealPlanId == planId)
            .SelectMany(mpr => mpr.Recipe.Ingredients.Select(ri => ri.IngredientId))
            .Distinct()
            .ToListAsync();

        if (planIngredientIds.Count == 0)
            return [];

        // Get recipe IDs already in the plan (to exclude)
        var planRecipeIds = await db.MealPlanRecipes
            .Where(mpr => mpr.MealPlanId == planId)
            .Select(mpr => mpr.RecipeId)
            .Distinct()
            .ToListAsync();

        // Find candidates that share at least one ingredient — filtered include loads only overlapping ones
        var candidates = await db.Recipes
            .Where(r => !planRecipeIds.Contains(r.Id))
            .Where(r => r.Ingredients.Any(ri => planIngredientIds.Contains(ri.IngredientId)))
            .Include(r => r.Ingredients.Where(ri => planIngredientIds.Contains(ri.IngredientId)))
            .ThenInclude(ri => ri.Ingredient)
            .AsNoTracking()
            .ToListAsync();

        return candidates
            .Select(r => new SuggestionResponse(
                r.Id,
                r.Name,
                r.ImageUrl,
                r.LastCookedAt,
                r.Ingredients.Count,
                r.Ingredients.Take(3).Select(ri => ri.Ingredient.DisplayName).ToList()
            ))
            .OrderByDescending(s => s.OverlapCount)
            .ThenBy(s => s.RecipeName)
            .Take(10)
            .ToList();
    }

    // ── Mutations — Meal Plans ─────────────────────────────────────────────────

    public async Task<MealPlanDetailResponse> CreateAsync(CreateMealPlanRequest request)
    {
        // Deactivate existing active plan in the same SaveChanges so the partial unique index is satisfied
        var existingActive = await db.MealPlans.FirstOrDefaultAsync(p => p.IsActive);
        if (existingActive is not null)
        {
            existingActive.IsActive = false;
            existingActive.ClosedAt = DateTime.UtcNow;
        }

        var newPlan = new MealPlan
        {
            Id        = Guid.NewGuid(),
            Name      = request.Name.Trim(),
            IsActive  = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.MealPlans.Add(newPlan);

        await db.SaveChangesAsync();

        return (await GetByIdAsync(newPlan.Id))!;
    }

    public async Task<MealPlanDetailResponse?> UpdateAsync(Guid id, UpdateMealPlanRequest request)
    {
        var plan = await db.MealPlans.FindAsync(id);
        if (plan is null) return null;

        plan.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var plan = await db.MealPlans.FindAsync(id);
        if (plan is null) return false;

        db.MealPlans.Remove(plan);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Mutations — Meal Plan Recipes ─────────────────────────────────────────

    public async Task<MealPlanRecipeResponse?> AddRecipeAsync(Guid planId, AddMealPlanRecipeRequest request)
    {
        var planExists   = await db.MealPlans.AnyAsync(p => p.Id == planId);
        if (!planExists) return null;

        var recipeExists = await db.Recipes.AnyAsync(r => r.Id == request.RecipeId);
        if (!recipeExists) return null;

        var maxOrder = await db.MealPlanRecipes
            .Where(mpr => mpr.MealPlanId == planId)
            .MaxAsync(mpr => (int?)mpr.DisplayOrder) ?? 0;

        var mpr = new MealPlanRecipe
        {
            Id            = Guid.NewGuid(),
            MealPlanId    = planId,
            RecipeId      = request.RecipeId,
            ScheduledDate = request.ScheduledDate,
            PortionSize   = request.PortionSize ?? PortionSize.Regular,
            DisplayOrder  = maxOrder + 1,
        };
        db.MealPlanRecipes.Add(mpr);
        await db.SaveChangesAsync();

        var loaded = await db.MealPlanRecipes
            .Include(m => m.Recipe)
            .AsNoTracking()
            .FirstAsync(m => m.Id == mpr.Id);

        return loaded.ToResponse();
    }

    public async Task<MealPlanRecipeResponse?> UpdateRecipeAsync(Guid planId, Guid mprId, UpdateMealPlanRecipeRequest request)
    {
        var mpr = await db.MealPlanRecipes
            .Include(m => m.Recipe)
            .FirstOrDefaultAsync(m => m.Id == mprId && m.MealPlanId == planId);

        if (mpr is null) return null;

        mpr.ScheduledDate = request.ScheduledDate;
        mpr.PortionSize   = request.PortionSize;
        await db.SaveChangesAsync();

        return mpr.ToResponse();
    }

    public async Task<bool> RemoveRecipeAsync(Guid planId, Guid mprId)
    {
        var mpr = await db.MealPlanRecipes
            .FirstOrDefaultAsync(m => m.Id == mprId && m.MealPlanId == planId);

        if (mpr is null) return false;

        db.MealPlanRecipes.Remove(mpr);
        await db.SaveChangesAsync();
        return true;
    }
}
