using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

public enum DeleteItemResult { Deleted, NotFound, Forbidden }

public enum UpdateCustomItemResult { Updated, NotFound, Conflict }

public class ShoppingListService(AppDbContext db)
{
    /// <summary>Threshold at which a g/ml total is promoted to kg/L for display.</summary>
    private const decimal BaseUnitPromotionThreshold = 1000m;

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<ShoppingListResponse?> GetActiveAsync()
    {
        var plan = await db.MealPlans.AsNoTracking().FirstOrDefaultAsync(p => p.IsActive);
        if (plan is null) return null;

        var list = await LoadListWithItems(plan.Id);
        if (list is null)
        {
            await GenerateAndPersistAsync(plan);
            list = await LoadListWithItems(plan.Id);
        }

        return list!.ToResponse(IsStale(plan, list!));
    }

    public async Task<ShoppingListResponse?> GetByIdAsync(Guid id)
    {
        var list = await LoadListWithItems(planId: null, listId: id);
        if (list is null) return null;

        var plan = await db.MealPlans.AsNoTracking().FirstAsync(p => p.Id == list.MealPlanId);
        return list.ToResponse(IsStale(plan, list));
    }

    public async Task<ShoppingListResponse?> RegenerateActiveAsync()
    {
        var plan = await db.MealPlans.FirstOrDefaultAsync(p => p.IsActive);
        if (plan is null) return null;

        var list = await db.ShoppingLists
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.MealPlanId == plan.Id);

        if (list is null)
        {
            await GenerateAndPersistAsync(plan);
        }
        else
        {
            // Remove recipe-derived items, keep custom items
            var toRemove = list.Items.Where(i => !i.IsCustom).ToList();
            db.ShoppingListItems.RemoveRange(toRemove);

            var newItems = await BuildRecipeItemsAsync(plan.Id);
            int maxCustomOrder = list.Items.Where(i => i.IsCustom).Select(i => i.DisplayOrder).DefaultIfEmpty(0).Max();
            foreach (var item in newItems)
            {
                item.ShoppingListId = list.Id;
                db.ShoppingListItems.Add(item);
            }

            list.GeneratedAt = DateTime.UtcNow;
            list.UpdatedAt   = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var refreshed = await LoadListWithItems(plan.Id);
        return refreshed!.ToResponse(IsStale(plan, refreshed!));
    }

    public async Task<ShoppingListItemResponse?> AddCustomItemAsync(Guid listId, AddCustomItemRequest request)
    {
        var list = await db.ShoppingLists.FindAsync(listId);
        if (list is null) return null;

        var category = request.Category ?? IngredientCategory.Other;

        var item = new ShoppingListItem
        {
            Id             = Guid.NewGuid(),
            ShoppingListId = listId,
            IngredientId   = null,
            CustomName     = request.Name.Trim(),
            Category       = category,
            Amount         = request.Amount,
            Unit           = request.Unit?.Trim(),
            IsChecked      = false,
            IsCustom       = true,
            NeedsReview    = false,
            DisplayOrder   = await NextDisplayOrderAsync(listId, category),
        };
        db.ShoppingListItems.Add(item);

        list.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Load ingredient nav (null for custom) — return directly
        return new ShoppingListItemResponse(
            item.Id,
            null,
            item.CustomName,
            item.Category,
            item.Amount,
            item.Unit,
            item.IsChecked,
            item.IsCustom,
            item.NeedsReview,
            item.DisplayOrder
        );
    }

    public async Task<ShoppingListItemResponse?> UpdateItemAsync(Guid listId, Guid itemId, UpdateItemRequest request)
    {
        var item = await db.ShoppingListItems
            .Include(i => i.Ingredient)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == listId);

        if (item is null) return null;

        item.IsChecked = request.IsChecked;
        if (request.Amount.HasValue) item.Amount = request.Amount;
        if (request.Unit is not null) item.Unit = request.Unit.Trim();

        var list = await db.ShoppingLists.FindAsync(listId);
        if (list is not null) list.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return item.ToResponse();
    }

    public async Task<(UpdateCustomItemResult Result, ShoppingListItemResponse? Item)> UpdateCustomItemAsync(
        Guid listId, Guid itemId, UpdateCustomItemRequest request)
    {
        var item = await db.ShoppingListItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == listId);

        if (item is null) return (UpdateCustomItemResult.NotFound, null);
        if (!item.IsCustom) return (UpdateCustomItemResult.Conflict, null);

        if (item.Category != request.Category)
        {
            item.DisplayOrder = await NextDisplayOrderAsync(listId, request.Category);
            item.Category     = request.Category;
        }

        item.CustomName = request.Name.Trim();
        item.Amount     = request.Amount;
        item.Unit       = request.Unit?.Trim();

        var list = await db.ShoppingLists.FindAsync(listId);
        if (list is not null) list.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return (UpdateCustomItemResult.Updated, item.ToResponse());
    }

    public async Task<DeleteItemResult> DeleteItemAsync(Guid listId, Guid itemId)
    {
        var item = await db.ShoppingListItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == listId);

        if (item is null) return DeleteItemResult.NotFound;
        if (!item.IsCustom) return DeleteItemResult.Forbidden;

        db.ShoppingListItems.Remove(item);

        var list = await db.ShoppingLists.FindAsync(listId);
        if (list is not null) list.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return DeleteItemResult.Deleted;
    }

    // ── Generation / Aggregation ──────────────────────────────────────────────

    private async Task GenerateAndPersistAsync(MealPlan plan)
    {
        var list = new ShoppingList
        {
            Id          = Guid.NewGuid(),
            MealPlanId  = plan.Id,
            GeneratedAt = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.ShoppingLists.Add(list);

        var items = await BuildRecipeItemsAsync(plan.Id);
        foreach (var item in items)
        {
            item.ShoppingListId = list.Id;
            db.ShoppingListItems.Add(item);
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<ShoppingListItem>> BuildRecipeItemsAsync(Guid planId)
    {
        var plannedMeals = await db.PlannedMeals
            .Where(plannedMeal => plannedMeal.MealPlanId == planId)
            .Include(plannedMeal => plannedMeal.Recipe)
                .ThenInclude(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
            .AsNoTracking()
            .ToListAsync();

        // Step 1-3: Scale and group by IngredientId
        var byIngredient = new Dictionary<Guid, (Ingredient Ingredient, List<(decimal Amount, string Unit)> Rows)>();

        // An unquantified ingredient is partitioned out before grouping so it is never summed, but tracked because the shopper still has to buy it.
        var unquantifiedIngredients = new Dictionary<Guid, Ingredient>();

        foreach (var plannedMeal in plannedMeals)
        {
            var multiplier = BatchSize.Multiplier(plannedMeal.BatchSize);
            foreach (var ri in plannedMeal.Recipe.Ingredients)
            {
                if (ri.Amount is not { } baseAmount || ri.Unit is not { } unit)
                {
                    unquantifiedIngredients[ri.IngredientId] = ri.Ingredient;
                    continue;
                }

                if (!byIngredient.ContainsKey(ri.IngredientId))
                    byIngredient[ri.IngredientId] = (ri.Ingredient, []);
                byIngredient[ri.IngredientId].Rows.Add((baseAmount * multiplier, unit));
            }
        }

        // Steps 4-8: Consolidate units, detect incompatible, sort
        var consolidated = new List<(Ingredient Ingredient, decimal? Amount, string? Unit, bool NeedsReview)>();

        foreach (var (_, (ing, rows)) in byIngredient)
        {
            var resultRows  = ConsolidateRows(rows, ing.GramsPerMillilitre);
            var needsReview = resultRows.Count > 1;
            foreach (var (amount, unit) in resultRows)
                consolidated.Add((ing, amount, unit, needsReview));
        }

        // An ingredient the plan only ever names yields one amount-less row, while one measured in any recipe keeps its total alone.
        foreach (var (ingredientId, ing) in unquantifiedIngredients)
        {
            if (byIngredient.ContainsKey(ingredientId)) continue;
            consolidated.Add((ing, null, null, false));
        }

        // Sort: by IngredientCategory.All index, then DisplayName, then unit
        var categoryOrder = IngredientCategory.All
            .Select((cat, idx) => (cat, idx))
            .ToDictionary(x => x.cat, x => x.idx);

        var sorted = consolidated
            .OrderBy(x => categoryOrder.TryGetValue(x.Ingredient.Category, out var o) ? o : int.MaxValue)
            .ThenBy(x => x.Ingredient.DisplayName)
            .ThenBy(x => x.Unit ?? string.Empty)
            .ToList();

        var items = sorted.Select((x, idx) => new ShoppingListItem
        {
            Id           = Guid.NewGuid(),
            IngredientId = x.Ingredient.Id,
            Category     = x.Ingredient.Category,
            Amount       = x.Amount,
            Unit         = x.Unit,
            IsChecked    = false,
            IsCustom     = false,
            NeedsReview  = x.NeedsReview,
            DisplayOrder = idx + 1,
        }).ToList();

        return items;
    }

    // ── Consolidation ─────────────────────────────────────────────────────────

    /// <summary>Reduces one ingredient's scaled rows to the fewest honest shopping-list rows, summing mass and volume within themselves and converting volume to mass when a density exists.</summary>
    private static List<(decimal? Amount, string? Unit)> ConsolidateRows(
        List<(decimal Amount, string Unit)> rows, decimal? gramsPerMillilitre)
    {
        // Group by dimension, giving a unit outside the storable set a bucket of its own so it is never summed with something incompatible.
        var byDimension = new Dictionary<string, List<(decimal Amount, string Unit)>>(StringComparer.Ordinal);
        foreach (var (amount, unit) in rows)
        {
            var normalised = unit.Trim();
            var key = MeasurementUnit.DimensionOf(normalised) is { } dimension
                ? dimension.ToString()
                : $"__raw_{normalised.ToLowerInvariant()}";

            if (!byDimension.TryGetValue(key, out var bucket))
                byDimension[key] = bucket = [];
            bucket.Add((amount, normalised));
        }

        var massKey   = UnitDimension.Mass.ToString();
        var volumeKey = UnitDimension.Volume.ToString();

        // A density makes mass and volume the same purchase, applying to every volume unit unlike the import policy.
        if (gramsPerMillilitre is > 0m &&
            byDimension.TryGetValue(massKey, out var massRows) &&
            byDimension.Remove(volumeKey, out var volumeRows))
        {
            var millilitres = SumInBaseUnit(volumeRows);
            massRows.Add((
                MeasurementConverter.MillilitresToGrams(millilitres, gramsPerMillilitre.Value),
                MeasurementUnit.Gram));
        }

        var resultRows = new List<(decimal? Amount, string? Unit)>();
        foreach (var (key, dimRows) in byDimension)
        {
            if (key == massKey)
            {
                resultRows.Add(Present(dimRows, MeasurementUnit.Gram, MeasurementUnit.Kilogram));
            }
            else if (key == volumeKey)
            {
                resultRows.Add(Present(dimRows, MeasurementUnit.Millilitre, MeasurementUnit.Litre));
            }
            else
            {
                // Counts and unrecognised units sum only within identical unit strings.
                foreach (var unitGroup in dimRows.GroupBy(r => r.Unit, StringComparer.OrdinalIgnoreCase))
                    resultRows.Add((Math.Round(unitGroup.Sum(r => r.Amount), 3), unitGroup.Key));
            }
        }

        return resultRows;
    }

    /// <summary>Presents a summed group, keeping a shared non-base unit as the cook wrote it and otherwise summing in the base unit and promoting to kg or L past 1000.</summary>
    private static (decimal? Amount, string? Unit) Present(
        List<(decimal Amount, string Unit)> dimRows, string baseUnit, string largeUnit)
    {
        var distinctUnits = dimRows.Select(r => r.Unit).Distinct(StringComparer.Ordinal).ToList();
        if (distinctUnits.Count == 1 && distinctUnits[0] != baseUnit && distinctUnits[0] != largeUnit)
            return (Math.Round(dimRows.Sum(r => r.Amount), 3), distinctUnits[0]);

        var baseTotal = SumInBaseUnit(dimRows);
        return baseTotal >= BaseUnitPromotionThreshold
            ? (Math.Round(baseTotal / BaseUnitPromotionThreshold, 3), largeUnit)
            : (baseTotal, baseUnit);
    }

    private static decimal SumInBaseUnit(List<(decimal Amount, string Unit)> dimRows) =>
        Math.Round(dimRows.Sum(r => MeasurementUnit.ToBase(r.Amount, r.Unit) ?? r.Amount), 3);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> NextDisplayOrderAsync(Guid listId, string category) =>
        (await db.ShoppingListItems
            .Where(i => i.ShoppingListId == listId && i.Category == category)
            .MaxAsync(i => (int?)i.DisplayOrder) ?? 0) + 1;

    private async Task<ShoppingList?> LoadListWithItems(Guid? planId = null, Guid? listId = null)
    {
        var query = db.ShoppingLists
            .Include(s => s.MealPlan)
            .Include(s => s.Items).ThenInclude(i => i.Ingredient)
            .AsNoTracking();

        if (planId.HasValue)
            return await query.FirstOrDefaultAsync(s => s.MealPlanId == planId.Value);

        if (listId.HasValue)
            return await query.FirstOrDefaultAsync(s => s.Id == listId.Value);

        return null;
    }

    private static bool IsStale(MealPlan plan, ShoppingList list) =>
        plan.UpdatedAt > list.GeneratedAt;
}
