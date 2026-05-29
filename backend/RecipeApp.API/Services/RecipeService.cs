using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

public class RecipeService(AppDbContext db)
{
    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<RecipeListItemResponse>> GetListAsync(
        string? search,
        string? ingredient,
        int? excludeRecentDays,
        DateTime? lastCookedBefore)
    {
        var query = db.Recipes
            .Include(r => r.Ingredients)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => EF.Functions.ILike(r.Name, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(ingredient))
            query = query.Where(r => r.Ingredients.Any(ri =>
                EF.Functions.ILike(ri.Ingredient.Name, $"%{ingredient.ToLower()}%")));

        if (excludeRecentDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-excludeRecentDays.Value);
            query = query.Where(r => r.LastCookedAt == null || r.LastCookedAt < cutoff);
        }

        if (lastCookedBefore.HasValue)
            query = query.Where(r => r.LastCookedAt == null || r.LastCookedAt < lastCookedBefore);

        var recipes = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return recipes.Select(r => r.ToListItem()).ToList();
    }

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
            var ri = new RecipeIngredient
            {
                Id           = Guid.NewGuid(),
                RecipeId     = recipeId,
                IngredientId = req.IngredientId,
                Amount       = req.Amount,
                Unit         = req.Unit,
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
