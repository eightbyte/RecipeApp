# Phase 4 — Meal Planning

**Version:** 1.0  
**Date:** 2026-05-31  
**Status:** Complete  
**Depends on:** Phase 2 (Recipe CRUD) complete; Phase 3 (Recipe Scraping) complete

---

## 1. Overview

Phase 4 introduces **meal plans** — named collections of recipes, optionally assigned to dates, each with a portion size. Exactly one meal plan is *active* at a time; creating a new plan closes the previous active one. The home screen surfaces the active plan, and a dedicated meal-plan flow lets the user build a plan by browsing recipes (with name/ingredient search), assigning dates and portions, and seeing waste-reduction suggestions. Recipes cooked within the last 7 days are visually de-emphasised (greyscale) and trigger a confirmation before being added.

This phase covers the meal-plan and meal-plan-recipe data layer plus its UI. **Shopping list generation (Phase 5) and food-waste persistence (Phase 6) are out of scope** — see §10. The `closed_at` lifecycle and the suggestions algorithm are delivered here.

---

## 2. Deliverable

> Create a named meal plan → browse and add recipes (with recently-cooked greyscale + confirmation) → assign dates and portions → see waste-reduction suggestions → the active plan appears on the home screen → past plans are viewable read-only.

---

## 3. Data Model

Two new tables are added. Naming follows the existing EF Core convention in this project: **PascalCase table and column names** (EF/Npgsql defaults — *not* snake_case, despite the schema sketch in `SPEC.md`). New `DbSet`s are added to `AppDbContext`.

### 3.1 MealPlan (`MealPlans` table)

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `Name` | `string` NOT NULL | Plan name (e.g. "Week of 2 June") |
| `IsActive` | `bool` DEFAULT `false` | Only one row may be `true` at a time (partial unique index) |
| `CreatedAt` | `DateTime` (TIMESTAMPTZ) | `DateTime.UtcNow`; DB default `NOW()` |
| `ClosedAt` | `DateTime?` (TIMESTAMPTZ) | `NULL` while active; set when deactivated |

Navigation: `ICollection<MealPlanRecipe> Recipes`.

### 3.2 MealPlanRecipe (`MealPlanRecipes` table)

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `MealPlanId` | `Guid` FK → `MealPlans` | Cascade delete |
| `RecipeId` | `Guid` FK → `Recipes` | Restrict delete (cannot delete a recipe that is in a plan) |
| `ScheduledDate` | `DateOnly?` | Nullable — undated meals allowed; stored as `date` column |
| `PortionSize` | `string` DEFAULT `'REGULAR'` | `HALF` \| `REGULAR` \| `DOUBLE` |
| `DisplayOrder` | `int` | Insertion order; final sort is derived (dated first, ascending, then undated by this value) |

Navigation: `MealPlan MealPlan`, `Recipe Recipe`.

> **Note on `ScheduledDate`:** use `DateOnly` (maps to PostgreSQL `date`). The SPEC stores a calendar date, not an instant, so this avoids any timezone ambiguity. All true *timestamps* (`CreatedAt`, `ClosedAt`) remain `TIMESTAMPTZ` / `DateTime.UtcNow` per project convention.

### 3.3 Model files

```
backend/RecipeApp.API/Models/
├── MealPlan.cs
└── MealPlanRecipe.cs
```

Add a navigation collection to the existing `Recipe` model is **not required** (the relationship can be configured FK-only). Do not add it unless a query needs it — keep `Recipe` unchanged to avoid widening its surface.

### 3.4 PortionSize enum/constants

Create `Enums/PortionSize.cs`, mirroring the existing `IngredientCategory` constants pattern:

```csharp
namespace RecipeApp.API.Enums;

/// <summary>
/// Portion multipliers applied at display/query time. Base recipe amounts are never modified.
/// </summary>
public static class PortionSize
{
    public const string Half    = "HALF";
    public const string Regular = "REGULAR";
    public const string Double  = "DOUBLE";

    public static readonly IReadOnlyList<string> All = [Half, Regular, Double];

    public static bool IsValid(string value) => All.Contains(value);

    /// <summary>Numeric multiplier for a portion size. Defaults to 1.0 for unknown values.</summary>
    public static decimal Multiplier(string value) => value switch
    {
        Half    => 0.5m,
        Double  => 2.0m,
        _       => 1.0m,
    };
}
```

> `Multiplier` is provided now because the suggestions and (later) shopping-list layers will need it. Portion scaling is **never** applied to stored `Amount` values — only at the query/DTO layer.

---

## 4. AppDbContext Configuration

Add to `OnModelCreating`:

```csharp
// ── MealPlan ──────────────────────────────────────────────────────────────
modelBuilder.Entity<MealPlan>(e =>
{
    e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");

    // Enforce a single active plan via a partial unique index.
    e.HasIndex(p => p.IsActive)
        .IsUnique()
        .HasFilter("\"IsActive\" = true");
});

// ── MealPlanRecipe ────────────────────────────────────────────────────────
modelBuilder.Entity<MealPlanRecipe>(e =>
{
    e.Property(mpr => mpr.PortionSize).HasDefaultValue(PortionSize.Regular);

    e.HasOne(mpr => mpr.MealPlan)
        .WithMany(p => p.Recipes)
        .HasForeignKey(mpr => mpr.MealPlanId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(mpr => mpr.Recipe)
        .WithMany()                       // no back-navigation on Recipe
        .HasForeignKey(mpr => mpr.RecipeId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

Add the `DbSet`s:

```csharp
public DbSet<MealPlan> MealPlans => Set<MealPlan>();
public DbSet<MealPlanRecipe> MealPlanRecipes => Set<MealPlanRecipe>();
```

> **Partial unique index filter:** the filter string must reference the quoted PascalCase column name (`"IsActive" = true`) because the project uses default EF column naming. The index guarantees at most one active plan even under concurrent writes; the service layer still deactivates the prior plan explicitly (§6.2) so the constraint is a safety net, not the primary mechanism.

### 4.1 Migration

```bash
cd backend/RecipeApp.API
dotnet ef migrations add AddMealPlans --output-dir Data/Migrations
```

Verify the generated migration creates both tables, the partial unique index on `MealPlans (IsActive) WHERE "IsActive" = true`, the `DateOnly` → `date` mapping for `ScheduledDate`, and the restrict/cascade FK behaviours. The dev server auto-migrates on startup (`Program.cs`).

---

## 5. DTOs

**Directory:** `DTOs/MealPlans/`

```
DTOs/MealPlans/
├── MealPlanListItemResponse.cs
├── MealPlanDetailResponse.cs
├── MealPlanRecipeResponse.cs
├── CreateMealPlanRequest.cs
├── UpdateMealPlanRequest.cs
├── AddMealPlanRecipeRequest.cs
├── UpdateMealPlanRecipeRequest.cs
└── SuggestionResponse.cs
```

```csharp
namespace RecipeApp.API.DTOs.MealPlans;

public record MealPlanListItemResponse(
    Guid Id,
    string Name,
    bool IsActive,
    int RecipeCount,
    DateOnly? FirstScheduledDate,   // earliest scheduled date, null if none scheduled
    DateOnly? LastScheduledDate,    // latest scheduled date, null if none scheduled
    DateTime CreatedAt,
    DateTime? ClosedAt
);

public record MealPlanDetailResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    List<MealPlanRecipeResponse> Recipes   // pre-sorted: dated ascending, then undated by DisplayOrder
);

public record MealPlanRecipeResponse(
    Guid Id,                    // the MealPlanRecipe id (mprId)
    Guid RecipeId,
    string RecipeName,
    string? RecipeImageUrl,
    DateTime? RecipeLastCookedAt,   // lets the client apply the greyscale rule
    DateOnly? ScheduledDate,
    string PortionSize,
    int DisplayOrder
);

public record CreateMealPlanRequest(
    string Name
);

public record UpdateMealPlanRequest(
    string Name
);

public record AddMealPlanRecipeRequest(
    Guid RecipeId,
    DateOnly? ScheduledDate,
    string? PortionSize           // null → defaults to REGULAR
);

public record UpdateMealPlanRecipeRequest(
    DateOnly? ScheduledDate,
    string PortionSize
);

public record SuggestionResponse(
    Guid RecipeId,
    string RecipeName,
    string? RecipeImageUrl,
    DateTime? RecipeLastCookedAt,
    int OverlapCount,                  // number of shared ingredients with the plan
    List<string> OverlappingIngredients  // display names, for the "Uses your leftover …" label
);
```

> `MealPlanRecipeResponse` deliberately carries a flattened subset of recipe fields (name, image, last-cooked) rather than nesting a full `RecipeDetailResponse`. This matches the lightweight card the home and meal-plan views render and avoids loading every recipe's ingredients/steps for a plan listing.

---

## 6. Backend Services

**File:** `Services/MealPlanService.cs`, registered as scoped in `Program.cs` (`builder.Services.AddScoped<MealPlanService>();`). Follows the constructor-injection style of `RecipeService(AppDbContext db)`.

### 6.1 Queries

| Method | Returns | Notes |
|---|---|---|
| `GetListAsync()` | `List<MealPlanListItemResponse>` | All plans, ordered: active first, then by `CreatedAt` descending |
| `GetActiveAsync()` | `MealPlanDetailResponse?` | The single active plan with its recipes, or `null` if none |
| `GetByIdAsync(Guid id)` | `MealPlanDetailResponse?` | Specific plan with recipes |
| `GetSuggestionsAsync(Guid id)` | `List<SuggestionResponse>` | Waste-reduction suggestions (§6.4) |

All read queries use `.AsNoTracking()`. When loading recipes for a plan, `Include(p => p.Recipes).ThenInclude(mpr => mpr.Recipe)` so the flattened recipe fields are available. Recipes within a plan are sorted in the service before mapping (dated ascending by `ScheduledDate`, then undated by `DisplayOrder`).

### 6.2 Mutations — Meal Plans

| Method | Returns | Behaviour |
|---|---|---|
| `CreateAsync(CreateMealPlanRequest)` | `MealPlanDetailResponse` | **Deactivate** any currently active plan (set `IsActive = false`, `ClosedAt = DateTime.UtcNow`), then create the new plan with `IsActive = true`, `ClosedAt = null`. Wrap both writes in a single `SaveChangesAsync` so the partial unique index is never violated. |
| `UpdateAsync(Guid id, UpdateMealPlanRequest)` | `MealPlanDetailResponse?` | Rename only. Returns `null` if not found. Does not change active state. |
| `DeleteAsync(Guid id)` | `bool` | Hard delete (cascade removes `MealPlanRecipes`). Returns `false` if not found. Deleting the active plan simply leaves no active plan (no auto-promotion of another plan). |

> **Deactivation ordering:** within `CreateAsync`, load the existing active plan (if any), mutate it, add the new plan to the change tracker, then call `SaveChangesAsync` once. EF orders the UPDATE (clearing the old `IsActive`) before the INSERT within the transaction, satisfying the partial unique index. If a race is still possible in your environment, perform the deactivation `UPDATE` and the `INSERT` in an explicit transaction.

### 6.3 Mutations — Meal Plan Recipes

| Method | Returns | Behaviour |
|---|---|---|
| `AddRecipeAsync(Guid planId, AddMealPlanRecipeRequest)` | `MealPlanRecipeResponse?` | Returns `null` if the plan or recipe doesn't exist. Validates `PortionSize` (defaulting `null` → `REGULAR`). `DisplayOrder` = current max in the plan + 1. The **same recipe may appear multiple times** in a plan (e.g. cooked on two dates) — no uniqueness constraint on `(MealPlanId, RecipeId)`. |
| `UpdateRecipeAsync(Guid planId, Guid mprId, UpdateMealPlanRecipeRequest)` | `MealPlanRecipeResponse?` | Update `ScheduledDate` and/or `PortionSize`. Returns `null` if the `MealPlanRecipe` is not found or doesn't belong to `planId`. |
| `RemoveRecipeAsync(Guid planId, Guid mprId)` | `bool` | Remove a recipe from the plan. Returns `false` if not found / wrong plan. |

> **`last_cooked_at` is not touched here.** Marking a recipe cooked remains the existing `POST /recipes/{id}/cook` action (Phase 2). The "tap-to-cook" action on a meal-plan card calls that existing endpoint. Phase 4 does not auto-update `LastCookedAt` from meal-plan state.

### 6.4 Suggestions Algorithm (`GetSuggestionsAsync`)

**Goal:** surface recipes *not already in the plan* that reuse ingredients already required by the plan, ranked by overlap — supporting the "Uses your leftover onion & carrot" label (SPEC §6.2.4).

**Phase 4 simplification:** the SPEC's ideal definition keys off *fractional/partial* amounts versus a standard package size. Package sizes live in `food_waste_log.standard_package_size`, which is **not introduced until Phase 6**. For Phase 4, rank by **count of shared ingredients** instead. This is a deliberate, documented simplification; the algorithm is refined in Phase 6 once package sizes exist.

**Steps:**
1. Load the plan and the distinct set of `IngredientId`s used across all its `MealPlanRecipe → Recipe → RecipeIngredients`. If the plan has no recipes, return an empty list.
2. Collect the set of `RecipeId`s already in the plan (to exclude them).
3. Query recipes (excluding those already in the plan) that use **at least one** of the plan's ingredients.
4. For each candidate, compute `OverlapCount` = number of distinct plan-ingredients it shares, and gather up to (say) 3 overlapping ingredient display names for the label.
5. Order by `OverlapCount` descending, then `Name` ascending. Cap the result at a reasonable limit (e.g. 10).

Keep this as a single EF query where practical (group/join on `RecipeIngredients`), falling back to in-memory aggregation of a focused result set if the LINQ translation gets unwieldy. Document the chosen approach inline.

---

## 7. Mappings

Add to `DTOs/Mappings.cs` (static extension methods, pure entity-in/DTO-out, consistent with the existing file):

```csharp
// ── MealPlan ────────────────────────────────────────────────────────────────

public static MealPlanListItemResponse ToListItem(this MealPlan p) => new(
    p.Id,
    p.Name,
    p.IsActive,
    p.Recipes.Count,
    p.Recipes.Where(r => r.ScheduledDate.HasValue).Min(r => (DateOnly?)r.ScheduledDate),
    p.Recipes.Where(r => r.ScheduledDate.HasValue).Max(r => (DateOnly?)r.ScheduledDate),
    p.CreatedAt,
    p.ClosedAt
);

public static MealPlanDetailResponse ToDetail(this MealPlan p) => new(
    p.Id,
    p.Name,
    p.IsActive,
    p.CreatedAt,
    p.ClosedAt,
    p.Recipes
        .OrderBy(r => r.ScheduledDate.HasValue ? 0 : 1)   // dated first
        .ThenBy(r => r.ScheduledDate)
        .ThenBy(r => r.DisplayOrder)
        .Select(ToResponse)
        .ToList()
);

public static MealPlanRecipeResponse ToResponse(this MealPlanRecipe mpr) => new(
    mpr.Id,
    mpr.RecipeId,
    mpr.Recipe.Name,
    mpr.Recipe.ImageUrl,
    mpr.Recipe.LastCookedAt,
    mpr.ScheduledDate,
    mpr.PortionSize,
    mpr.DisplayOrder
);
```

> `ImageUrl` is returned as stored (relative path). The frontend store normalises it to an absolute URL via the existing `assetUrl` helper, exactly as the recipes store already does.

---

## 8. Validators

**File:** `Validators/MealPlanValidators.cs` (auto-discovered via `AddValidatorsFromAssemblyContaining<Program>()`).

**`CreateMealPlanRequestValidator`:**
- `Name` not empty, max 200 characters.

**`UpdateMealPlanRequestValidator`:**
- `Name` not empty, max 200 characters.

**`AddMealPlanRecipeRequestValidator`:**
- `RecipeId` not empty.
- `PortionSize`, when provided (non-null), must be in `PortionSize.All`.

**`UpdateMealPlanRecipeRequestValidator`:**
- `PortionSize` not empty and in `PortionSize.All`.

> Existence of `RecipeId`/`MealPlanId` is checked in the service (returns `null` → endpoint returns `404`), not in validators, matching how the recipe endpoints handle missing entities.

---

## 9. Endpoints

**File:** `Endpoints/MealPlanEndpoints.cs`, registered in `Program.cs` as `api.MapMealPlanEndpoints();`. Both meal-plan and meal-plan-recipe routes live in this one file under a single `/meal-plans` group, tagged `Meal Plans`.

### 9.1 Meal Plans

| Method | Route | Success | Errors |
|---|---|---|---|
| `GET` | `/meal-plans` | `200` `List<MealPlanListItemResponse>` | — |
| `GET` | `/meal-plans/active` | `200` `MealPlanDetailResponse` | `404` if no active plan |
| `GET` | `/meal-plans/{id:guid}` | `200` `MealPlanDetailResponse` | `404` |
| `POST` | `/meal-plans` | `201` `MealPlanDetailResponse`, `Location: /api/v1/meal-plans/{id}` | `400` validation |
| `PUT` | `/meal-plans/{id:guid}` | `200` `MealPlanDetailResponse` | `400`, `404` |
| `DELETE` | `/meal-plans/{id:guid}` | `204` | `404` |
| `GET` | `/meal-plans/{id:guid}/suggestions` | `200` `List<SuggestionResponse>` | `404` if plan not found |

### 9.2 Meal Plan Recipes

| Method | Route | Success | Errors |
|---|---|---|---|
| `POST` | `/meal-plans/{id:guid}/recipes` | `201` `MealPlanRecipeResponse` | `400`, `404` (plan or recipe missing) |
| `PUT` | `/meal-plans/{id:guid}/recipes/{mprId:guid}` | `200` `MealPlanRecipeResponse` | `400`, `404` |
| `DELETE` | `/meal-plans/{id:guid}/recipes/{mprId:guid}` | `204` | `404` |

**Validation pattern** (matches `RecipesEndpoints`):
```csharp
var validation = await validator.ValidateAsync(request);
if (!validation.IsValid)
    return Results.ValidationProblem(validation.ToDictionary());
```

Each handler injects `MealPlanService` and the relevant `IValidator<T>`. Add `.WithSummary(...)`/`.WithDescription(...)` to every endpoint, consistent with the existing endpoint files.

---

## 10. Out of Scope for Phase 4

Deferred to later phases (do **not** build in Phase 4):

- **Shopping list generation** (`shopping_lists`, `shopping_list_items`, aggregation, stale-list banner) → **Phase 5**.
- **Food-waste persistence** (`food_waste_log`, `standard_package_size`, waste summary endpoints) → **Phase 6**. Consequently the suggestions algorithm uses ingredient-overlap counting rather than partial-amount/package-size logic (§6.4).
- **Cooking mode** and `last_cooked_at` automation → **Phase 7**. The "tap-to-cook" card action reuses the existing `POST /recipes/{id}/cook` endpoint.
- Drag-and-drop manual reordering of meals (sort is derived from date + insertion order).
- Authentication / multi-user plans.

---

## 11. Frontend

### 11.1 New & Modified Files

```
frontend/src/
├── stores/
│   └── mealPlans.js                 NEW — Pinia store for meal plans
├── components/
│   ├── RecipeBrowser.vue            NEW — searchable recipe picker used during plan building
│   ├── RecentlyCookedDialog.vue     NEW — bottom-sheet confirmation for recently-cooked recipes
│   └── SuggestionsPanel.vue         NEW — waste-reduction suggestions list
├── views/
│   ├── HomeView.vue                 MODIFIED — wire active plan from API
│   ├── MealPlanView.vue             MODIFIED — wire active plan + entry points
│   ├── MealPlanBuilderView.vue      NEW — create/edit a plan (name, browse, dates, portions)
│   ├── MealPlanDetailView.vue       NEW — read-only plan detail
│   └── PastPlansView.vue            NEW — read-only list of past plans
└── router/index.js                  MODIFIED — add routes
```

### 11.2 Router Changes

Add to `router/index.js` (lazy-loaded, with `meta.title`, matching existing entries):

```js
{
  path: '/meal-plan/new',
  name: 'meal-plan-create',
  component: () => import('@/views/MealPlanBuilderView.vue'),
  meta: { title: 'New Meal Plan' },
},
{
  path: '/meal-plan/past',
  name: 'meal-plan-past',
  component: () => import('@/views/PastPlansView.vue'),
  meta: { title: 'Past Plans' },
},
{
  path: '/meal-plan/:id',
  name: 'meal-plan-detail',
  component: () => import('@/views/MealPlanDetailView.vue'),
  meta: { title: 'Meal Plan' },
  props: true,
},
```

> Keep the existing `/meal-plan` (`name: 'meal-plan'`) route as the active-plan landing page. Order the `:id` route after the static `/new` and `/past` paths so they aren't captured as ids.

### 11.3 Pinia Store — `stores/mealPlans.js`

`useMealPlanStore`, `<script setup>`-style composition store, using the pre-configured `api` instance and the `assetUrl` helper for image normalisation (mirroring `stores/recipes.js`).

**State:**
```js
const plans         = ref([])     // MealPlanListItemResponse[]
const activePlan    = ref(null)   // MealPlanDetailResponse | null
const currentPlan   = ref(null)   // MealPlanDetailResponse | null (detail/builder view)
const suggestions   = ref([])     // SuggestionResponse[]
const loading       = ref(false)
const error         = ref(null)
```

**Actions (one per API call):**

| Action | Calls |
|---|---|
| `fetchPlans()` | `GET /meal-plans` |
| `fetchActivePlan()` | `GET /meal-plans/active` (treat `404` as "no active plan" → `activePlan = null`, not an error) |
| `fetchPlan(id)` | `GET /meal-plans/{id}` → `currentPlan` |
| `createPlan(name)` | `POST /meal-plans` → sets `activePlan`, unshifts into `plans`; returns new id |
| `renamePlan(id, name)` | `PUT /meal-plans/{id}` |
| `deletePlan(id)` | `DELETE /meal-plans/{id}` |
| `addRecipe(planId, payload)` | `POST /meal-plans/{planId}/recipes` |
| `updatePlanRecipe(planId, mprId, payload)` | `PUT /meal-plans/{planId}/recipes/{mprId}` |
| `removePlanRecipe(planId, mprId)` | `DELETE /meal-plans/{planId}/recipes/{mprId}` |
| `fetchSuggestions(planId)` | `GET /meal-plans/{planId}/suggestions` → `suggestions` |

- Normalise `recipeImageUrl` on every plan-recipe and suggestion via `assetUrl`.
- After `addRecipe`/`updatePlanRecipe`/`removePlanRecipe`, re-fetch or patch `currentPlan`/`activePlan` so the derived sort order stays correct (simplest: re-fetch the affected plan).
- Reuse the recipes store's `isRecentlyCooked(recipe, days = 7)` helper concept for the greyscale rule; expose an equivalent here or import it, but keep one source of truth — prefer importing from the recipes store.

### 11.4 HomeView.vue (modify)

- On mount, call `mealPlanStore.fetchActivePlan()`.
- Replace the `activePlan = ref(null)` placeholder with the store's `activePlan`.
- Render meal cards from `activePlan.recipes` (already sorted by the API). Apply the **greyscale rule**: add the `grayscale` CSS class to the recipe image when `recipeLastCookedAt` is within 7 days.
- The chef-hat "cook" button calls the recipes store `markCooked(recipeId)` (existing `POST /recipes/{id}/cook`), then refreshes the active plan so the greyscale updates.
- Keep the "No active meal plan" empty state; its CTA routes to `{ name: 'meal-plan-create' }`.

### 11.5 MealPlanView.vue (modify)

- The active-plan landing page. On mount, `fetchActivePlan()`.
- "New meal plan" CTA → `{ name: 'meal-plan-create' }`.
- "Add recipe" on an existing active plan → `{ name: 'meal-plan-create' }` in edit mode for the active plan (or open the `RecipeBrowser` inline — choose one and keep it consistent; inline browser is preferred so the user stays on the active plan).
- "View past meal plans" → `{ name: 'meal-plan-past' }`.
- Apply the greyscale rule to recipe images as in HomeView.
- Each meal card's overflow menu offers: change date, change portion, remove from plan (calling the corresponding store actions).

### 11.6 MealPlanBuilderView.vue (new)

The create/build flow. Responsibilities:
1. **Name entry** — `v-text-field`; "Create plan" calls `createPlan(name)`, which becomes the active plan.
2. **Recipe browser** — embed `RecipeBrowser` to search and add recipes.
3. **Per-recipe controls** — for each added recipe: a date picker (`v-date-picker` / `VDateInput`) for `ScheduledDate` and the three-way portion selector (½ / Regular / Double). Changes call `updatePlanRecipe`.
4. **Suggestions** — embed `SuggestionsPanel`, refreshed after each add/remove.
5. **Done** — navigates to `{ name: 'meal-plan' }`.

### 11.7 RecipeBrowser.vue (new)

A reusable recipe picker.
- Uses the recipes store: search by **name** (`fetchRecipes({ search })`) and by **ingredient** (`fetchRecipes({ ingredient })`); a "Not cooked recently" toggle adds `excludeRecentDays: 7`.
- Renders recipe cards. Apply the **greyscale** class to images of recipes cooked within 7 days (`recipesStore.isRecentlyCooked`).
- Tapping a recipe:
  - If recently cooked → open `RecentlyCookedDialog` first.
  - Otherwise → emit `add` (or call `addRecipe` directly) with `{ recipeId, scheduledDate: null, portionSize: 'REGULAR' }`.

### 11.8 RecentlyCookedDialog.vue (new)

- A `VBottomSheet` confirmation.
- Copy: *"You cooked **{recipeName}** in the last week — are you sure you want to include it?"*
- Actions: **Add Anyway** (emits `confirm`) | **Cancel** (emits `cancel`, dismisses).

### 11.9 SuggestionsPanel.vue (new)

- Renders `suggestions` as cards.
- Each card shows the recipe name/image and a label built from `overlappingIngredients`, e.g. *"Uses your leftover onion & carrot"* (join names with "&"/commas; fall back to *"Uses ingredients already in your plan"* if the list is empty).
- Tapping a suggestion adds it to the plan (same recently-cooked guard as the browser).
- Empty state when there are no suggestions.

### 11.10 MealPlanDetailView.vue (new)

- Read-only view of a plan by id (`props: true`). On mount, `fetchPlan(id)`.
- Shows plan name, active/closed status, date range, and the sorted recipe list (dated first ascending, then undated).
- No editing controls (editing happens via the active-plan flow). Phase 6 will add the per-plan waste summary here.

### 11.11 PastPlansView.vue (new)

- On mount, `fetchPlans()`.
- Lists **non-active** plans (read-only), newest first, showing name, date range (`firstScheduledDate`–`lastScheduledDate`), recipe count, and `closedAt`.
- Each row → `{ name: 'meal-plan-detail', params: { id } }`.

### 11.12 User-Facing Copy

| UI Location | Copy |
|---|---|
| Home active-plan heading | **This Week's Plan** |
| New plan CTA | **New meal plan** |
| Builder name field label | **Plan name** |
| Create plan button | **Create plan** |
| Recipe browser search placeholder | **Search recipes** |
| Not-cooked-recently toggle | **Hide recently cooked** |
| Recently-cooked dialog body | *You cooked **{name}** in the last week — are you sure you want to include it?* |
| Recently-cooked confirm / cancel | **Add Anyway** / **Cancel** |
| Suggestions heading | **Uses up your leftovers** |
| Suggestion label | *Uses your leftover {ingredients}* |
| Portion selector options | **½ Portion** / **Regular** / **Double** |
| Past plans link | **View past meal plans** |

---

## 12. Data Flow

```
Create plan
  → MealPlanBuilderView calls createPlan(name)
    → POST /api/v1/meal-plans
      → MealPlanService deactivates current active plan (IsActive=false, ClosedAt=now)
      → creates new plan (IsActive=true), single SaveChanges
      → returns MealPlanDetailResponse (201)
  → store sets activePlan

Add recipe (with recently-cooked guard)
  → RecipeBrowser: user taps a recipe
    → if recently cooked → RecentlyCookedDialog → Add Anyway
    → addRecipe(planId, { recipeId, scheduledDate, portionSize })
      → POST /api/v1/meal-plans/{id}/recipes  (201 MealPlanRecipeResponse)
  → store re-fetches the plan (keeps derived sort) + fetchSuggestions(planId)

Adjust date / portion
  → updatePlanRecipe(planId, mprId, { scheduledDate, portionSize })
    → PUT /api/v1/meal-plans/{id}/recipes/{mprId}  (200)

Home screen
  → fetchActivePlan() → GET /api/v1/meal-plans/active
  → renders sorted cards; greyscale where lastCookedAt within 7 days
  → tap-to-cook → POST /api/v1/recipes/{id}/cook → refresh active plan
```

---

## 13. Testing

### 13.1 Backend Tests

Add to `backend/RecipeApp.Tests/`:

```
Services/
  MealPlanServiceTests.cs        Unit/integration tests against the Testcontainers DB
Validators/
  MealPlanValidatorTests.cs      FluentValidation.TestHelper
Endpoints/
  MealPlanEndpointsTests.cs      Integration via RecipeAppFactory
DTOs/
  MappingsTests.cs               (extend) meal-plan mapping + sort-order assertions
```

**Service / behaviour tests:**
- Creating a plan deactivates the previously active plan (`IsActive=false`, `ClosedAt` set) and the new plan is active.
- At most one active plan exists after multiple creates (verify the partial unique index holds).
- `GetActiveAsync` returns the active plan with recipes; returns `null` when none active.
- Adding the same recipe twice is allowed; `DisplayOrder` increments.
- `UpdateRecipeAsync` / `RemoveRecipeAsync` reject a `mprId` belonging to a different plan (`null` / `false`).
- Deleting a plan cascades its `MealPlanRecipes`; deleting a recipe that is in a plan is **restricted**.
- Detail sort order: dated recipes ascending first, then undated by `DisplayOrder`.
- Suggestions: returns recipes sharing ingredients with the plan, excludes recipes already in the plan, ranked by overlap count; empty plan → empty list.

**Validator tests:**
- `CreateMealPlanRequestValidator`: empty/over-long name fails.
- `AddMealPlanRecipeRequestValidator`: empty `RecipeId` fails; invalid `PortionSize` fails; null `PortionSize` passes.
- `UpdateMealPlanRecipeRequestValidator`: invalid `PortionSize` fails.

**Endpoint tests (RecipeAppFactory):**
- `GET /meal-plans/active` → `404` when none, `200` when present.
- `POST /meal-plans` → `201` with `Location` header.
- `POST /meal-plans/{id}/recipes` with unknown recipe → `404`; with unknown plan → `404`.
- `PUT`/`DELETE` recipe with wrong plan id → `404`.
- `DELETE /meal-plans/{id}` → `204`, then `GET` → `404`.
- `GET /meal-plans/{id}/suggestions` → `200` list.

### 13.2 Frontend Tests

```
stores/
  mealPlans.spec.js              Pinia store actions + state (MSW)
components/
  RecipeBrowser.spec.js
  RecentlyCookedDialog.spec.js
  SuggestionsPanel.spec.js
views/
  MealPlanBuilderView.spec.js
  MealPlanDetailView.spec.js
  PastPlansView.spec.js
  HomeView.spec.js               (add) renders active plan; greyscale class applied
```

**Store tests (MSW):**
- `fetchActivePlan`: stores response; `404` → `activePlan = null` without setting `error`.
- `createPlan`: posts name, sets `activePlan`, returns id.
- `addRecipe` / `updatePlanRecipe` / `removePlanRecipe`: call correct endpoints and refresh the plan.
- `fetchSuggestions`: stores list; image URLs normalised via `assetUrl`.

**Component / view tests:**
- `RecipeBrowser`: search input drives `fetchRecipes`; recently-cooked recipe opens `RecentlyCookedDialog` instead of adding directly; toggle adds `excludeRecentDays`.
- `RecentlyCookedDialog`: renders the recipe name; **Add Anyway** emits `confirm`, **Cancel** emits `cancel`.
- `SuggestionsPanel`: renders the "Uses your leftover …" label from `overlappingIngredients`; empty state when none.
- `HomeView`: renders cards from `activePlan`; applies `grayscale` class when `recipeLastCookedAt` is within 7 days; empty state when no active plan.

Run with the existing commands (`dotnet test …` for backend, `npm test` / `npm run test:coverage` for frontend).

---

## 14. Definition of Done

- [x] `MealPlan` and `MealPlanRecipe` entities, `PortionSize` enum, DbContext config, and `AddMealPlans` migration applied (partial unique index verified).
- [x] `MealPlanService`, DTOs, mappings, validators, and `MealPlanEndpoints` implemented and registered in `Program.cs`.
- [x] Single-active-plan invariant enforced (service + index); deactivation sets `ClosedAt`.
- [x] Suggestions endpoint returns overlap-ranked recipes excluding those already in the plan.
- [x] Frontend store, builder/detail/past-plan views, recipe browser, recently-cooked dialog, and suggestions panel implemented; routes added.
- [x] Home + meal-plan views show the active plan with the greyscale rule and tap-to-cook.
- [x] Backend and frontend tests added and passing; `CLAUDE.md` "Current phase" line and structure notes updated to reflect Phase 4.
```