using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

public enum DeleteItemResult { Deleted, NotFound, Forbidden }

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

        var maxOrder = await db.ShoppingListItems
            .Where(i => i.ShoppingListId == listId && i.Category == category)
            .MaxAsync(i => (int?)i.DisplayOrder) ?? 0;

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
            DisplayOrder   = maxOrder + 1,
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
        var planRecipes = await db.MealPlanRecipes
            .Where(mpr => mpr.MealPlanId == planId)
            .Include(mpr => mpr.Recipe)
                .ThenInclude(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
            .AsNoTracking()
            .ToListAsync();

        // Step 1-3: Scale and group by IngredientId
        var byIngredient = new Dictionary<Guid, (Ingredient Ingredient, List<(decimal Amount, string Unit)> Rows)>();

        foreach (var mpr in planRecipes)
        {
            var multiplier = PortionSize.Multiplier(mpr.PortionSize);
            foreach (var ri in mpr.Recipe.Ingredients)
            {
                var scaledAmount = ri.Amount * multiplier;
                if (!byIngredient.ContainsKey(ri.IngredientId))
                    byIngredient[ri.IngredientId] = (ri.Ingredient, []);
                byIngredient[ri.IngredientId].Rows.Add((scaledAmount, ri.Unit));
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

    /// <summary>
    /// Reduces one ingredient's scaled rows to the fewest shopping-list rows that are still
    /// honest. Mass and volume each sum within themselves; when an ingredient is measured both
    /// ways and carries a bulk density, the volume total converts to mass so the shopper sees a
    /// single purchase quantity. What is left over — a count, an unrecognised unit, or a
    /// mass/volume mix on an ingredient with no density — stays a row of its own, which is what
    /// makes the item genuinely worth reviewing. See Phase 8.5.1 §7.
    /// </summary>
    private static List<(decimal? Amount, string? Unit)> ConsolidateRows(
        List<(decimal Amount, string Unit)> rows, decimal? gramsPerMillilitre)
    {
        // Group by dimension. A unit outside the storable set gets a bucket of its own, keyed by
        // the exact string, so it is never silently summed with something incompatible.
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

        // Cross-dimension resolution: a density makes mass and volume the same purchase. It
        // applies to every volume unit here, unlike the import policy, because a shopping total is
        // a quantity to buy rather than an instruction to follow.
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

    /// <summary>
    /// Presents a summed mass or volume group. Rows that all share one non-base unit are presented
    /// in that unit — 2 cup + 1 cup reads as 3 cup, because that is what the cook wrote and no
    /// arithmetic improves on it. A mixture sums in the base unit, promoting to kg/L past 1000.
    /// </summary>
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
