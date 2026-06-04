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
    // Unit dimension table: unit string → base-unit factor (mass base = g, volume base = ml)
    private static readonly Dictionary<string, (string Dimension, decimal Factor)> UnitTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["g"]    = ("mass",   1m),
            ["kg"]   = ("mass",   1000m),
            ["ml"]   = ("volume", 1m),
            ["L"]    = ("volume", 1000m),
        };

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

        foreach (var (ingredient, (ing, rows)) in byIngredient)
        {
            // Group by unit dimension
            var byDimension = new Dictionary<string, List<(decimal Amount, string Unit)>>();
            foreach (var (amount, unit) in rows)
            {
                var normalised = unit.Trim();
                var dimension  = UnitTable.TryGetValue(normalised, out var entry) ? entry.Dimension : $"__raw_{normalised.ToLowerInvariant()}";
                if (!byDimension.ContainsKey(dimension)) byDimension[dimension] = [];
                byDimension[dimension].Add((amount, normalised));
            }

            // Within count/spoon/raw dimensions, further group by identical unit string
            var resultRows = new List<(decimal? Amount, string? Unit)>();
            foreach (var (dim, dimRows) in byDimension)
            {
                if (dim is "mass" or "volume")
                {
                    // Convertible — sum in base unit then present
                    var baseTotal = dimRows.Sum(r =>
                        UnitTable.TryGetValue(r.Unit, out var e) ? r.Amount * e.Factor : r.Amount);
                    baseTotal = Math.Round(baseTotal, 3);

                    if (dim == "mass")
                    {
                        if (baseTotal >= 1000m)
                            resultRows.Add((Math.Round(baseTotal / 1000m, 3), "kg"));
                        else
                            resultRows.Add((baseTotal, "g"));
                    }
                    else
                    {
                        if (baseTotal >= 1000m)
                            resultRows.Add((Math.Round(baseTotal / 1000m, 3), "L"));
                        else
                            resultRows.Add((baseTotal, "ml"));
                    }
                }
                else
                {
                    // Sum only within identical unit strings
                    var byUnit = dimRows.GroupBy(r => r.Unit, StringComparer.OrdinalIgnoreCase);
                    foreach (var unitGroup in byUnit)
                    {
                        var total = Math.Round(unitGroup.Sum(r => r.Amount), 3);
                        resultRows.Add((total, unitGroup.Key));
                    }
                }
            }

            var needsReview = resultRows.Count > 1;
            foreach (var (amt, unit) in resultRows)
                consolidated.Add((ing, amt, unit, needsReview));
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
