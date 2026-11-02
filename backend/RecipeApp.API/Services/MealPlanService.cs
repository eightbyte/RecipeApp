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
            .Include(p => p.PlannedMeals)
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

        return plans.Select(p => p.ToListItem()).ToList();
    }

    public async Task<MealPlanDetailResponse?> GetActiveAsync()
    {
        var plan = await db.MealPlans
            .Include(p => p.PlannedMeals).ThenInclude(plannedMeal => plannedMeal.Recipe)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive);

        return plan?.ToDetail();
    }

    public async Task<MealPlanDetailResponse?> GetByIdAsync(Guid id)
    {
        var plan = await db.MealPlans
            .Include(p => p.PlannedMeals).ThenInclude(plannedMeal => plannedMeal.Recipe)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        return plan?.ToDetail();
    }

    public async Task<List<SuggestionResponse>?> GetSuggestionsAsync(Guid planId)
    {
        var planExists = await db.MealPlans.AnyAsync(p => p.Id == planId);
        if (!planExists) return null;

        // Get distinct ingredient IDs used by all recipes in this plan
        var planIngredientIds = await db.PlannedMeals
            .Where(plannedMeal => plannedMeal.MealPlanId == planId)
            .SelectMany(plannedMeal => plannedMeal.Recipe.Ingredients.Select(ri => ri.IngredientId))
            .Distinct()
            .ToListAsync();

        if (planIngredientIds.Count == 0)
            return [];

        // Get recipe IDs already in the plan (to exclude)
        var planRecipeIds = await db.PlannedMeals
            .Where(plannedMeal => plannedMeal.MealPlanId == planId)
            .Select(plannedMeal => plannedMeal.RecipeId)
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
            UpdatedAt = DateTime.UtcNow,
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

    // ── Mutations — Planned Meals ──────────────────────────────────────────────

    public async Task<PlannedMealResponse?> AddPlannedMealAsync(Guid planId, AddPlannedMealRequest request)
    {
        var planExists   = await db.MealPlans.AnyAsync(p => p.Id == planId);
        if (!planExists) return null;

        var recipeExists = await db.Recipes.AnyAsync(r => r.Id == request.RecipeId);
        if (!recipeExists) return null;

        var maxOrder = await db.PlannedMeals
            .Where(plannedMeal => plannedMeal.MealPlanId == planId)
            .MaxAsync(plannedMeal => (int?)plannedMeal.DisplayOrder) ?? 0;

        var plannedMeal = new PlannedMeal
        {
            Id            = Guid.NewGuid(),
            MealPlanId    = planId,
            RecipeId      = request.RecipeId,
            ScheduledDate = request.ScheduledDate,
            BatchSize     = request.BatchSize ?? BatchSize.Regular,
            DisplayOrder  = maxOrder + 1,
        };
        db.PlannedMeals.Add(plannedMeal);

        var plan = await db.MealPlans.FindAsync(planId);
        if (plan is not null) plan.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        var loaded = await db.PlannedMeals
            .Include(m => m.Recipe)
            .AsNoTracking()
            .FirstAsync(m => m.Id == plannedMeal.Id);

        return loaded.ToResponse();
    }

    public async Task<PlannedMealResponse?> UpdatePlannedMealAsync(Guid planId, Guid plannedMealId, UpdatePlannedMealRequest request)
    {
        var plannedMeal = await db.PlannedMeals
            .Include(m => m.Recipe)
            .FirstOrDefaultAsync(m => m.Id == plannedMealId && m.MealPlanId == planId);

        if (plannedMeal is null) return null;

        plannedMeal.ScheduledDate = request.ScheduledDate;
        plannedMeal.BatchSize     = request.BatchSize;

        var plan = await db.MealPlans.FindAsync(planId);
        if (plan is not null) plan.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return plannedMeal.ToResponse();
    }

    public async Task<bool> RemovePlannedMealAsync(Guid planId, Guid plannedMealId)
    {
        var plannedMeal = await db.PlannedMeals
            .FirstOrDefaultAsync(m => m.Id == plannedMealId && m.MealPlanId == planId);

        if (plannedMeal is null) return false;

        db.PlannedMeals.Remove(plannedMeal);

        var plan = await db.MealPlans.FindAsync(planId);
        if (plan is not null) plan.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }
}
