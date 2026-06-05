# Phase 5 — Shopping List

**Version:** 1.0  
**Date:** 2026-06-01  
**Status:** Draft  
**Depends on:** Phase 2 (Recipe CRUD), Phase 4 (Meal Planning) complete

---

## 1. Overview

Phase 5 turns the active meal plan into an actionable **shopping list**. The list is
**auto-generated** from the active meal plan's recipes: every recipe ingredient is scaled by its
meal-plan portion multiplier, amounts are aggregated per ingredient, compatible units are
consolidated (g↔kg, ml↔L), and the result is grouped by shopping category for in-store use.
Users check items off as they shop (checked items hidden by default), add custom non-recipe
items that survive regeneration, and see a banner when the meal plan has changed since the list
was generated.

This phase delivers the `shopping_lists` / `shopping_list_items` data layer, the aggregation
service, the REST endpoints, and the wiring of the existing `ShoppingView.vue` stub to a real
store/API. **Food-waste persistence (the `food_waste_log` write that SPEC §6.4.2 couples to list
generation) remains out of scope → Phase 6** (see §11).

---

## 2. Deliverable

> Open the Shopping tab with an active meal plan → the list auto-generates (recipe ingredients
> aggregated with portion multipliers, grouped by category) → check items off (hidden by default,
> "Show purchased" reveals them) → add custom items → if the plan changes, a banner offers
> "Regenerate" (custom items preserved).

---

## 3. Data Model

Two new tables are added and one existing table gains a column. Naming follows the established EF
Core convention in this project: **PascalCase table and column names** (EF/Npgsql defaults — *not*
snake_case, despite the schema sketch in `SPEC.md` §4.8–4.9, exactly as Phase 4 noted). New
`DbSet`s are added to `AppDbContext`.

### 3.1 ShoppingList (`ShoppingLists` table)

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `MealPlanId` | `Guid` FK → `MealPlans` **UNIQUE** | One list per meal plan; cascade delete |
| `GeneratedAt` | `DateTime` (TIMESTAMPTZ) | When the recipe-derived items were last (re)generated; `DateTime.UtcNow` |
| `UpdatedAt` | `DateTime` (TIMESTAMPTZ) | Bumped on any item mutation (check/add/edit/delete); DB default `NOW()` |

Navigation: `ICollection<ShoppingListItem> Items`.

### 3.2 ShoppingListItem (`ShoppingListItems` table)

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `ShoppingListId` | `Guid` FK → `ShoppingLists` | Cascade delete |
| `IngredientId` | `Guid?` FK → `Ingredients` | `NULL` for custom items; **Restrict** delete (preserve catalogue) |
| `CustomName` | `string?` | Set only for custom items (where `IngredientId` is null) |
| `Category` | `string` NOT NULL | `IngredientCategory` constant; mirrors the ingredient's category, or chosen for custom items |
| `Amount` | `decimal(10,3)?` | Combined total at plan portions; `NULL` allowed for custom items with no quantity |
| `Unit` | `string?` | Metric unit; `NULL` allowed for quantity-less custom items |
| `IsChecked` | `bool` DEFAULT `false` | In-store check-off state |
| `IsCustom` | `bool` DEFAULT `false` | `true` = user-added; **not** replaced on regeneration |
| `NeedsReview` | `bool` DEFAULT `false` | `true` when this row is one of several incompatible-unit rows for the same ingredient (see §6.3) |
| `DisplayOrder` | `int` | Sort within a category (see §6.3 ordering) |

Navigation: `ShoppingList ShoppingList`, `Ingredient? Ingredient`.

### 3.3 MealPlan change (existing Phase-4 table)

Add one column to `MealPlan` to support stale-list detection (the meal plan currently has no
"last modified" timestamp):

| Property | Type | Notes |
|---|---|---|
| `UpdatedAt` | `DateTime` (TIMESTAMPTZ) | DB default `NOW()`; bumped whenever a recipe is added/updated/removed from the plan (§10) |

> **Stale rule:** a shopping list is **stale** when `MealPlan.UpdatedAt > ShoppingList.GeneratedAt`.
> Editing the plan after generating the list raises the plan's `UpdatedAt` past the list's
> `GeneratedAt`, surfacing the "plan changed" banner. Checking items off touches only the list's
> own `UpdatedAt`, never the plan's — so it does not make the list look stale.

### 3.4 Model files

```
backend/RecipeApp.API/Models/
├── ShoppingList.cs          NEW
├── ShoppingListItem.cs      NEW
└── MealPlan.cs              MODIFIED (add UpdatedAt)
```

```csharp
namespace RecipeApp.API.Models;

public class ShoppingList
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public MealPlan MealPlan { get; set; } = null!;
    public ICollection<ShoppingListItem> Items { get; set; } = [];
}

public class ShoppingListItem
{
    public Guid Id { get; set; }
    public Guid ShoppingListId { get; set; }
    public Guid? IngredientId { get; set; }      // null for custom items
    public string? CustomName { get; set; }
    public string Category { get; set; } = IngredientCategory.Other;
    public decimal? Amount { get; set; }
    public string? Unit { get; set; }
    public bool IsChecked { get; set; }
    public bool IsCustom { get; set; }
    public bool NeedsReview { get; set; }
    public int DisplayOrder { get; set; }

    public ShoppingList ShoppingList { get; set; } = null!;
    public Ingredient? Ingredient { get; set; }
}
```

> Add a navigation collection to the existing `Ingredient` model is **not required** — configure
> the FK only (matching how `Recipe` was left untouched in Phase 4). Keep `Ingredient` unchanged.

---

## 4. AppDbContext Configuration

Add to `OnModelCreating`:

```csharp
// ── ShoppingList ─────────────────────────────────────────────────────────────
modelBuilder.Entity<ShoppingList>(e =>
{
    e.Property(s => s.GeneratedAt).HasDefaultValueSql("NOW()");
    e.Property(s => s.UpdatedAt).HasDefaultValueSql("NOW()");

    // One shopping list per meal plan.
    e.HasIndex(s => s.MealPlanId).IsUnique();

    e.HasOne(s => s.MealPlan)
        .WithMany()                       // no back-navigation on MealPlan
        .HasForeignKey(s => s.MealPlanId)
        .OnDelete(DeleteBehavior.Cascade);
});

// ── ShoppingListItem ─────────────────────────────────────────────────────────
modelBuilder.Entity<ShoppingListItem>(e =>
{
    e.Property(i => i.Amount).HasPrecision(10, 3);
    e.Property(i => i.Category).HasDefaultValue(IngredientCategory.Other);

    e.HasOne(i => i.ShoppingList)
        .WithMany(s => s.Items)
        .HasForeignKey(i => i.ShoppingListId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(i => i.Ingredient)
        .WithMany()
        .HasForeignKey(i => i.IngredientId)
        .OnDelete(DeleteBehavior.Restrict);   // ingredient FK optional; never cascade-delete catalogue
});

// ── MealPlan (modify existing config) ────────────────────────────────────────
modelBuilder.Entity<MealPlan>(e =>
{
    // ... existing config (CreatedAt default, partial unique index on IsActive) ...
    e.Property(p => p.UpdatedAt).HasDefaultValueSql("NOW()");
});
```

Add the `DbSet`s:

```csharp
public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();
```

### 4.1 Migration

```bash
cd backend/RecipeApp.API
dotnet ef migrations add AddShoppingLists --output-dir Data/Migrations
```

Verify the migration creates both tables, the unique index on `ShoppingLists (MealPlanId)`, the
`decimal(10,3)` precision on `Amount`, the cascade/restrict FK behaviours, the `NOW()` defaults,
and the new `MealPlans.UpdatedAt` column. The dev server auto-migrates on startup (`Program.cs`).

---

## 5. DTOs

**Directory:** `DTOs/ShoppingLists/`

```
DTOs/ShoppingLists/
├── ShoppingListResponse.cs
├── ShoppingListItemResponse.cs
├── AddCustomItemRequest.cs
└── UpdateItemRequest.cs
```

```csharp
namespace RecipeApp.API.DTOs.ShoppingLists;

public record ShoppingListResponse(
    Guid Id,
    Guid MealPlanId,
    string MealPlanName,
    DateTime GeneratedAt,
    bool IsStale,                              // MealPlan.UpdatedAt > GeneratedAt
    List<ShoppingListItemResponse> Items       // flat, pre-sorted by category order then DisplayOrder
);

public record ShoppingListItemResponse(
    Guid Id,
    Guid? IngredientId,                        // null for custom items
    string DisplayName,                        // ingredient display name, or CustomName
    string Category,                           // IngredientCategory constant
    decimal? Amount,
    string? Unit,
    bool IsChecked,
    bool IsCustom,
    bool NeedsReview,
    int DisplayOrder
);

public record AddCustomItemRequest(
    string Name,
    decimal? Amount,
    string? Unit,
    string? Category                           // null → defaults to OTHER
);

public record UpdateItemRequest(
    bool IsChecked,
    decimal? Amount,                           // allows editing quantity
    string? Unit
);
```

> The frontend keeps category **grouping** on the client (the existing `ShoppingView.vue`
> already groups a flat `items` array by `category`). The API therefore returns a flat,
> pre-sorted `Items` list rather than nested groups. `DisplayName` matches the stub's
> `item.displayName`; the full item shape lines up 1:1 with the stub's
> `{ id, displayName, amount, unit, category, isChecked, isCustom }` plus `needsReview`.

---

## 6. Backend Service

**File:** `Services/ShoppingListService.cs`, registered scoped in `Program.cs`
(`builder.Services.AddScoped<ShoppingListService>();`). Constructor-injection style matching
`MealPlanService(AppDbContext db)`.

### 6.1 Queries & Mutations

| Method | Returns | Behaviour |
|---|---|---|
| `GetActiveAsync()` | `ShoppingListResponse?` | Load the active `MealPlan`. If none → `null` (endpoint → `404`). If a `ShoppingList` already exists for it → return it. **If none exists → generate + persist** the list, then return it (auto-generation, §6.2). Computes `IsStale`. |
| `GetByIdAsync(Guid id)` | `ShoppingListResponse?` | Specific list (e.g. for a past plan) with its items. `null` if not found. |
| `RegenerateActiveAsync()` | `ShoppingListResponse?` | Regenerate the active plan's list: **replace all non-custom items**, **preserve custom items**, set `GeneratedAt = UtcNow`. `null` if no active plan. |
| `AddCustomItemAsync(Guid listId, AddCustomItemRequest)` | `ShoppingListItemResponse?` | Create a custom item (`IsCustom = true`, `IngredientId = null`, `CustomName = Name`, `Category` defaulting `null → OTHER`). `DisplayOrder` = current max in that category + 1. Bumps list `UpdatedAt`. `null` if list not found. |
| `UpdateItemAsync(Guid listId, Guid itemId, UpdateItemRequest)` | `ShoppingListItemResponse?` | Toggle `IsChecked`, optionally edit `Amount`/`Unit`. Bumps list `UpdatedAt`. `null` if item not found / wrong list. |
| `DeleteItemAsync(Guid listId, Guid itemId)` | `DeleteItemResult` | Remove a **custom** item only. Returns `NotFound` (wrong list/missing), `Forbidden` (item is recipe-derived, not custom), or `Deleted`. |

> `DeleteItemResult` is a small enum (`NotFound`, `Forbidden`, `Deleted`) so the endpoint can map
> recipe-derived deletes to `409 Conflict` rather than silently succeeding. Recipe-derived items
> are removed only by editing the meal plan and regenerating.

All read queries use `.AsNoTracking()` and `Include(s => s.Items).ThenInclude(i => i.Ingredient)`.

### 6.2 Generation / Aggregation Logic

This mirrors the Include / `SelectMany` / in-memory aggregation style of
`MealPlanService.GetSuggestionsAsync` and reuses `PortionSize.Multiplier`.

**Steps (for the active plan):**
1. Load the active plan with `Recipes → Recipe → RecipeIngredients → Ingredient`.
2. For each `MealPlanRecipe`, compute `multiplier = PortionSize.Multiplier(mpr.PortionSize)` and,
   for each `RecipeIngredient`, a **scaled amount** `ri.Amount * multiplier`.
   *Never mutate the stored `ri.Amount`* (project rule — scale at the query/DTO layer only).
3. Group the scaled amounts by `IngredientId`.
4. Within each ingredient group, **sub-group by unit dimension**:
   - **Mass** (`g`, `kg`) → consolidate to a single row. Convert `kg → g` (×1000) to sum, then
     present in `kg` if the total ≥ 1000 g else `g`.
   - **Volume** (`ml`, `L`) → consolidate to a single row. Convert `L → ml` (×1000) to sum, then
     present in `L` if total ≥ 1000 ml else `ml`.
   - **Count / spoons / other** (`pcs`, `tsp`, `tbsp`, and anything not in a convertible
     dimension) → sum **only within identical units**; each distinct unit stays its own row.
5. If an ingredient ends up with **more than one row** (incompatible units could not be merged),
   set `NeedsReview = true` on **all** of that ingredient's rows so the UI can flag them.
   Single-row ingredients have `NeedsReview = false`.
6. Set each item's `Category` from `Ingredient.Category`, `Amount`/`Unit` from the consolidated
   result, `IsCustom = false`, `IsChecked = false`.
7. **Ordering:** sort items by the index of `Category` in `IngredientCategory.All` (so categories
   appear in the canonical Produce→…→Other order), then by ingredient `DisplayName`, then unit.
   Assign sequential `DisplayOrder` reflecting that sort.
8. Round all consolidated amounts to 3 decimals (`Math.Round(value, 3)`), consistent with the
   `decimal(10,3)` column and the Phase-3 conversion convention.

**Conversion helper:** maintain a small static unit table (dimension + base-unit factor) inside
the service, e.g. `g→1`, `kg→1000` (mass, base `g`); `ml→1`, `L→1000` (volume, base `ml`). Units
absent from the table are treated as their own non-convertible dimension keyed by the raw unit
string (case-insensitive, trimmed) — matching the Phase-3 "match full unit string, not substring"
rule to avoid false positives.

**Regeneration (`RegenerateActiveAsync`):** delete existing items where `IsCustom == false`, run
steps 1–8 to insert fresh recipe-derived items, leave `IsCustom == true` items untouched, set
`GeneratedAt = UtcNow`. Custom items keep their `IsChecked` state. Single `SaveChangesAsync`.

**Empty plan:** an active plan with no recipes generates a list with no recipe-derived items
(custom items, if any, remain). Frontend shows the existing empty state when there are no items.

---

## 7. Mappings

Add to `DTOs/Mappings.cs` (static extension methods, entity-in/DTO-out, consistent with the file):

```csharp
// ── ShoppingList ─────────────────────────────────────────────────────────────

public static ShoppingListItemResponse ToResponse(this ShoppingListItem i) => new(
    i.Id,
    i.IngredientId,
    i.IsCustom ? (i.CustomName ?? string.Empty) : i.Ingredient!.DisplayName,
    i.Category,
    i.Amount,
    i.Unit,
    i.IsChecked,
    i.IsCustom,
    i.NeedsReview,
    i.DisplayOrder
);

public static ShoppingListResponse ToResponse(this ShoppingList s, bool isStale) => new(
    s.Id,
    s.MealPlanId,
    s.MealPlan.Name,
    s.GeneratedAt,
    isStale,
    s.Items
        .OrderBy(i => i.DisplayOrder)
        .Select(ToResponse)
        .ToList()
);
```

> `IsStale` is passed in by the service (it owns the `MealPlan.UpdatedAt` comparison) rather than
> derived in the mapper, keeping the mapper pure. The `MealPlan` navigation must be `Include`d for
> `MealPlanName`.

---

## 8. Validators

**File:** `Validators/ShoppingListValidators.cs` (auto-discovered via
`AddValidatorsFromAssemblyContaining<Program>()`).

**`AddCustomItemRequestValidator`:**
- `Name` not empty, max 200 characters.
- `Category`, when provided (non-null), must be in `IngredientCategory.All`.
- `Amount`, when provided, must be `> 0`.
- `Unit` max 20 characters (when provided).

**`UpdateItemRequestValidator`:**
- `Amount`, when provided, must be `> 0`.
- `Unit` max 20 characters (when provided).

> Existence of the list/item is checked in the service (returns `null` → `404`), not in
> validators — matching the established endpoint pattern.

---

## 9. Endpoints

**File:** `Endpoints/ShoppingListEndpoints.cs`, registered in `Program.cs` as
`api.MapShoppingListEndpoints();`. Single `/shopping-lists` group, tagged `Shopping Lists`.

| Method | Route | Success | Errors |
|---|---|---|---|
| `GET` | `/shopping-lists/active` | `200` `ShoppingListResponse` (auto-generates if missing) | `404` if no active meal plan |
| `GET` | `/shopping-lists/{id:guid}` | `200` `ShoppingListResponse` | `404` |
| `POST` | `/shopping-lists/active/generate` | `200` `ShoppingListResponse` (regenerate; custom items preserved) | `404` if no active plan |
| `POST` | `/shopping-lists/{id:guid}/items` | `201` `ShoppingListItemResponse`, `Location: /api/v1/shopping-lists/{id}` | `400` validation, `404` list missing |
| `PUT` | `/shopping-lists/{id:guid}/items/{itemId:guid}` | `200` `ShoppingListItemResponse` | `400`, `404` |
| `DELETE` | `/shopping-lists/{id:guid}/items/{itemId:guid}` | `204` | `404` not found, `409` if item is recipe-derived (non-custom) |

**Validation pattern** (matches existing endpoints):
```csharp
var validation = await validator.ValidateAsync(request);
if (!validation.IsValid)
    return Results.ValidationProblem(validation.ToDictionary());
```

Each handler injects `ShoppingListService` and the relevant `IValidator<T>`. Map the
`DeleteItemResult` enum: `Deleted → 204`, `NotFound → 404`, `Forbidden → 409` (with a problem
detail, e.g. *"Recipe-derived items are removed by editing the meal plan and regenerating."*).
Add `.WithSummary(...)`/`.WithDescription(...)` to every endpoint, consistent with existing files.

> **Note on `generate` vs auto-generation:** `GET /shopping-lists/active` creates the list on
> first view if absent; `POST /shopping-lists/active/generate` force-**re**generates (used by the
> stale banner's "Regenerate" action). Both preserve custom items.

---

## 10. MealPlan `UpdatedAt` Wiring (Phase-4 file edit)

For stale detection to work, the meal plan's `UpdatedAt` must advance whenever its recipe
composition changes. In `Services/MealPlanService.cs`, set `UpdatedAt = DateTime.UtcNow` on the
parent `MealPlan` within:

- `AddRecipeAsync` (after adding the `MealPlanRecipe`)
- `UpdateRecipeAsync` (portion/date change)
- `RemoveRecipeAsync`

These already load or can cheaply load the plan; set the timestamp before the existing
`SaveChangesAsync`. `CreateAsync` sets `UpdatedAt = CreatedAt` on the new plan. Renaming a plan
(`UpdateAsync`) does **not** affect the shopping list and need not bump `UpdatedAt` (rename has no
effect on ingredients) — but bumping it is harmless if simpler.

> This is the only change to Phase-4 code. It is small and additive; the existing single-active-
> plan logic and tests are unaffected.

---

## 11. Out of Scope for Phase 5

Deferred to later phases (do **not** build in Phase 5):

- **Food-waste persistence** (`food_waste_log`, `standard_package_size`, waste summary/breakdown
  endpoints) → **Phase 6**. Although SPEC §6.4.2 says waste calculation "runs on shopping list
  generation", the table and logic land in Phase 6; Phase 5 only produces the aggregated list.
- **Cooking mode / `last_cooked_at` automation** → **Phase 7**.
- **Cross-dimension unit coercion** (e.g. `pcs → g` using an ingredient's typical weight). Phase 5
  keeps incompatible units lossless via separate `NeedsReview` rows rather than approximating.
- **Custom store-aisle ordering** (Future Functionality) — categories use the fixed
  `IngredientCategory.All` order.
- Multiple concurrent shopping lists per plan (one per plan, enforced by the unique index).

---

## 12. Frontend

### 12.1 New & Modified Files

```
frontend/src/
├── stores/
│   └── shoppingList.js              NEW — Pinia store for the active shopping list
├── views/
│   └── ShoppingView.vue             MODIFIED — wire the existing stub to the store/API
└── components/
    └── AddCustomItemDialog.vue      NEW — dialog/bottom-sheet for the custom-item FAB
```

The `shopping` route (`router/index.js`) and the bottom-nav entry
(`components/layout/AppBottomNav.vue`, `mdi-cart-outline`) **already exist** — no router or nav
changes are required. `MealPlanView.vue` already links to `{ name: 'shopping' }`.

### 12.2 Pinia Store — `stores/shoppingList.js`

`useShoppingListStore`, composition store using the pre-configured `api` instance, mirroring
`stores/mealPlans.js` (`ref` state, async actions, `loading`/`error` handling).

**State:**
```js
const list    = ref(null)    // ShoppingListResponse | null
const loading = ref(false)
const error   = ref(null)
```

**Actions (one per API call):**

| Action | Calls |
|---|---|
| `fetchActive()` | `GET /shopping-lists/active` (treat `404` as "no active plan" → `list = null`, not an error, mirroring `fetchActivePlan`) |
| `regenerate()` | `POST /shopping-lists/active/generate` → replaces `list` |
| `toggleItem(itemId, isChecked)` | `PUT /shopping-lists/{listId}/items/{itemId}` (optimistic flip, revert on error) |
| `updateItem(itemId, payload)` | `PUT /shopping-lists/{listId}/items/{itemId}` |
| `addCustomItem(payload)` | `POST /shopping-lists/{listId}/items` → push into `list.items` |
| `deleteItem(itemId)` | `DELETE /shopping-lists/{listId}/items/{itemId}` |

- After `addCustomItem`/`deleteItem`, patch `list.items` locally (no full refetch needed; server
  returns the created item / 204).
- Expose `isStale` from `list.isStale` for the banner.
- No images on the shopping list, so `assetUrl` is not needed here.

### 12.3 ShoppingView.vue (modify the stub)

Keep the existing template structure, item shape, category labels, toolbar, grouped list, empty
state, and FAB. Wire it up:

1. On mount, `store.fetchActive()`; bind the local `items` to `store.list?.items ?? []` (the
   existing `visibleGroups`/`checkedCount`/`uncheckedCount` computeds work unchanged).
2. Add a **loading skeleton** (`v-skeleton-loader type="list-item-two-line"` ×N) while
   `store.loading` and no list yet — the stub currently lacks one (noted in the stub's TODOs).
3. Replace the stub's local `toggleItem` TODO with `store.toggleItem(item.id, !item.isChecked)`
   (optimistic; the store reverts on failure).
4. Add a **stale banner** above the list when `store.list?.isStale`:
   *"Your meal plan has changed — tap to update your shopping list."* with a **Regenerate**
   action calling `store.regenerate()`. Use `v-alert` (type `info`/`warning`, the app's warning
   colour) consistent with existing alert usage.
5. Wire the **custom-item FAB** to open `AddCustomItemDialog`; on confirm call
   `store.addCustomItem(payload)`.
6. Allow **deleting custom items** (e.g. swipe or an append menu on items where `item.isCustom`):
   call `store.deleteItem(item.id)`. Recipe-derived items are not deletable from the list.
7. Flag **review** items: where `item.needsReview`, show a small warning chip/icon
   (e.g. `mdi-alert-outline`, secondary/warning colour) next to the amount so the user knows the
   units could not be combined.
8. Keep the empty state; when there is no active plan (`store.list === null`) show the existing
   "Create a meal plan to generate your list" CTA routing to `{ name: 'meal-plan' }`.

### 12.4 AddCustomItemDialog.vue (new)

- A `VDialog` (or `VBottomSheet`, consistent with `RecentlyCookedDialog`/`RecipeUrlBottomSheet`).
- Fields: **Name** (required), **Amount** (optional, numeric), **Unit** (optional), **Category**
  (`VSelect` over `IngredientCategory` values, default **Other**) — reuse the ingredients store's
  `fetchCategories()` for the option list.
- Actions: **Add** (emits payload `{ name, amount, unit, category }`) | **Cancel**.

### 12.5 User-Facing Copy

| UI Location | Copy |
|---|---|
| Items remaining (existing) | **{n} items remaining** |
| Show/Hide purchased (existing) | **Show purchased ({n})** / **Hide purchased ({n})** |
| Stale banner body | *Your meal plan has changed — tap to update your shopping list.* |
| Stale banner action | **Regenerate** |
| Empty state heading (existing) | **Shopping list is empty** |
| Empty state body (existing) | **Create a meal plan to generate your list** |
| Add-item dialog title | **Add item** |
| Add-item name label | **Item name** |
| Add-item category label | **Category** |
| Add / Cancel | **Add** / **Cancel** |
| Review chip tooltip | **Units couldn't be combined — check this item** |

---

## 13. Data Flow

```
Open Shopping tab
  → ShoppingView calls fetchActive()
    → GET /api/v1/shopping-lists/active
      → ShoppingListService loads active MealPlan
        → no active plan → 404 → store.list = null → empty state
        → list exists      → return it
        → no list yet      → aggregate recipe ingredients (× portion multiplier),
                              consolidate units, group by category, persist, return (auto-generate)
      → ShoppingListResponse (200, with IsStale)
  → renders grouped, sorted items; checked items hidden by default

Check off an item
  → toggleItem(itemId, !isChecked)  (optimistic UI flip)
    → PUT /api/v1/shopping-lists/{id}/items/{itemId}  (200) ; bumps list UpdatedAt only
    → on error: revert the flip

Add custom item
  → AddCustomItemDialog → addCustomItem({ name, amount, unit, category })
    → POST /api/v1/shopping-lists/{id}/items  (201 ShoppingListItemResponse)
  → push into list.items (IsCustom = true)

Plan changed since generation
  → MealPlan.UpdatedAt > ShoppingList.GeneratedAt  → IsStale = true → banner shown
  → user taps Regenerate → regenerate()
    → POST /api/v1/shopping-lists/active/generate  (200)
      → replace non-custom items, preserve custom items, GeneratedAt = now
```

---

## 14. Testing

### 14.1 Backend Tests

Add to `backend/RecipeApp.Tests/`:

```
Services/
  ShoppingListServiceTests.cs     Integration tests against the Testcontainers DB
Validators/
  ShoppingListValidatorTests.cs   FluentValidation.TestHelper
Endpoints/
  ShoppingListEndpointsTests.cs   Integration via RecipeAppFactory
DTOs/
  MappingsTests.cs                (extend) shopping-list mapping assertions
```

**Service / behaviour tests:**
- Aggregation sums the same ingredient across recipes, applying `HALF`/`REGULAR`/`DOUBLE`
  multipliers (stored `Amount` never mutated).
- Unit consolidation: `g`+`kg` combine into one row (correct total, sensible display unit);
  `ml`+`L` combine; `pcs` rows sum only within `pcs`.
- Incompatible units for one ingredient (`pcs` + `g`) → **separate rows**, all `NeedsReview = true`.
- Category ordering follows `IngredientCategory.All`; `DisplayOrder` assigned accordingly.
- `GetActiveAsync` **auto-generates and persists** when no list exists; returns the existing list
  on subsequent calls; returns `null` when no active plan.
- `RegenerateActiveAsync` replaces recipe-derived items but **preserves custom items** (and their
  checked state) and updates `GeneratedAt`.
- `IsStale` is `true` after a meal-plan recipe mutation bumps `MealPlan.UpdatedAt`; `false`
  immediately after generation; checking an item off does **not** make the list stale.
- `DeleteItemAsync`: custom item → `Deleted`; recipe-derived item → `Forbidden`; wrong list →
  `NotFound`.
- Deleting the meal plan cascades the shopping list and its items.

**Validator tests:**
- `AddCustomItemRequestValidator`: empty/over-long name fails; invalid category fails; null
  category passes (defaults OTHER); non-positive amount fails.
- `UpdateItemRequestValidator`: non-positive amount fails; over-long unit fails.

**Endpoint tests (RecipeAppFactory):**
- `GET /shopping-lists/active` → `404` when no active plan; `200` (auto-generated) when a plan
  with recipes is active.
- `POST /shopping-lists/active/generate` → `200`; custom items survive a regenerate.
- `POST /shopping-lists/{id}/items` → `201` with `Location`; `400` on empty name.
- `PUT .../items/{itemId}` toggles checked → `200`.
- `DELETE .../items/{itemId}` custom → `204`; recipe-derived → `409`; unknown → `404`.

### 14.2 Frontend Tests

```
stores/
  shoppingList.spec.js            Pinia store actions + state (MSW)
views/
  ShoppingView.spec.js            list rendering, grouping, stale banner, toggle
components/
  AddCustomItemDialog.spec.js     fields + emit payload
```

**Store tests (MSW):**
- `fetchActive`: stores response; `404` → `list = null` without setting `error`.
- `toggleItem`: optimistic flip; reverts on API error.
- `addCustomItem`: posts and appends the returned item.
- `regenerate`: posts to generate and replaces `list`.

**Component / view tests:**
- `ShoppingView`: renders category groups from `list.items`; checked items hidden until
  "Show purchased"; stale banner appears when `isStale` and **Regenerate** calls `regenerate`;
  `needsReview` items show the warning chip; empty/no-plan state.
- `AddCustomItemDialog`: requires a name; **Add** emits `{ name, amount, unit, category }`.

Run with the existing commands (`dotnet test …` for backend; `npm test` / `npm run test:coverage`
for frontend). Backend integration tests require Docker (Testcontainers PostgreSQL).

---

## 15. Definition of Done

- [ ] `ShoppingList` and `ShoppingListItem` entities, `MealPlan.UpdatedAt` column, DbContext
      config, and `AddShoppingLists` migration applied (unique index on `MealPlanId` verified).
- [ ] `ShoppingListService` (auto-generate, aggregate with portion multipliers, unit
      consolidation, incompatible-unit `NeedsReview` rows, regenerate-preserving-custom), DTOs,
      mappings, validators, and `ShoppingListEndpoints` implemented and registered in `Program.cs`.
- [ ] `MealPlanService` add/update/remove-recipe actions bump `MealPlan.UpdatedAt`; stale flag
      surfaces correctly.
- [ ] Frontend store, `AddCustomItemDialog`, and the wired `ShoppingView` deliver: auto-generated
      grouped list, check-off (hidden by default + show purchased), custom items, stale banner +
      regenerate, review flagging.
- [ ] Backend and frontend tests added and passing; `CLAUDE.md` "Current phase" line and
      structure notes updated to reflect Phase 5.
- [ ] `SPEC.md` §7 Phase 5 task checkboxes ticked.
```