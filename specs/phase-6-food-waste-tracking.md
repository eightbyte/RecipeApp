# Phase 6 — Food Waste Tracking

**Version:** 2.0  
**Date:** 2026-06-04  
**Status:** Deferred  
**Depends on:** Phase 4 (Meal Planning), Phase 5 (Shopping List) complete

> **This phase is deferred to a future iteration.** Meaningful waste tracking requires an
> ingredient-admin capability (per-ingredient standard package sizes) that adds scope beyond the
> current development track. The full specification is preserved here for when that iteration is
> scheduled. Phase 7 (Polish, UX & Cooking Mode) is the next active phase.
>
> See also: `SPEC.md` §7 Phase 6 and §9 Future Functionality.

---

## 1. Overview

Phase 6 adds **food waste tracking** driven by per-ingredient **standard package sizes**.

The core insight (from the product decision behind this phase): waste can only be meaningfully
estimated for ingredients sold in known package units — produce sold per piece, canned/dry goods
sold in fixed weights, etc. Hard-to-portion goods (most meat, dairy) and negligible quantities
(spices) should simply **not be tracked**. So tracking is **opt-in per ingredient**: a user sets a
*standard package size* on an ingredient (e.g. canned tomatoes = `400 g`, onion = `1 pcs`); an
ingredient with **no** standard package size is excluded from waste tracking entirely.

This requires three things:

1. An **ingredient-admin capability** — the catalogue gains `StandardPackageSize` + `StandardUnit`
   columns, the ingredient `PUT` endpoint accepts them, and a new **Manage Ingredients** screen
   lets the user set them.
2. A **waste calculation** that runs whenever a shopping list is generated/regenerated: for each
   *tracked* ingredient it computes how many whole packages the plan forces you to buy and how much
   is left over, persisting the result to `food_waste_log`.
3. **Display** — the existing home-screen waste placeholder is wired to live stats, the meal-plan
   detail view gains a per-ingredient waste breakdown, and the suggestions panel highlights
   ingredient reuse.

> This phase brings `StandardPackageSize` and `WasteAmount` (SPEC §4.10) into scope — Phase 5 had
> deferred all `food_waste_log` work to here.

---

## 2. Deliverable

> Set a standard package size on ingredients via **Manage Ingredients** → generate/regenerate a
> shopping list with an active plan → the backend computes per-ingredient leftover for every
> *tracked* ingredient and persists it → the home card shows *"You've fully used N ingredients with
> no leftovers across M meal plans"* → the meal-plan detail view shows a per-ingredient breakdown
> (fully-used vs has-leftovers, with leftover amounts) → suggestion cards highlight ingredient
> reuse.

**Worked example (amount-based calculation):**

| Ingredient | Package | Plan total | Packages bought | Leftover (waste) | Fully used? |
|---|---|---|---|---|---|
| Onion | `1 pcs` | `3 pcs` | 3 | `0` | ✅ |
| Garlic | `1 pcs` | `2 pcs` | 2 | `0` | ✅ |
| Canned tomato | `400 g` | `600 g` | 2 (`800 g`) | `200 g` | ❌ |
| Cumin (no package set) | — | `2 tsp` | — | — | *not tracked* |

---

## 3. Data Model

Naming follows the established EF Core convention: **PascalCase** table/column names (matching
Phases 4–5). The schema sketch in `SPEC.md` §4.2 / §4.10 uses snake_case — the actual columns are
PascalCase, exactly as prior phases noted.

### 3.1 Ingredient changes (existing `Ingredients` table)

Two nullable columns are added to enable opt-in waste tracking:

| Property | Type | Notes |
|---|---|---|
| `StandardPackageSize` | `decimal(10,3)?` | Purchase package quantity, e.g. `400`. `null` = ingredient not tracked for waste |
| `StandardUnit` | `string?` | Metric unit of the package, e.g. `g`, `pcs`, `ml`. `null` = not tracked |

> **Both-or-neither rule:** an ingredient is *tracked* only when **both** are set. Setting one
> without the other is a validation error (§10). Clearing both makes the ingredient untracked
> (e.g. spices).

Updated [Ingredient.cs](../backend/RecipeApp.API/Models/Ingredient.cs):

```csharp
public class Ingredient
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "OTHER";
    public string? DefaultUnit { get; set; }

    // ── Phase 6: waste tracking (both null = not tracked) ──
    public decimal? StandardPackageSize { get; set; }
    public string? StandardUnit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = [];
}
```

### 3.2 FoodWasteLog (`FoodWasteLogs` table — NEW)

One row per **tracked** ingredient per meal plan. Package size is **snapshotted** at calculation
time so a later admin edit doesn't silently rewrite historical waste figures.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `MealPlanId` | `Guid` FK → `MealPlans` | Cascade delete |
| `IngredientId` | `Guid` FK → `Ingredients` | Restrict delete (preserve catalogue) |
| `TotalAmountInPlan` | `decimal(10,3)` | Plan total for this ingredient, expressed in `Unit` |
| `StandardPackageSize` | `decimal(10,3)` | Snapshot of the ingredient's package size at calc time |
| `Unit` | `string` NOT NULL | The package unit the totals are expressed in (snapshot of `StandardUnit`) |
| `PackagesPurchased` | `int` | `ceil(TotalAmountInPlan / StandardPackageSize)` |
| `WasteAmount` | `decimal(10,3)` | `PackagesPurchased × StandardPackageSize − TotalAmountInPlan` (≥ 0) |
| `IsFullyUsed` | `bool` | `WasteAmount == 0` |
| `RecordedAt` | `DateTime` (TIMESTAMPTZ) | `DateTime.UtcNow` at calculation |

**Unique constraint:** one row per `(MealPlanId, IngredientId)` (per the "one row per ingredient"
decision). On (re)calculation the service deletes all rows for the plan then re-inserts, so the
index is a safety net.

> **Untracked ingredients produce no row.** An ingredient with no package size — or whose plan
> usage can't be converted into the package's unit dimension (e.g. package in `g` but the plan
> only uses it in `pcs`, see §6.2) — is silently skipped, never appearing in the log.

Navigation: `MealPlan MealPlan`, `Ingredient Ingredient`.

### 3.3 Model files

```
backend/RecipeApp.API/Models/
├── Ingredient.cs           MODIFIED (add StandardPackageSize, StandardUnit)
└── FoodWasteLog.cs         NEW
```

```csharp
namespace RecipeApp.API.Models;

public class FoodWasteLog
{
    public Guid Id { get; set; }
    public Guid MealPlanId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal TotalAmountInPlan { get; set; }
    public decimal StandardPackageSize { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int PackagesPurchased { get; set; }
    public decimal WasteAmount { get; set; }
    public bool IsFullyUsed { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public MealPlan MealPlan { get; set; } = null!;
    public Ingredient Ingredient { get; set; } = null!;
}
```

---

## 4. AppDbContext Configuration

```csharp
// ── Ingredient (extend existing config) ───────────────────────────────────────
modelBuilder.Entity<Ingredient>(e =>
{
    // ... existing config (Name unique index, etc.) ...
    e.Property(i => i.StandardPackageSize).HasPrecision(10, 3);
    e.Property(i => i.StandardUnit).HasMaxLength(20);
});

// ── FoodWasteLog ──────────────────────────────────────────────────────────────
modelBuilder.Entity<FoodWasteLog>(e =>
{
    e.Property(f => f.TotalAmountInPlan).HasPrecision(10, 3);
    e.Property(f => f.StandardPackageSize).HasPrecision(10, 3);
    e.Property(f => f.WasteAmount).HasPrecision(10, 3);
    e.Property(f => f.RecordedAt).HasDefaultValueSql("NOW()");

    e.HasIndex(f => new { f.MealPlanId, f.IngredientId }).IsUnique();

    e.HasOne(f => f.MealPlan)
        .WithMany()
        .HasForeignKey(f => f.MealPlanId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(f => f.Ingredient)
        .WithMany()
        .HasForeignKey(f => f.IngredientId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

Add the `DbSet`:

```csharp
public DbSet<FoodWasteLog> FoodWasteLogs => Set<FoodWasteLog>();
```

### 4.1 Migration

```bash
cd backend/RecipeApp.API
dotnet ef migrations add AddFoodWasteTracking --output-dir Data/Migrations
```

One migration adds both the `Ingredients` columns and the `FoodWasteLogs` table. Verify: the two
new nullable `Ingredients` columns, the new table, the unique index on
`(MealPlanId, IngredientId)`, `decimal(10,3)` precision, the `NOW()` default on `RecordedAt`, and
the cascade/restrict FK behaviours. The dev server auto-migrates on startup.

---

## 5. Shared Unit Helper (refactor of Phase 5)

The waste calculation must consolidate and convert units **exactly as the shopping list does**, but
that logic is currently **private** inside
[ShoppingListService.BuildRecipeItemsAsync](../backend/RecipeApp.API/Services/ShoppingListService.cs#L190)
(the `UnitTable`, the mass/volume consolidation). To avoid divergent duplicate logic (per the
"avoid hard-coded values" / DRY conventions), **extract a shared static helper** and have both
services use it.

**New file:** `Enums/Units.cs` (or `Services/UnitConsolidation.cs` — keep next to the domain it
serves; `Enums/` already holds `IngredientCategory`/`PortionSize` constant classes):

```csharp
namespace RecipeApp.API.Enums;

public static class Units
{
    // Metric units allowed across the app (mirrors CLAUDE.md "Measurements").
    public static readonly IReadOnlyList<string> All =
        ["g", "kg", "ml", "L", "pcs", "tsp", "tbsp"];

    public static bool IsValid(string unit) =>
        All.Any(u => string.Equals(u, unit?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Convertible dimensions: unit → (dimension, factor to base unit).
    // mass base = g, volume base = ml.
    private static readonly Dictionary<string, (string Dimension, decimal Factor)> Table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["g"]  = ("mass",   1m),
            ["kg"] = ("mass",   1000m),
            ["ml"] = ("volume", 1m),
            ["L"]  = ("volume", 1000m),
        };

    /// <summary>Dimension key for a unit ("mass"/"volume" or a raw, non-convertible key).</summary>
    public static string Dimension(string unit) =>
        Table.TryGetValue(unit.Trim(), out var e) ? e.Dimension : $"__raw_{unit.Trim().ToLowerInvariant()}";

    /// <summary>Consolidate (amount, unit) rows: sum within convertible dimensions, present in the
    /// larger unit when the base total crosses 1000; sum non-convertible units only within identical
    /// unit strings. Returns one row per resulting dimension/unit.</summary>
    public static List<(decimal Amount, string Unit)> Consolidate(
        IEnumerable<(decimal Amount, string Unit)> rows) { /* moved from BuildRecipeItemsAsync */ }

    /// <summary>Convert an amount between two units of the same convertible dimension.
    /// Returns false when the units aren't in the same convertible dimension.</summary>
    public static bool TryConvert(decimal amount, string fromUnit, string toUnit, out decimal result)
    { /* base-factor conversion; result rounded to 3 dp */ }
}
```

**Refactor note:** `ShoppingListService.BuildRecipeItemsAsync` is rewritten to call
`Units.Consolidate(...)` instead of its inline logic; its private `UnitTable` is removed. This is a
**behaviour-preserving** extraction — all existing Phase 5 tests must still pass unchanged.

---

## 6. Backend Service — FoodWasteService

**File:** `Services/FoodWasteService.cs`, registered scoped in `Program.cs`
(`builder.Services.AddScoped<FoodWasteService>();`). Primary-constructor style:
`FoodWasteService(AppDbContext db)`.

### 6.1 Queries

| Method | Returns | Behaviour |
|---|---|---|
| `GetSummaryAsync()` | `FoodWasteSummaryResponse` | Aggregate counts across all `FoodWasteLogs`. Always returns a response (counts may be zero). |
| `GetByMealPlanAsync(Guid mealPlanId)` | `FoodWasteMealPlanResponse?` | All log rows for the plan with `Ingredient.DisplayName`. `null` if the plan doesn't exist; empty `Items` if it exists but has no log rows (list never generated, or no tracked ingredients). |

Both use `.AsNoTracking()`.

### 6.2 Waste Calculation

**Method:** `CalculateAndPersistAsync(Guid mealPlanId)`  
**Called by:** `ShoppingListService` after each generate / regenerate (§7).

**Algorithm:**

1. Load the plan with `Recipes → Recipe → RecipeIngredients → Ingredient`. If the plan doesn't
   exist, return early (no error).
2. For each `MealPlanRecipe` apply `multiplier = PortionSize.Multiplier(mpr.PortionSize)` to each
   `RecipeIngredient`, producing `(IngredientId, scaledAmount = ri.Amount × multiplier, ri.Unit)`.
   *Never mutate stored `ri.Amount`.*
3. Group by `IngredientId`. For each ingredient:
   1. **Skip if untracked:** `Ingredient.StandardPackageSize` or `StandardUnit` is `null`.
   2. Consolidate its scaled rows with `Units.Consolidate(...)`.
   3. Select the consolidated row in the **same dimension** as `StandardUnit`
      (`Units.Dimension`). If none matches, **skip** (can't compute — e.g. package in `g`,
      plan uses `pcs`). If a same-dimension row exists, convert it into `StandardUnit` via
      `Units.TryConvert` → `total`. (For `pcs`/`tsp`/`tbsp` the dimension is the raw unit string,
      so the package unit must match exactly.)
   4. `packages = (int)Math.Ceiling(total / packageSize)`.
   5. `waste = Math.Round(packages × packageSize − total, 3)`.
   6. `isFullyUsed = waste == 0`.
   7. Build a `FoodWasteLog`: `TotalAmountInPlan = Math.Round(total, 3)`,
      `StandardPackageSize = packageSize` (snapshot), `Unit = StandardUnit` (snapshot),
      `PackagesPurchased = packages`, `WasteAmount = waste`, `IsFullyUsed`,
      `RecordedAt = DateTime.UtcNow`.
4. **Delete** all existing `FoodWasteLog` rows for `mealPlanId`, **insert** the freshly built rows,
   single `await db.SaveChangesAsync()`.

> **Why this defines "saved":** an ingredient used by only one recipe rarely fills whole packages,
> so it shows leftover (`IsFullyUsed = false`). When several recipes share it, the plan total grows
> toward whole packages and leftover shrinks to zero (`IsFullyUsed = true`) — directly rewarding the
> reuse behaviour the suggestions feature encourages. This matches SPEC §6.4.2's "Waste Saved =
> ingredients whose remainder was consumed by another recipe in the same plan", grounded in real
> package sizes rather than the recipe-count proxy.

### 6.3 Summary aggregation & message

`GetSummaryAsync` computes:

- `IngredientsFullyUsed` = count of rows where `IsFullyUsed == true`.
- `IngredientsWithWaste` = count where `IsFullyUsed == false`.
- `MealPlansTracked` = distinct `MealPlanId` count.

The headline `Message` (singular/plural correct; **no hard-coded strings in the endpoint** — build
it in the service):

| Condition | Message |
|---|---|
| `MealPlansTracked == 0` | *"No waste data yet — set package sizes on your ingredients and generate a shopping list to start tracking."* |
| else | *"You've fully used {IngredientsFullyUsed} ingredient(s) with no leftovers across {MealPlansTracked} meal plan(s)."* |

> Amounts can't be summed into one headline number because tracked ingredients span different units
> (`g`, `ml`, `pcs`) — so the home headline is **count-based**. Actual leftover *amounts* appear
> per-ingredient in the meal-plan detail breakdown (§12.4), each in its own unit.

---

## 7. Integration with ShoppingListService (Phase 5 edit)

`FoodWasteService.CalculateAndPersistAsync` runs every time recipe-derived shopping items are
(re)generated. Inject `FoodWasteService` into `ShoppingListService` and call it at **two precise
sites**, each immediately **after** that path's existing `SaveChangesAsync` (so the shopping items
are committed first; both services share the scoped `AppDbContext`):

1. End of `GenerateAndPersistAsync`
   ([ShoppingListService.cs:188](../backend/RecipeApp.API/Services/ShoppingListService.cs#L188)) —
   covers first-view auto-generation **and** `RegenerateActiveAsync`'s no-list branch.
2. End of the regenerate branch in `RegenerateActiveAsync`
   ([ShoppingListService.cs:79](../backend/RecipeApp.API/Services/ShoppingListService.cs#L79)).

```csharp
public class ShoppingListService(AppDbContext db, FoodWasteService foodWaste)
{
    // ... after the SaveChangesAsync in GenerateAndPersistAsync:
    await foodWaste.CalculateAndPersistAsync(plan.Id);
}
```

> Checking items off, adding/deleting custom items do **not** recalculate waste — only recipe
> composition changes do, and those already prompt a regenerate via the Phase 5 stale banner.
> `CalculateAndPersistAsync` issues its own `SaveChangesAsync`; keep it separate from the shopping
> list save (two sequential atomic saves in one request).

---

## 8. DTOs

### 8.1 Ingredient DTOs (extend existing — `DTOs/Ingredients/`)

Add the two fields to each record:

```csharp
public record IngredientResponse(
    Guid Id, string Name, string DisplayName, string Category, string? DefaultUnit,
    decimal? StandardPackageSize, string? StandardUnit,   // NEW
    DateTime CreatedAt);

public record CreateIngredientRequest(
    string Name, string DisplayName, string Category, string? DefaultUnit,
    decimal? StandardPackageSize = null, string? StandardUnit = null);  // NEW (optional)

public record UpdateIngredientRequest(
    string DisplayName, string Category, string? DefaultUnit,
    decimal? StandardPackageSize, string? StandardUnit);  // NEW
```

### 8.2 FoodWaste DTOs (`DTOs/FoodWaste/`)

```
DTOs/FoodWaste/
├── FoodWasteSummaryResponse.cs
├── FoodWasteMealPlanResponse.cs
└── FoodWasteItemResponse.cs
```

```csharp
namespace RecipeApp.API.DTOs.FoodWaste;

public record FoodWasteSummaryResponse(
    int IngredientsFullyUsed,
    int IngredientsWithWaste,
    int MealPlansTracked,
    string Message);

public record FoodWasteMealPlanResponse(
    Guid MealPlanId,
    string MealPlanName,
    int IngredientsFullyUsed,
    int IngredientsWithWaste,
    List<FoodWasteItemResponse> Items);

public record FoodWasteItemResponse(
    Guid Id,
    Guid IngredientId,
    string IngredientName,        // Ingredient.DisplayName
    decimal TotalAmountInPlan,
    decimal StandardPackageSize,
    string Unit,
    int PackagesPurchased,
    decimal WasteAmount,
    bool IsFullyUsed);
```

---

## 9. Mappings

Add to `DTOs/Mappings.cs`. Also **update** the existing `Ingredient.ToResponse()` to include the
two new fields.

```csharp
// Ingredient (extend existing mapping)
public static IngredientResponse ToResponse(this Ingredient i) => new(
    i.Id, i.Name, i.DisplayName, i.Category, i.DefaultUnit,
    i.StandardPackageSize, i.StandardUnit,            // NEW
    i.CreatedAt);

// ── FoodWasteLog ─────────────────────────────────────────────────────────────
public static FoodWasteItemResponse ToResponse(this FoodWasteLog f) => new(
    f.Id, f.IngredientId, f.Ingredient.DisplayName,
    f.TotalAmountInPlan, f.StandardPackageSize, f.Unit,
    f.PackagesPurchased, f.WasteAmount, f.IsFullyUsed);
```

`FoodWasteSummaryResponse` / `FoodWasteMealPlanResponse` are assembled in the service (aggregate
counts + message don't map from a single entity), consistent with how `ShoppingListService`
computes `IsStale` outside the mapper.

---

## 10. Validators

### 10.1 Ingredient validators (extend `Validators/IngredientValidators.cs`)

Add to **both** `CreateIngredientValidator` and `UpdateIngredientValidator`:

- **Both-or-neither:** `StandardPackageSize` and `StandardUnit` must be either **both** set or
  **both** null. (`RuleFor(x => x).Must(x => (size, unit) both null || both set)` with a clear
  message: *"Set both a package size and unit to track waste, or leave both empty."*)
- `StandardPackageSize`, when set, must be `> 0`.
- `StandardUnit`, when set, must be in `Units.All` (reuse the new helper rather than a literal list).

### 10.2 FoodWaste

No request DTOs / validators — the `/food-waste` endpoints are read-only.

---

## 11. Endpoints

### 11.1 Ingredient PUT (extend existing handler)

In [IngredientsEndpoints.cs](../backend/RecipeApp.API/Endpoints/IngredientsEndpoints.cs#L88), the
`PUT /ingredients/{id}` handler currently sets `DisplayName`/`Category`/`DefaultUnit`. Add:

```csharp
ingredient.StandardPackageSize = request.StandardPackageSize;
ingredient.StandardUnit        = request.StandardUnit?.Trim();
```

(`POST /ingredients` may optionally carry them too via the defaulted request params; the admin UI
primarily uses `PUT`.) No route changes — the existing endpoint and the ingredients store's
`updateIngredient` already round-trip the full payload.

### 11.2 FoodWaste endpoints (NEW)

**File:** `Endpoints/FoodWasteEndpoints.cs`, registered in `Program.cs` as
`api.MapFoodWasteEndpoints();`. Group `/food-waste`, tagged `Food Waste`.

| Method | Route | Success | Errors |
|---|---|---|---|
| `GET` | `/food-waste/summary` | `200 FoodWasteSummaryResponse` (counts may be zero) | — |
| `GET` | `/food-waste/meal-plan/{id:guid}` | `200 FoodWasteMealPlanResponse` | `404` plan not found |

```csharp
group.MapGet("/summary", async (FoodWasteService service) =>
    Results.Ok(await service.GetSummaryAsync()))
    .WithSummary("Get lifetime waste-saving summary");

group.MapGet("/meal-plan/{id:guid}", async (Guid id, FoodWasteService service) =>
{
    var result = await service.GetByMealPlanAsync(id);
    return result is null ? Results.NotFound() : Results.Ok(result);
})
.WithSummary("Get waste breakdown for a specific meal plan");
```

Add `.WithDescription(...)` to each, consistent with existing endpoint files.

---

## 12. Frontend

### 12.1 New & Modified Files

```
frontend/src/
├── stores/
│   └── foodWaste.js                  NEW — Pinia store for waste data
├── views/
│   ├── HomeView.vue                  MODIFIED — wire the existing waste placeholder card
│   ├── MealPlanDetailView.vue        MODIFIED — add waste breakdown section
│   └── ManageIngredientsView.vue     NEW — ingredient admin (set package sizes)
├── components/
│   ├── SuggestionsPanel.vue          MODIFIED — elevate reuse label to a chip
│   └── EditIngredientDialog.vue      NEW — edit one ingredient's package size
└── router/index.js                   MODIFIED — add the `ingredients` route
```

### 12.2 Pinia Store — `stores/foodWaste.js`

`useFoodWasteStore`, composition store mirroring `stores/shoppingList.js` / `mealPlans.js`.

```js
const summary = ref(null)   // FoodWasteSummaryResponse | null
const detail  = ref(null)   // FoodWasteMealPlanResponse | null
const loading = ref(false)
const error   = ref(null)
```

| Action | Calls |
|---|---|
| `fetchSummary()` | `GET /food-waste/summary` → `summary` |
| `fetchByMealPlan(id)` | `GET /food-waste/meal-plan/{id}` → `detail`; `404` → `detail = null` (not an error, mirroring `fetchActivePlan`) |

### 12.3 HomeView — wire the existing placeholder card

[HomeView.vue:68-77](../frontend/src/views/HomeView.vue#L68-L77) **already** renders a Food-waste
card (`success`/`tonal`, `mdi-leaf`, *"Coming soon…"*). Keep the card **always visible** (per the
product decision) and wire its body text:

- Add `useFoodWasteStore`; call `fetchSummary()` in the existing `onMounted` alongside
  `fetchActivePlan()`.
- Title stays **"Food waste saved"**.
- Body: when `summary` exists → `summary.message`; otherwise keep the friendly zero-state
  (*"Tracked after your first meal plan"*). Both render in the same card — no show/hide logic.

### 12.4 MealPlanDetailView — waste breakdown section

[MealPlanDetailView.vue](../frontend/src/views/MealPlanDetailView.vue) renders both active and
closed plans (reached from `PastPlansView`). After the recipe list, add a collapsible
`VExpansionPanel`. Call `fetchByMealPlan(props.id)` in `onMounted` (the `id` prop already exists).

Render only when `detail` is non-null. Layout:

```
▼  Food Waste — 3 fully used · 1 with leftovers

  Fully used (3)
  ──────────────────────────────────
  ✓  Onion           3 pcs   (3 packs, 0 left)
  ✓  Garlic          2 pcs
  ✓  Flour           1 kg

  Has leftovers (1)
  ──────────────────────────────────
  ⚠  Canned tomato   600 g   (2 packs of 400 g → 200 g left over)
```

- **Fully used** (`isFullyUsed = true`): green check, muted-green row.
- **Has leftovers** (`isFullyUsed = false`): amber warning, muted-amber row; show
  `wasteAmount` + `unit` and `packagesPurchased` (e.g. *"2 packs of 400 g → 200 g left over"*).
- If `detail` has **no items** (plan never generated a list, or no tracked ingredients):
  *"No waste data — set package sizes on ingredients and generate this plan's shopping list."*

### 12.5 ManageIngredientsView (NEW) + EditIngredientDialog (NEW)

The admin screen for setting standard package sizes.

- **Route:** add to [router/index.js](../frontend/src/router/index.js) — `path: '/ingredients'`,
  `name: 'ingredients'`, `meta.title: 'Manage Ingredients'`, lazy-loaded.
- **Entry point:** the bottom nav already has its 4 tabs ([AppBottomNav.vue:33-38](../frontend/src/components/layout/AppBottomNav.vue#L33-L38)) — do **not** add a 5th. Instead surface
  a **"Manage ingredients"** action in the Recipes view toolbar (via the `AppTopBar` `actions`
  slot) routing to `{ name: 'ingredients' }`. *(`ingredients` is not a top-level tab, so
  `AppTopBar` shows its back button automatically — no change needed there.)*
- **View:** uses `useIngredientStore` (`fetchIngredients`, `updateIngredient` already exist). List
  ingredients (optionally filter/search) with each row showing `displayName`, `category`, and the
  package size (e.g. *"400 g"* or a muted *"Not tracked"* when null). Tapping a row opens
  `EditIngredientDialog`.
- **EditIngredientDialog:** fields **Standard package size** (numeric, optional) and **Unit**
  (`VSelect` over `Units.All` values; reuse the unit option list already used by the recipe
  ingredient builder). A **"Don't track waste"** affordance clears both. On save, call
  `store.updateIngredient(id, { ...existing, standardPackageSize, standardUnit })`. The dialog must
  enforce the both-or-neither rule client-side (mirroring the validator) for a clean UX, while the
  API remains the source of truth.

### 12.6 SuggestionsPanel — elevate the reuse label

[SuggestionsPanel.vue](../frontend/src/components/SuggestionsPanel.vue) already shows a caption
*"Uses your leftover X & Y"* ([:66-69](../frontend/src/components/SuggestionsPanel.vue#L66-L69)).
Wrap that label in a `VChip` with a leaf/recycle icon (`mdi-leaf`) and a success/teal colour to make
the waste-reuse benefit prominent. **Keep the existing wording** (matches SPEC §6.2.4) — this is a
purely visual enhancement; no backend change (the suggestions endpoint already returns
`overlappingIngredients`).

### 12.7 User-Facing Copy

| UI Location | Copy |
|---|---|
| Home card title (existing) | **Food waste saved** |
| Home card body (data) | `summary.message` |
| Home card body (no data) | *Tracked after your first meal plan* |
| Detail panel header | **Food Waste — {N} fully used · {M} with leftovers** |
| Fully-used section | **Fully used ({N})** |
| Has-leftovers section | **Has leftovers ({M})** |
| Leftover detail | **{packages} packs of {pkg} {unit} → {waste} {unit} left over** |
| Detail no-data | *No waste data — set package sizes on ingredients and generate this plan's shopping list.* |
| Manage-ingredients title | **Manage Ingredients** |
| Package-size field | **Standard package size** |
| Unit field | **Unit** |
| Untracked row hint | **Not tracked** |
| Don't-track action | **Don't track waste** |
| Suggestions chip | **Uses your leftover {ingredient names}** |

---

## 13. Data Flow

```
Admin sets package sizes
  → ManageIngredientsView → EditIngredientDialog
    → PUT /api/v1/ingredients/{id}  { ..., standardPackageSize, standardUnit }
  → ingredient is now tracked (or untracked if cleared)

Shopping list generated / regenerated  (Phase 5 paths)
  → ShoppingListService writes ShoppingListItems (SaveChangesAsync)
    → FoodWasteService.CalculateAndPersistAsync(mealPlanId)
      → load plan recipes + ingredients (incl. StandardPackageSize/Unit)
      → per tracked ingredient: consolidate → convert to package unit
        → packages = ceil(total / pkg); waste = packages*pkg - total
        → isFullyUsed = waste == 0
      → delete existing FoodWasteLogs for plan → insert fresh (SaveChangesAsync)

HomeView mounts
  → foodWasteStore.fetchSummary()  → GET /food-waste/summary
  → existing card body shows summary.message (or zero-state)

Past plan tapped → MealPlanDetailView
  → foodWasteStore.fetchByMealPlan(id)  → GET /food-waste/meal-plan/{id}
  → render fully-used vs has-leftovers breakdown

Suggestions panel (Phase 4 API unchanged)
  → reuse label rendered as a leaf VChip
```

---

## 14. Testing

### 14.1 Backend Tests

```
Services/
  FoodWasteServiceTests.cs       Integration tests (Testcontainers DB)
  ShoppingListServiceTests.cs    (extend) generation now also writes FoodWasteLogs
Validators/
  IngredientValidatorTests.cs    (extend) both-or-neither package rules
Endpoints/
  FoodWasteEndpointsTests.cs     Integration via RecipeAppFactory
DTOs/
  MappingsTests.cs               (extend) FoodWasteLog + Ingredient package fields
```

**FoodWasteService:**
- Tracked ingredient, plan total = exact package multiple → `WasteAmount = 0`, `IsFullyUsed = true`.
- Tracked ingredient, partial use (600 g vs 400 g pkg) → `PackagesPurchased = 2`,
  `WasteAmount = 200`, `IsFullyUsed = false`.
- Reuse across 2 recipes raising the total to a whole package flips `IsFullyUsed` true (and stored
  `Amount` never mutated; HALF/REGULAR/DOUBLE multipliers applied).
- Untracked ingredient (no package size) → **no** `FoodWasteLog` row.
- Package/plan unit-dimension mismatch (pkg `g`, plan uses `pcs`) → skipped (no row).
- `kg`/`g` and `L`/`ml` consolidation feeds the package unit correctly (via `Units`).
- Package size **snapshotted**: editing the ingredient's package size afterwards doesn't change an
  existing log row until the list is regenerated.
- Recalculation replaces rows (no duplicates) — unique `(MealPlanId, IngredientId)` upheld.
- `GetSummaryAsync` counts + singular/plural message; zero-state message when no data.
- `GetByMealPlanAsync`: `null` for unknown plan; empty items for plan with no tracked ingredients;
  correct fully-used / with-leftovers split.
- Deleting a `MealPlan` cascades its `FoodWasteLog` rows.

**Units helper:** `Consolidate` parity with prior Phase-5 behaviour; `TryConvert` mass/volume
correctness and false for cross-dimension; `IsValid` accepts the metric set.

**IngredientValidator:** size-without-unit fails; unit-without-size fails; both-null passes;
both-set passes; non-positive size fails; invalid unit fails.

**Endpoints:** `GET /food-waste/summary` → `200` (incl. zero state); `GET /food-waste/meal-plan/{id}`
→ `404` unknown, `200` with structure; generating a list for a plan with tracked shared ingredients
creates rows (verify via the meal-plan endpoint); `PUT /ingredients/{id}` round-trips package fields.

### 14.2 Frontend Tests

```
stores/
  foodWaste.spec.js              actions + state (MSW)
views/
  HomeView.spec.js               (extend) card shows message when data; zero-state otherwise
  MealPlanDetailView.spec.js     (extend) breakdown sections + no-data state
  ManageIngredientsView.spec.js  list + edit flow
components/
  EditIngredientDialog.spec.js   both-or-neither enforcement; emits payload
  SuggestionsPanel.spec.js       (extend) reuse chip rendered
```

- `foodWaste` store: `fetchSummary` stores response / zero-count handled; `fetchByMealPlan` `404` →
  `detail = null` without error.
- `HomeView`: body shows `summary.message` when present, zero-state copy otherwise (card always
  rendered).
- `MealPlanDetailView`: fully-used vs has-leftovers counts/rows; leftover amount shown; no-data
  message.
- `ManageIngredientsView` / `EditIngredientDialog`: shows "Not tracked" for null; setting size
  without unit is blocked; clearing both untracks; save emits `{ standardPackageSize, standardUnit }`.
- `SuggestionsPanel`: reuse chip appears for suggestions with `overlappingIngredients`.

Run with the existing commands (`dotnet test`; `npm test` / `npm run test:coverage`). Backend
integration tests require Docker (Testcontainers PostgreSQL).

---

## 15. Out of Scope for Phase 6

- **"Waste avoided by reuse" counterfactual** — we report *actual* leftover per ingredient, not a
  modelled "what waste would have been without the other recipe" delta.
- **Aggregated waste *amount* on the home headline** — units differ across ingredients, so the
  headline is count-based; per-ingredient amounts live in the detail view.
- **Auto-suggesting package sizes** (e.g. via Claude) — admin sets them manually (Future
  Functionality: "Auto Categorisation via Claude" is the analogous deferred idea).
- **Cross-dimension coercion** (`pcs ↔ g` via typical weights) — mismatched units are left
  untracked, never approximated.
- **Cooking mode / `last_cooked_at` automation** → Phase 7.
- Any write endpoints on `/food-waste` — the log is owned by `ShoppingListService` triggering
  `FoodWasteService.CalculateAndPersistAsync`.

---

## 16. Definition of Done

- [ ] `Ingredient.StandardPackageSize` / `StandardUnit` columns, `FoodWasteLog` entity, DbContext
      config, and `AddFoodWasteTracking` migration applied (unique index verified).
- [ ] `Units` helper extracted; `ShoppingListService` refactored to use it with **all Phase 5 tests
      still green**.
- [ ] `FoodWasteService` implemented (amount-based calc, untracked-skip, unit-dimension guard,
      snapshotting, delete-then-insert, summary + per-plan queries) and registered.
- [ ] `ShoppingListService` calls `CalculateAndPersistAsync` at both generate/regenerate sites.
- [ ] Ingredient DTOs/mapping/validator extended (both-or-neither); `PUT /ingredients/{id}` persists
      package fields; `FoodWasteEndpoints` registered.
- [ ] Home card wired (always visible, message + zero-state); `MealPlanDetailView` breakdown;
      `ManageIngredientsView` + `EditIngredientDialog` + `ingredients` route + Recipes-view entry
      point; `SuggestionsPanel` reuse chip; `foodWaste` store.
- [ ] Backend + frontend tests added and passing.
- [ ] `CLAUDE.md` "Current phase" line + structure notes updated to Phase 6; `SPEC.md` §7 Phase 6
      checkboxes ticked, and §4.2 noted to include the new `Ingredients` package columns.
```
