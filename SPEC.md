# Recipe App — Development Specification

**Version:** 1.0  
**Date:** 2026-05-26  
**Status:** Draft

---

## Table of Contents

1. [Overview](#1-overview)
2. [Technology Stack](#2-technology-stack)
3. [Architecture](#3-architecture)
4. [Data Models](#4-data-models)
5. [API Design](#5-api-design)
6. [Feature Specifications](#6-feature-specifications)
7. [Development Phases](#7-development-phases)
8. [Testing](#8-testing)
9. [Future Functionality](#9-future-functionality)

---

## 1. Overview

A mobile-first web application for managing personal recipes, building meal plans, and generating smart shopping lists. The app emphasises convenience during cooking (clean step-by-step instruction views), meal planning efficiency (automatic shopping list generation with combined ingredient amounts), and food waste reduction (ingredient reuse suggestions and waste tracking).

### Core Feature Summary

| Feature | Description |
|---|---|
| Recipe Management | Store, view, edit, and scrape recipes with ingredient lists and step instructions |
| Meal Planning | Build a collection of recipes assigned to dates; one active plan at a time |
| Shopping List | Auto-generated, grouped, checkable list derived from the active meal plan |
| Food Waste Tracking | Tracks how much food waste has been avoided through ingredient reuse |

---

## 2. Technology Stack

### Frontend
| Concern | Choice | Notes |
|---|---|---|
| Framework | **Vue 3** (Composition API, `<script setup>` only) | Mobile-first SPA |
| State Management | **Pinia** | One store file per domain |
| Routing | **Vue Router 4** | Lazy-loaded views |
| HTTP Client | **Axios** | Pre-configured instance in `src/services/api.js` |
| UI Components | **Vuetify 3** | Primary component library |
| CSS Utilities | **Bootstrap 5** | Grid + utility classes only (no Bootstrap JS) |
| Build Tool | **Vite** | Fast dev server and builds |

### Backend
| Concern | Choice | Notes |
|---|---|---|
| Framework | **.NET 10 Web API** | Minimal API (endpoint groups, not MVC controllers) |
| ORM | **Entity Framework Core 10** | Code-first with migrations (Npgsql provider) |
| Validation | **FluentValidation** | One validator class per request DTO |
| AI Integration | **Anthropic Claude API** (claude-sonnet-4-6) | Recipe scraping and extraction |
| Web Scraping | **HtmlAgilityPack** or **AngleSharp** | Raw HTML retrieval before passing to Claude |
| Image Storage | **Local filesystem** | Configurable; served as static files at `/uploads/images/` |
| Mapping | **Manual extension methods** | `DTOs/Mappings.cs` — `ToResponse()` / `ToDetail()` / `ToListItem()` |

### Database
| Concern | Choice |
|---|---|
| Engine | **PostgreSQL 16+** |
| Migrations | EF Core Migrations |

### Infrastructure / Dev
| Concern | Choice |
|---|---|
| Containerisation | **Docker Compose** (API + PostgreSQL) |
| API Documentation | **Swagger / OpenAPI** (Swashbuckle) |
| Environment Config | `.env` / `appsettings.{Environment}.json` |

---

## 3. Architecture

```
┌─────────────────────────────────────────────┐
│              Browser (Mobile)               │
│           Vue 3 SPA (Vite/Pinia)            │
└──────────────────┬──────────────────────────┘
                   │ HTTPS / REST JSON
┌──────────────────▼──────────────────────────┐
│           .NET 10 Web API                   │
│  ┌──────────┐ ┌──────────┐ ┌─────────────┐ │
│  │ Recipes  │ │MealPlans │ │ Shopping    │ │
│  │ Module   │ │ Module   │ │ List Module │ │
│  └──────────┘ └──────────┘ └─────────────┘ │
│  ┌──────────────────────────────────────┐   │
│  │        Claude AI Service             │   │
│  │  (Recipe Scrape / Extract / Convert) │   │
│  └──────────────────────────────────────┘   │
│  ┌──────────────────────────────────────┐   │
│  │     Entity Framework Core (ORM)      │   │
│  └──────────────────────────────────────┘   │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              PostgreSQL 16                  │
└─────────────────────────────────────────────┘
```

### Key Design Decisions

- **REST API** — standard JSON endpoints; no GraphQL in v1 for simplicity.
- **Single-user** — no authentication layer in v1 (single household use). Authentication noted in Future Functionality.
- **Metric-only measurements** — all ingredient amounts stored in metric units (g, kg, ml, L, units/pcs). No imperial conversion in v1.
- **Portion multipliers** — stored as an enum: `HALF (0.5)`, `REGULAR (1.0)`, `DOUBLE (2.0)`. Applied at query/display time; base recipe quantities are always stored as-is.
- **Single active meal plan** — enforced at the API layer; creating a new plan marks the previous one as closed.

---

## 4. Data Models

### 4.1 Ingredients (Global Catalogue)

```
ingredients
  id               UUID  PK
  name             TEXT  NOT NULL UNIQUE  (normalised, lowercase e.g. "onion")
  display_name     TEXT  NOT NULL         (e.g. "Onion")
  category         TEXT  NOT NULL         (shopping group: PRODUCE, MEAT_SEAFOOD, DAIRY,
                                           CANNED, FROZEN, DRY_GOODS, BAKERY, CONDIMENTS,
                                           BEVERAGES, OTHER)
  default_unit     TEXT                   (e.g. "g", "ml", "pcs")
  created_at       TIMESTAMPTZ
```

### 4.2 Recipes

```
recipes
  id               UUID  PK
  name             TEXT  NOT NULL
  description      TEXT
  image_url        TEXT
  source_url       TEXT                   (if scraped from web)
  servings         INTEGER  DEFAULT 4     (base serving count)
  last_cooked_at   TIMESTAMPTZ            (updated when a meal plan containing this
                                           recipe is marked complete/active)
  created_at       TIMESTAMPTZ
  updated_at       TIMESTAMPTZ
```

### 4.3 Recipe Ingredients

```
recipe_ingredients
  id               UUID  PK
  recipe_id        UUID  FK → recipes
  ingredient_id    UUID  FK → ingredients
  amount           DECIMAL(10,3)  NOT NULL   (base portion amount)
  unit             TEXT  NOT NULL            (metric: g, kg, ml, L, pcs, tsp, tbsp)
  notes            TEXT                      (e.g. "finely chopped", "optional")
  display_order    INTEGER
```

### 4.4 Recipe Steps

```
recipe_steps
  id               UUID  PK
  recipe_id        UUID  FK → recipes
  step_number      INTEGER  NOT NULL
  instruction      TEXT  NOT NULL
```

### 4.5 Recipe Step Ingredients (Step ↔ Ingredient association)

```
recipe_step_ingredients
  step_id          UUID  FK → recipe_steps
  recipe_ingredient_id  UUID  FK → recipe_ingredients
  PRIMARY KEY (step_id, recipe_ingredient_id)
```

### 4.6 Meal Plans

```
meal_plans
  id               UUID  PK
  name             TEXT  NOT NULL
  is_active        BOOLEAN  DEFAULT FALSE
  created_at       TIMESTAMPTZ
  closed_at        TIMESTAMPTZ            (NULL while active)
```

> **Constraint:** Only one row may have `is_active = TRUE` at any time (enforced via partial unique index).

### 4.7 Meal Plan Recipes

```
meal_plan_recipes
  id               UUID  PK
  meal_plan_id     UUID  FK → meal_plans
  recipe_id        UUID  FK → recipes
  scheduled_date   DATE                   (nullable — not all meals have a date)
  portion_size     TEXT  DEFAULT 'REGULAR'  (HALF | REGULAR | DOUBLE)
  display_order    INTEGER                (derived from scheduled_date, then insertion order)
```

### 4.8 Shopping Lists

```
shopping_lists
  id               UUID  PK
  meal_plan_id     UUID  FK → meal_plans  UNIQUE
  generated_at     TIMESTAMPTZ
  updated_at       TIMESTAMPTZ
```

### 4.9 Shopping List Items

```
shopping_list_items
  id               UUID  PK
  shopping_list_id UUID  FK → shopping_lists
  ingredient_id    UUID  FK → ingredients  (NULL for custom items)
  custom_name      TEXT                   (for custom/non-recipe items)
  category         TEXT  NOT NULL         (mirrors ingredient.category or manual)
  amount           DECIMAL(10,3)          (combined total; NULL for custom items with no qty)
  unit             TEXT
  is_checked       BOOLEAN  DEFAULT FALSE
  is_custom        BOOLEAN  DEFAULT FALSE  (TRUE = user-added, not from a recipe)
  display_order    INTEGER
```

### 4.10 Food Waste Log

```
food_waste_log
  id                     UUID  PK
  meal_plan_id           UUID  FK → meal_plans
  ingredient_id          UUID  FK → ingredients
  total_amount_in_plan   DECIMAL(10,3)     (total used across all recipes in this plan)
  standard_package_size  DECIMAL(10,3)     (optional — for future waste calculation refinement)
  unit                   TEXT
  is_fully_used          BOOLEAN           (TRUE = no waste for this ingredient in this plan)
  waste_amount           DECIMAL(10,3)     (amount estimated as wasted)
  recorded_at            TIMESTAMPTZ
```

---

## 5. API Design

Base URL: `/api/v1`

### 5.1 Recipes

| Method | Endpoint | Description |
|---|---|---|
| GET | `/recipes` | List all recipes (supports filters) |
| GET | `/recipes/{id}` | Get single recipe with steps and ingredients |
| POST | `/recipes` | Create recipe manually |
| PUT | `/recipes/{id}` | Update recipe |
| DELETE | `/recipes/{id}` | Delete recipe |
| POST | `/recipes/scrape` | Scrape + extract recipe from URL via Claude AI |
| POST | `/recipes/{id}/image` | Upload recipe image |
| POST | `/recipes/{id}/cook` | Mark as cooked (updates `last_cooked_at`) |

**GET /recipes query params:**
- `search` — name partial match
- `ingredient` — filter by ingredient name (partial)
- `lastCookedBefore` — ISO date filter
- `excludeRecentDays` — integer; exclude recipes cooked within N days

### 5.2 Ingredients

| Method | Endpoint | Description |
|---|---|---|
| GET | `/ingredients` | List all ingredients |
| POST | `/ingredients` | Create ingredient |
| PUT | `/ingredients/{id}` | Update ingredient |
| GET | `/ingredients/categories` | Return available category enum values |

### 5.3 Meal Plans

| Method | Endpoint | Description |
|---|---|---|
| GET | `/meal-plans` | List all meal plans (paginated) |
| GET | `/meal-plans/active` | Get current active meal plan with recipes |
| GET | `/meal-plans/{id}` | Get specific meal plan |
| POST | `/meal-plans` | Create new meal plan (deactivates current active) |
| PUT | `/meal-plans/{id}` | Rename meal plan |
| DELETE | `/meal-plans/{id}` | Delete meal plan |
| POST | `/meal-plans/{id}/recipes` | Add recipe to meal plan |
| PUT | `/meal-plans/{id}/recipes/{mprId}` | Update portion size / scheduled date |
| DELETE | `/meal-plans/{id}/recipes/{mprId}` | Remove recipe from meal plan |
| GET | `/meal-plans/{id}/suggestions` | Get waste-reduction recipe suggestions |

### 5.4 Shopping Lists

| Method | Endpoint | Description |
|---|---|---|
| GET | `/shopping-lists/active` | Get shopping list for active meal plan |
| GET | `/shopping-lists/{id}` | Get specific shopping list |
| POST | `/shopping-lists/active/generate` | (Re)generate list from active meal plan |
| POST | `/shopping-lists/{id}/items` | Add custom item |
| PUT | `/shopping-lists/{id}/items/{itemId}` | Update item (check/uncheck, edit qty) |
| DELETE | `/shopping-lists/{id}/items/{itemId}` | Remove custom item |

### 5.5 Food Waste

| Method | Endpoint | Description |
|---|---|---|
| GET | `/food-waste/summary` | Aggregated waste saved stats |
| GET | `/food-waste/meal-plan/{id}` | Waste breakdown for a specific meal plan |

---

## 6. Feature Specifications

### 6.1 Recipe Management

#### 6.1.1 Recipe Display
- Recipe card on list view shows: image (or placeholder), name, description snippet, last cooked date.
- **Grayscale rule:** if `last_cooked_at` is within the past 7 days, the recipe image is rendered in CSS greyscale (`filter: grayscale(100%)`). This applies on both recipe list and meal plan selection views.
- Detail view shows full ingredient list (at selected portion multiplier) and numbered steps.
- Each step displays the instruction text plus a chips/badges row listing only the ingredients relevant to that step.
- Tapping an ingredient chip in a step highlights its entry in the ingredient list.

#### 6.1.2 Portion Adjustment
- Three options: **½ Portion**, **Regular**, **Double**.
- Multiplier is applied on the client side for display; the stored base amounts are never modified.
- When a recipe is viewed standalone the selector defaults to Regular.
- When viewing from within a meal plan, the selector reflects the portion size set in `meal_plan_recipes`.

#### 6.1.3 Recipe Scraping via Claude AI
- User provides a URL.
- Backend fetches raw HTML using HtmlAgilityPack/AngleSharp.
- Stripped text is passed to Claude API with a structured prompt requesting JSON output matching the recipe schema (name, description, servings, ingredients with metric amounts and units, ordered steps, step-ingredient mappings).
- Claude response is validated and mapped to internal DTOs.
- User is shown a **preview/confirmation screen** before saving, allowing edits.
- Ingredient names are normalised and matched against the global ingredient catalogue; new ingredients are created automatically with a suggested category that the user can confirm.
- `source_url` is stored on the recipe.

#### 6.1.4 Recipe Image
- Upload via multipart form; stored as a file with a generated name.
- Served back via a static file endpoint or CDN path stored in `image_url`.
- Max file size: 10 MB. Accepted formats: JPEG, PNG, WebP.

---

### 6.2 Meal Planning

#### 6.2.1 Active Meal Plan
- Only one meal plan can be `is_active = TRUE` at a time.
- Creating a new meal plan sets `is_active = FALSE` and records `closed_at` on the previous active plan.
- The home screen prominently displays the active meal plan if one exists: each recipe shown as a card with its scheduled date (or "Unscheduled"), portion size badge, and a tap-to-cook action.

#### 6.2.2 Building a Meal Plan
- Recipe browser within meal plan creation supports:
  - **Name search** — partial text match on recipe name.
  - **Ingredient filter** — filter recipes containing a given ingredient.
  - **Last cooked filter** — "not cooked recently" toggle to surface older recipes.
- Selecting a recipe adds it to the plan at Regular portion, no date.
- Added recipes can have:
  - **Date assigned** — a date picker; multiple recipes may share a date.
  - **Portion** changed via the three-way selector.
- The meal plan list self-sorts: recipes with dates sort ascending by date, undated recipes appear at the bottom.

#### 6.2.3 Recently Cooked Warning
- Any recipe cooked within the last 7 days renders its image in greyscale.
- Attempting to add such a recipe to a meal plan triggers a bottom-sheet confirmation:  
  *"You cooked [Recipe Name] in the last week — are you sure you want to include it?"*  
  Options: **Add Anyway** | **Cancel**

#### 6.2.4 Waste-Reduction Suggestions
- Accessible as a "Suggestions" section during meal plan building.
- Backend queries: for each ingredient in the current plan that has a fractional amount (i.e., `amount mod standard_whole_unit > 0`), find other recipes using that ingredient.
- Returns a ranked list of suggested recipes, sorted by number of overlapping partial ingredients.
- Displayed as cards with a label such as *"Uses your leftover onion & carrot"*.

#### 6.2.5 Past Meal Plans
- Accessible from the navigation as "Past Plans".
- Read-only view showing plan name, date range, recipes included, and waste summary.

---

### 6.3 Shopping List

#### 6.3.1 Generation Logic
1. Iterate all `meal_plan_recipes` for the active meal plan.
2. For each recipe apply the portion multiplier to all `recipe_ingredients`.
3. Aggregate by `ingredient_id`, summing amounts. If units differ, attempt unit conversion within the same dimension (e.g., ml → L); flag incompatible units for manual review.
4. Write aggregated rows to `shopping_list_items` linked to the meal plan's `shopping_list`.
5. Group items by `category` for display.

#### 6.3.2 Shopping List Display
- Items grouped under category headings: **Produce**, **Meat & Seafood**, **Dairy**, **Canned Goods**, **Frozen**, **Dry Goods & Pasta**, **Bakery**, **Condiments & Sauces**, **Beverages**, **Other**.
- Each item shows: ingredient name, combined amount + unit.
- Custom user-added items appear in their category with a distinct visual indicator (e.g. user icon badge).

#### 6.3.3 Checking Off Items
- Tap item to toggle checked state (persisted via `PUT /shopping-lists/{id}/items/{itemId}`).
- Checked items are **hidden by default**.
- A toggle at the top of the list: *"Show purchased (N)"* reveals checked items with a strikethrough style and a distinct background.

#### 6.3.4 Custom Items
- Floating action button on the shopping list: **+ Add Item**.
- User provides: name, quantity (optional), unit (optional), category.
- Stored as `is_custom = TRUE`; these items are **not** regenerated or removed when the shopping list is regenerated from the meal plan — only recipe-derived items are replaced.

#### 6.3.5 List Regeneration
- If meal plan recipes are modified after the list is generated, a banner appears: *"Your meal plan has changed — tap to update shopping list."*
- Regeneration replaces all non-custom items; custom items are preserved.

---

### 6.4 Food Waste Tracking

#### 6.4.1 Waste Definition
> An ingredient is considered **wasted** when the total amount used across all recipes in a meal plan is not a whole unit of that ingredient (i.e., implies a remainder after purchase), and no other recipe in the same plan uses the remaining portion.

#### 6.4.2 Calculation
- For simplicity in v1: when a shopping list is generated, identify ingredients where total plan amount is not a round purchase unit.
- Compare against other recipes in the same plan to determine if the remainder is consumed.
- Store result in `food_waste_log`.
- **"Waste Saved"** metric = number of ingredients where remainder was consumed because of another recipe in the same plan.

#### 6.4.3 Display
- A summary widget (home screen or dedicated page): **"You've saved N ingredient portions from waste across M meal plans."**
- Detail view per meal plan showing a per-ingredient breakdown.

---

## 7. Development Phases

Development is structured into 7 iterative phases. Each phase produces a working, deployable increment.

---

### Phase 1 — Project Foundation & Infrastructure
**Goal:** Get a running skeleton that developers can build on.

**Tasks:**
- [x] Initialise Git repository with monorepo structure (`/frontend`, `/backend`, `/docker`)
- [x] Create `docker-compose.yml` with PostgreSQL and .NET API services
- [x] Scaffold .NET 10 Web API project (minimal API pattern)
  - Configure EF Core with Npgsql provider
  - Configure Swagger/OpenAPI
  - Add `appsettings.Development.json` with DB connection string
  - Add health check endpoint `GET /health`
- [x] Scaffold Vue 3 project (Vite + Pinia + Vue Router + bootstrap + vuetify)
  - Establish mobile-first viewport and base layout shell (bottom nav, top bar)
  - Set up Axios instance with base URL from env
- [x] Design initial database schema; create EF Core entities and first migration
  - `ingredients`, `recipes`, `recipe_ingredients`, `recipe_steps`, `recipe_step_ingredients`
- [x] Set up CLAUDE.md with project conventions

**Deliverable:** Running app shell; database created; Swagger accessible; Vue app loads from Docker.

---

### Phase 2 — Recipe Management (Core CRUD)
**Goal:** Users can create, view, edit, and delete recipes manually.

**Tasks:**
- [x] **Backend:**
  - Implement `IngredientsController`: GET list, POST, PUT, GET categories
  - Implement `RecipesController`: GET list, GET by ID, POST, PUT, DELETE
  - DTO + manual mapping (extension methods in `DTOs/Mappings.cs`) for Recipe with nested steps and ingredients
  - Seed a small set of ingredient categories and a few sample ingredients
- [x] **Frontend:**
  - Recipe list page (cards with name, image placeholder, last cooked)
  - Recipe detail page — ingredient list, numbered steps
  - Recipe create/edit form
    - Step builder (add/reorder/delete steps)
    - Ingredient list builder (ingredient search/select, amount, unit)
    - Step-ingredient assignment (checkbox overlay per step)
  - Portion selector (½ / Regular / Double) on detail page — local multiplier only
  - Image upload on recipe form
- [x] Ingredient auto-complete backed by `GET /ingredients?search=`

**Deliverable:** Full recipe CRUD in the UI; portion adjustment works; images upload and display.

---

### Phase 3 — Recipe Scraping via Claude AI
**Goal:** Users can import a recipe by pasting a URL.

**Tasks:**
- [x] **Backend:**
  - Add Anthropic SDK (Claude API) to .NET project
  - `RecipeScrapeService`: fetch URL HTML → strip to text → prompt Claude → parse structured JSON response
  - Define Claude prompt template (structured output: recipe schema)
  - `POST /recipes/scrape` endpoint returning a preview DTO
  - Ingredient normalisation: match scraped names against catalogue, suggest new entries with auto-categorisation
  - Unit conversion: convert any imperial units detected by Claude into metric
- [x] **Frontend:**
  - "Import from URL" button on recipe list page
  - URL input bottom sheet → loading state → preview/edit screen
  - Preview screen shows editable version of scraped recipe before save
  - Confirm ingredient categories for any newly created ingredients

**Deliverable:** Paste a recipe URL; Claude extracts it; user confirms and saves.

---

### Phase 4 — Meal Planning
**Goal:** Users can build and manage meal plans; home screen shows active plan.

**Tasks:**
- [x] **Backend:**
  - `MealPlanEndpoints`: GET list, GET active, GET by ID, POST, PUT, DELETE, suggestions
  - `MealPlanRecipeEndpoints`: POST, PUT (portion/date), DELETE (within MealPlanEndpoints.cs)
  - Business logic: deactivate existing plan when new one is created (single SaveChanges)
  - `GET /meal-plans/{id}/suggestions` — ingredient overlap algorithm
  - `MealPlanService`, `MealPlanValidators`, `PortionSize` enum, EF migration `AddMealPlans`
- [x] **Frontend:**
  - Home screen: active meal plan widget (recipe cards sorted by date, greyscale rule)
  - New meal plan flow: `MealPlanBuilderView` (name entry → recipe browser → date/portion controls)
  - Recipe browser (`RecipeBrowser.vue`) with name search, recently-cooked greyscale and confirmation
  - `RecentlyCookedDialog.vue` — bottom-sheet confirmation
  - `SuggestionsPanel.vue` — waste-reduction suggestions with ingredient labels
  - `MealPlanView.vue` — active plan with per-recipe overflow menu (date, portion, remove)
  - `PastPlansView.vue` — read-only closed plans list
  - `MealPlanDetailView.vue` — read-only plan detail (sorted recipes)
  - `mealPlans.js` Pinia store; routes added

**Deliverable:** Full meal planning flow; active plan on home; suggestions surface; greyscale + warning on recent recipes.

---

### Phase 5 — Shopping List
**Goal:** Shopping list is auto-generated from the active meal plan and is functional for in-store use.

**Tasks:**
- [ ] **Backend:**
  - `ShoppingListsController`: GET active, GET by ID, POST generate, item CRUD
  - Aggregation logic: combine recipe ingredients by `ingredient_id`, apply portion multipliers, attempt unit consolidation (ml/L, g/kg)
  - Flag items for unit mismatch review
  - Custom item management (preserve on regeneration)
  - Stale-list detection (return flag if meal plan was modified after list was generated)
- [ ] **Frontend:**
  - Shopping list page accessible from bottom nav and from active meal plan
  - Items grouped by category with sticky category headers
  - Tap to check off item; checked items hidden by default
  - "Show purchased (N)" toggle reveals checked items in strikethrough style
  - Custom item FAB (+): name, amount, unit, category
  - Stale-list banner with "Regenerate" action
  - Clear visual distinction for custom items

**Deliverable:** Working shopping list; in-store use flow (check off, show/hide); custom items.

---

### Phase 6 — Food Waste Tracking *(Deferred — see §9 Future Functionality)*

> **Status: Deferred.** Meaningful waste tracking requires per-ingredient standard package sizes
> (an ingredient-admin capability) which introduces scope that warrants a dedicated future
> iteration. The data model (`food_waste_log`, §4.10) and detailed specification
> (`specs/phase-6-food-waste-tracking.md`) are preserved for that iteration. Phase 7 is the next
> active development phase.

**Goal:** App tracks and surfaces food waste savings.

**Tasks:** *(deferred to future iteration)*
- [ ] **Ingredient admin** — allow users to set a standard package size per ingredient to enable waste tracking
- [ ] **Backend:**
  - Waste calculation service: runs on shopping list generation; amount-based using package sizes
  - Persist to `food_waste_log`
  - `GET /food-waste/summary` — lifetime stats
  - `GET /food-waste/meal-plan/{id}` — per-plan breakdown
- [ ] **Frontend:**
  - Manage Ingredients screen for setting standard package sizes
  - Home screen widget: waste saved summary stat
  - Waste detail page per meal plan (accessible from Past Plans)
  - Highlight suggestions that "rescue" a leftover ingredient

**Deliverable:** Waste stats appear after each meal plan's shopping list is generated.

---

### Phase 7 — Polish, UX & Cooking Mode
**Goal:** App is pleasant and efficient to use while cooking.

**Tasks:**
- [ ] **Cooking mode** — full-screen clean view of a recipe's steps:
  - Large typography, minimal chrome
  - Swipe or tap-next between steps
  - Current step's ingredients highlighted
  - Screen keep-awake (Wake Lock API where supported)
- [ ] "Mark as Cooked" action accessible from meal plan recipe card — updates `last_cooked_at`
- [ ] Empty states and loading skeletons throughout
- [ ] Error handling and retry patterns on API failures
- [ ] PWA manifest + service worker for offline viewing of current recipe (read-only cache)
- [ ] Responsive polish: test on iPhone SE, iPhone 15, and common Android sizes
- [ ] Accessibility audit: focus order, ARIA labels, contrast ratios
- [ ] Docker production build with Nginx serving Vue dist and proxying API

**Deliverable:** Production-quality, polished mobile experience.

---

## 8. Testing

The project includes automated tests for both the backend (.NET) and frontend (Vue) layers.

---

### 8.1 Backend Tests

**Location:** `backend/RecipeApp.Tests/`

**Stack:**
| Concern | Choice |
|---|---|
| Test framework | xunit.v3 |
| Assertions | FluentAssertions |
| Integration host | Microsoft.AspNetCore.Mvc.Testing |
| Database isolation | Testcontainers.PostgreSql (real PostgreSQL container per test run) |
| Validation helpers | FluentValidation.TestHelper (bundled in FluentValidation 11+) |

**Test structure:**
```
backend/RecipeApp.Tests/
├── Infrastructure/
│   ├── RecipeAppFactory.cs       WebApplicationFactory override; wires Testcontainers DB
│   └── DatabaseFixture.cs        Shared container fixture; runs one PG instance per collection
├── Helpers/
│   └── TestDataBuilder.cs        Fluent builders for seeding test entities
├── Validators/
│   ├── IngredientValidatorTests.cs
│   └── RecipeValidatorTests.cs
├── DTOs/
│   └── MappingsTests.cs
├── Services/
│   ├── RecipeServiceTests.cs
│   └── ImageServiceTests.cs
└── Endpoints/
    ├── HealthEndpointTests.cs
    ├── IngredientsEndpointTests.cs
    └── RecipesEndpointTests.cs
```

**Running backend tests:**
```bash
cd backend
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj
```

> **Note:** Integration tests spin up a real PostgreSQL container via Testcontainers. Docker must be running.

---

### 8.2 Frontend Tests

**Location:** `frontend/src/` (co-located `.spec.js` files) and `frontend/src/test/` (shared setup)

**Stack:**
| Concern | Choice |
|---|---|
| Test runner | Vitest 4 |
| DOM environment | jsdom |
| Component utilities | @vue/test-utils |
| HTTP mocking | msw 2 (Mock Service Worker) |
| Coverage | @vitest/coverage-v8 |

**Test structure:**
```
frontend/src/
├── test/
│   └── setup.js                   Global setup: ResizeObserver stub, MSW server lifecycle
├── services/
│   └── api.spec.js                Axios instance configuration tests
├── stores/
│   ├── recipes.spec.js            Pinia recipes store actions and state
│   └── ingredients.spec.js        Pinia ingredients store actions and state
├── components/layout/
│   └── AppBottomNav.spec.js       Bottom navigation component tests
└── views/
    ├── RecipesView.spec.js        Recipe list page tests
    ├── RecipeDetailView.spec.js   Recipe detail page tests
    └── RecipeFormView.spec.js     Recipe create/edit form tests
```

**Running frontend tests:**
```bash
cd frontend
npm test                   # run once
npm run test:watch         # watch mode
npm run test:coverage      # with coverage report
```

---

## 9. Future Functionality

These items are **out of scope for v1** but represent natural extensions.

| Feature | Description |
|---|---|
| **User Authentication** | Multi-user support (household accounts) with JWT auth. Until then the app is single-user with no login. |
| **Recipe from Image** | Upload a photo of a handwritten or printed recipe; pass to Claude Vision to extract structured recipe data. |
| **Nutritional Information** | Attach macro/calorie data to ingredients; display per-recipe and per-meal-plan totals. |
| **Recipe Collections / Tags** | Tag recipes (e.g. "weeknight", "vegetarian", "batch cook") and filter by tag. |
| **Recipe Ratings & Notes** | Per-recipe user notes and a simple star rating stored per cook. |
| **Scaling to Custom Servings** | Beyond half/regular/double, allow arbitrary serving count input. |
| **Unit System Toggle** | Optional display in imperial for users who prefer it (stored metric, displayed converted). |
| **Push Notifications** | Remind users to update the shopping list or check their meal plan the day before a planned meal. |
| **Barcode Scanning** | Scan a grocery barcode to check off a shopping list item. |
| **Shared Meal Plans** | Share a meal plan (and its shopping list) with another user or household. |
| **Auto Categorisation via Claude** | When a new ingredient is created manually, call Claude to suggest its shopping category. |
| **Pantry / Inventory Tracking** | Track what ingredients are already on hand and automatically deduct them from the shopping list. |
| **Meal Plan Templates** | Save a meal plan as a reusable template to quickly spin up a similar plan next time. |
| **Smart Shopping List Order** | Allow the user to define a custom store aisle order that the shopping list respects. |
| **Food Waste Tracking** | Per-ingredient standard package sizes (admin capability) drive amount-based waste calculation on shopping list generation. Surfaces fully-used vs leftover counts on the home screen and per-plan detail. Detailed specification in `specs/phase-6-food-waste-tracking.md`. |
| **Waste Benchmark Comparisons** | Compare personal food waste stats against an average household benchmark. |
| **Recipe Version History** | Track changes to a recipe over time; revert to a previous version. |
| **Offline-First Mode** | Full offline support with background sync, not just read-only PWA cache. |

---

*End of Specification*
