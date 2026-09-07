# RecipeApp — Testing Suite Specification

**Status:** Draft for review  
**Phase coverage:** Phase 2 (Recipe CRUD, Ingredient management, Image upload)  
**Written:** 2026-05-30

---

## 1. Goals and Philosophy

This spec defines a pragmatic testing suite for RecipeApp. The priorities in order:

1. **Catch regressions** — protect the CRUD operations and validator logic that are already working
2. **Document intent** — tests serve as executable specs for business rules (unit validation, filter logic, mapping)
3. **Confidence at the boundaries** — integration tests own the request→persistence→response path; unit tests own isolated logic

We are **not** aiming for 100% coverage. Lines inside scaffolded Vuetify components, EF migration designer files, and the seeder data definitions are not worth testing. The focus is on code that contains decisions.

---

## 2. Test Layers

```
┌──────────────────────────────────────────────────┐
│  E2E (optional, deferred to Phase 4+)            │
├──────────────────────────────────────────────────┤
│  Frontend — Component / Integration Tests        │  Vitest + Vue Test Utils
├──────────────────────────────────────────────────┤
│  Frontend — Unit Tests (stores, helpers)         │  Vitest
├──────────────────────────────────────────────────┤
│  Backend — Integration Tests (API + DB)          │  xUnit + WebApplicationFactory
├──────────────────────────────────────────────────┤
│  Backend — Unit Tests (validators, mapping)      │  xUnit + FluentValidation.TestHelper
└──────────────────────────────────────────────────┘
```

---

## 3. Backend Testing

### 3.1 Tooling

| Package | Purpose | Version |
|---|---|---|
| `xunit.v3` | Test runner and assertions | 3.2.2 |
| `FluentAssertions` | Readable assertion syntax | 8.10.0 |
| `FluentValidation.TestHelper` | Validator-specific assertions | 11.12.0 ¹ |
| `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory` for integration tests | 10.0.8 |
| `Testcontainers.PostgreSql` | Spin up a real PostgreSQL container per test run | 4.12.0 |

> ¹ Must match the production major version. Production uses `FluentValidation.AspNetCore 11.3.1`; using a different major version would cause build errors because validator classes implement 11.x interfaces.

**Project location:** `backend/RecipeApp.Tests/RecipeApp.Tests.csproj`  
Reference the API project; do not share the production project file.

### 3.2 Unit Tests — Validators

File: `backend/RecipeApp.Tests/Validators/`

These run in-process with no I/O. Use `FluentValidation.TestHelper` (`TestValidate(...).ShouldHaveValidationErrorFor(...)`).

#### CreateIngredientValidator

| Test | Input | Expected |
|---|---|---|
| Valid ingredient | `{ Name: "flour", DisplayName: "Plain Flour", Category: "DRY_GOODS" }` | No errors |
| Name empty | `{ Name: "" }` | Error on `Name` |
| Name not lowercase | `{ Name: "Flour" }` | Error on `Name` |
| Name with leading whitespace | `{ Name: " flour" }` | Error on `Name` |
| Name exceeds 200 chars | 201-char string | Error on `Name` |
| DisplayName empty | `{ DisplayName: "" }` | Error on `DisplayName` |
| DisplayName exceeds 200 chars | 201-char string | Error on `DisplayName` |
| Category invalid | `{ Category: "JUNK" }` | Error on `Category` |
| Category valid (each value) | All 10 `IngredientCategory` constants | No errors |
| DefaultUnit exceeds 20 chars | 21-char string | Error on `DefaultUnit` |
| DefaultUnit null | `{ DefaultUnit: null }` | No errors (optional) |

#### UpdateIngredientValidator

| Test | Input | Expected |
|---|---|---|
| Valid update | `{ DisplayName: "Plain Flour", Category: "DRY_GOODS" }` | No errors |
| DisplayName empty | `{ DisplayName: "" }` | Error on `DisplayName` |
| Category invalid | `{ Category: "INVALID" }` | Error on `Category` |

#### RecipeIngredientRequestValidator

| Test | Input | Expected |
|---|---|---|
| Valid ingredient | `{ IngredientId: guid, Amount: 100, Unit: "g" }` | No errors |
| Amount zero | `{ Amount: 0 }` | Error on `Amount` |
| Amount negative | `{ Amount: -1 }` | Error on `Amount` |
| Unit invalid | `{ Unit: "oz" }` | Error on `Unit` |
| Unit valid (all 7) | `g`, `kg`, `ml`, `L`, `pcs`, `tsp`, `tbsp` | No errors |
| Notes exceeds 500 chars | 501-char string | Error on `Notes` |
| Notes null | `{ Notes: null }` | No errors (optional) |
| IngredientId empty guid | `Guid.Empty` | Error on `IngredientId` |

#### RecipeStepRequestValidator

| Test | Input | Expected |
|---|---|---|
| Valid step | `{ StepNumber: 1, Instruction: "Boil water" }` | No errors |
| StepNumber zero | `{ StepNumber: 0 }` | Error on `StepNumber` |
| StepNumber negative | `{ StepNumber: -1 }` | Error on `StepNumber` |
| Instruction empty | `{ Instruction: "" }` | Error on `Instruction` |
| Instruction exceeds 2000 chars | 2001-char string | Error on `Instruction` |

#### CreateRecipeValidator (cross-field rules)

| Test | Input | Expected |
|---|---|---|
| Valid minimal recipe | 1 ingredient, 1 step, valid servings | No errors |
| Name empty | `{ Name: "" }` | Error on `Name` |
| Name exceeds 200 chars | 201-char string | Error on `Name` |
| Description exceeds 2000 chars | 2001-char string | Error on `Description` |
| Servings zero | `{ Servings: 0 }` | Error on `Servings` |
| Servings 101 | `{ Servings: 101 }` | Error on `Servings` |
| Servings boundary 1 and 100 | `{ Servings: 1 }`, `{ Servings: 100 }` | No errors |
| No ingredients | `{ Ingredients: [] }` | Error on `Ingredients` |
| Step references valid index | `IngredientIndexes: [0]` with 1 ingredient | No errors |
| Step references out-of-bounds index | `IngredientIndexes: [1]` with 1 ingredient | Error on step |
| Step references negative index | `IngredientIndexes: [-1]` | Error on step |
| Step with empty ingredient indexes | `IngredientIndexes: []` | No errors (steps don't require ingredients) |

#### UpdateRecipeValidator

Same rule set as `CreateRecipeValidator` — minimal smoke tests sufficient; the cross-field index logic is shared code and fully exercised above.

| Test | Input | Expected |
|---|---|---|
| Valid minimal update | 1 ingredient, 1 step, valid name and servings | No errors |
| Name empty | `{ Name: "" }` | Error on `Name` |
| Servings zero | `{ Servings: 0 }` | Error on `Servings` |
| No ingredients | `{ Ingredients: [] }` | Error on `Ingredients` |
| Step references out-of-bounds index | `IngredientIndexes: [1]` with 1 ingredient | Error on step |

### 3.3 Unit Tests — Mappings

File: `backend/RecipeApp.Tests/DTOs/MappingsTests.cs`

These test the static extension methods in `DTOs/Mappings.cs`. Build entity instances directly; no database required.

| Test | Method | Assertion |
|---|---|---|
| Basic recipe maps to list item | `recipe.ToListItem()` | All scalar fields match; IngredientCount equals ingredient list length |
| IngredientCount with no ingredients | `recipe.ToListItem()` (empty Ingredients) | `IngredientCount == 0` |
| LastCookedAt null passes through | `recipe.ToListItem()` where LastCookedAt is null | Response.LastCookedAt is null |
| Ingredients ordered by DisplayOrder | `recipe.ToDetail()` with unsorted ingredients | Ingredients in response are sorted by DisplayOrder ascending |
| Steps ordered by StepNumber | `recipe.ToDetail()` with unsorted steps | Steps in response are sorted by StepNumber ascending |
| StepIngredient IDs mapped correctly | `step.ToResponse()` with 2 step-ingredients | RecipeIngredientIds contains both ingredient IDs |
| Ingredient maps to response | `ingredient.ToResponse()` | All fields match |
| RecipeIngredient maps to response | `ri.ToResponse()` | Displays IngredientDisplayName and Category from the Ingredient navigation |

### 3.4 Unit Tests — ImageService

File: `backend/RecipeApp.Tests/Services/ImageServiceTests.cs`

Test the validation logic only (no real disk I/O needed for validation path).

| Test | Scenario | Expected |
|---|---|---|
| Valid JPEG file | `.jpg`, 5 MB | `Validate()` returns success |
| Valid PNG file | `.png`, 1 MB | Success |
| Valid WebP file | `.webp`, 2 MB | Success |
| Invalid extension | `.gif` | Returns error result |
| File too large | 11 MB (above default 10 MB) | Returns error result |
| Exactly at size limit | 10 MB | Success (boundary) |
| Mismatched content-type | `.jpg` file with `image/gif` content-type | Returns error result |

### 3.5 Integration Tests — Setup

File: `backend/RecipeApp.Tests/Infrastructure/`

#### Test Database Strategy

Use **Testcontainers** to spin up a real PostgreSQL 16 container. This ensures:
- EF migrations are tested against a real database engine
- Unique constraint, cascade delete, and default value behaviour is verified exactly
- No mocking of the ORM or database layer

```csharp
// Shared fixture across all integration tests in the collection
public class DatabaseFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; }
    public string ConnectionString => Container.GetConnectionString();

    // Runs EF migrations on startup; truncates tables between tests
}
```

One container per test session (via `ICollectionFixture`). Truncate tables between individual tests using `TRUNCATE ... RESTART IDENTITY CASCADE` — do not recreate the schema each time.

#### Custom WebApplicationFactory

```csharp
public class RecipeAppFactory : WebApplicationFactory<Program>
{
    // Override DbContext connection string to use the test container
    // Disable the DataSeeder (or provide a controlled seed method)
    // Keep all other DI registrations intact
}
```

The `HttpClient` produced by the factory is used for all endpoint integration tests.

### 3.6 Integration Tests — RecipeService

File: `backend/RecipeApp.Tests/Services/RecipeServiceIntegrationTests.cs`

These hit the real database through the service layer (no HTTP).

#### GetListAsync

| Test | Setup | Assertion |
|---|---|---|
| Returns all recipes | 3 recipes seeded | List count == 3 |
| Search by name (partial, case-insensitive) | Recipe named "Spaghetti Bolognese" | Search "spag" returns that recipe |
| Search no match | No matching recipe | Empty list |
| Filter by ingredient name | Recipe contains "garlic" | Returns only recipes with garlic |
| Exclude recently cooked | Recipe cooked 3 days ago, excludeRecentDays=7 | Not returned |
| Include if cooked just outside window | Recipe cooked 8 days ago, excludeRecentDays=7 | Returned |
| Filter by lastCookedBefore | Recipe cooked 2026-01-01, filter 2026-06-01 | Returned |
| Null LastCookedAt with excludeRecentDays | Recipe never cooked | Returned |

#### GetByIdAsync

| Test | Scenario | Assertion |
|---|---|---|
| Existing recipe | Recipe with 3 ingredients, 2 steps | Returns full detail with all nested data |
| Ingredients ordered | Ingredients with DisplayOrder 3, 1, 2 | Returned in order 1, 2, 3 |
| Steps ordered | Steps with StepNumber 2, 1 | Returned in order 1, 2 |
| Not found | Non-existent GUID | Returns null |

#### CreateAsync

| Test | Scenario | Assertion |
|---|---|---|
| Valid recipe | Full CreateRecipeRequest | Returns RecipeDetailResponse; row exists in DB |
| Ingredients persisted | 3 ingredients in request | 3 RecipeIngredients in DB |
| Steps persisted | 2 steps in request | 2 RecipeSteps in DB |
| Step-ingredient links | Step references ingredient index 0 | RecipeStepIngredient row created |
| Timestamps set | On creation | CreatedAt and UpdatedAt both set to UTC |
| Invalid IngredientId | Non-existent ingredient GUID | Throws `DbUpdateException` (FK violation from `SaveChangesAsync`) |

#### UpdateAsync

| Test | Scenario | Assertion |
|---|---|---|
| Replace ingredients | Original: 2 ingredients; Update: 1 ingredient | DB has 1 RecipeIngredient after update |
| Replace steps | Original: 3 steps; Update: 2 steps | DB has 2 RecipeSteps after update |
| UpdatedAt refreshed | After update | UpdatedAt > original UpdatedAt |
| Not found | Non-existent ID | Returns false / null |
| Step-ingredient remapping | New step references new ingredient | Correct links created |

#### DeleteAsync

| Test | Scenario | Assertion |
|---|---|---|
| Deletes recipe | Existing recipe | Row removed from DB |
| Cascades to ingredients | Recipe with 2 RecipeIngredients | RecipeIngredients also removed |
| Cascades to steps | Recipe with steps | RecipeSteps also removed |
| Not found | Non-existent ID | Returns false (no exception) |

#### MarkCookedAsync

| Test | Scenario | Assertion |
|---|---|---|
| Sets LastCookedAt | Recipe with null LastCookedAt | LastCookedAt set to UTC now (±5s) |
| Updates UpdatedAt | Any call | UpdatedAt refreshed |
| Not found | Non-existent ID | Returns false |

### 3.7 Integration Tests — Endpoints (HTTP)

File: `backend/RecipeApp.Tests/Endpoints/`

Use the `RecipeAppFactory` HTTP client. Assert HTTP status codes, response body shape, and database side effects where relevant.

#### HealthEndpoints

| Test | Request | Expected Status | Body assertion |
|---|---|---|---|
| Liveness always returns 200 | `GET /health` | 200 | — |
| Readiness with connected DB | `GET /health/ready` | 200 | — |

#### IngredientsEndpoints

| Test | Request | Expected Status | Assertion |
|---|---|---|---|
| List all | `GET /api/v1/ingredients` | 200 | Array of IngredientResponse |
| List with search | `GET /api/v1/ingredients?search=fl` | 200 | Only matching ingredients |
| List empty search | `GET /api/v1/ingredients?search=` | 200 | All ingredients |
| Get by ID | `GET /api/v1/ingredients/{id}` | 200 | Correct ingredient |
| Get not found | `GET /api/v1/ingredients/{unknownId}` | 404 | — |
| Get categories | `GET /api/v1/ingredients/categories` | 200 | Array of all 10 category strings |
| Create valid | `POST /api/v1/ingredients` with valid body | 201 | Created ingredient in response |
| Create duplicate name | POST same name twice | 409 | Error message in body |
| Create invalid body | POST with empty Name | 422 | Validation error details |
| Update valid | `PUT /api/v1/ingredients/{id}` | 200 | Updated ingredient in response |
| Update not found | `PUT /api/v1/ingredients/{unknownId}` | 404 | — |
| Update invalid body | PUT with invalid Category | 422 | — |

#### RecipesEndpoints

| Test | Request | Expected Status | Assertion |
|---|---|---|---|
| List all | `GET /api/v1/recipes` | 200 | Array of RecipeListItemResponse |
| List with name search | `?search=bolognese` | 200 | Filtered results |
| List with ingredient filter | `?ingredient=garlic` | 200 | Only recipes containing garlic |
| List with excludeRecentDays | `?excludeRecentDays=7` | 200 | Excludes recently-cooked recipes |
| Get by ID | `GET /api/v1/recipes/{id}` | 200 | Full RecipeDetailResponse |
| Get not found | `GET /api/v1/recipes/{unknownId}` | 404 | — |
| Create valid | `POST /api/v1/recipes` with full body | 201 | Created recipe in response body |
| Create invalid (no ingredients) | POST with empty ingredients array | 422 | Validation error |
| Create invalid step index | POST with out-of-bounds IngredientIndex | 422 | Validation error |
| Update valid | `PUT /api/v1/recipes/{id}` | 200 | Updated recipe |
| Update not found | `PUT /api/v1/recipes/{unknownId}` | 404 | — |
| Delete valid | `DELETE /api/v1/recipes/{id}` | 204 | Subsequent GET returns 404 |
| Delete not found | `DELETE /api/v1/recipes/{unknownId}` | 404 | — |
| Upload image valid | `POST /api/v1/recipes/{id}/image` multipart | 200 | imageUrl in response |
| Upload image too large | File > 10 MB | 400 | Error message |
| Upload image bad type | `.gif` file | 400 | Error message |
| Mark cooked | `POST /api/v1/recipes/{id}/cook` | 200 | lastCookedAt set in response |
| Mark cooked not found | `POST /api/v1/recipes/{unknownId}/cook` | 404 | — |

---

## 4. Frontend Testing

### 4.1 Tooling

| Package | Purpose | Version |
|---|---|---|
| `vitest` | Test runner (Vite-native, matches dev config) | 4.1.7 |
| `@vue/test-utils` | Vue component mounting and interaction | 2.4.10 |
| `@vitest/coverage-v8` | Coverage reports | 4.1.7 |
| `msw` (Mock Service Worker) | API mocking at the network layer for integration-style tests | 2.14.6 |

**Config:** Add `test` block to existing `vite.config.js`. No separate config file needed.

```js
// vite.config.js addition
test: {
  environment: 'jsdom',
  globals: true,
  setupFiles: ['./src/test/setup.js'],
}
```

**Test file convention:** Co-located with source, `*.spec.js` suffix.

### 4.2 Unit Tests — Pinia Stores

File: `frontend/src/stores/recipes.spec.js`, `frontend/src/stores/ingredients.spec.js`

Use `setActivePinia(createPinia())` before each test. Mock `api.js` using `vi.mock('@/services/api.js')`.

#### recipes store

| Test | Action | Mock | Assertion |
|---|---|---|---|
| fetchRecipes populates state | `fetchRecipes()` | API returns 2 recipes | `store.recipes` has 2 items |
| fetchRecipes sets loading flag | `fetchRecipes()` | Delayed response | `loading` true during, false after |
| fetchRecipes sets error on failure | `fetchRecipes()` | API throws | `store.error` is set; `store.recipes` unchanged |
| fetchRecipe populates currentRecipe | `fetchRecipe(id)` | API returns recipe | `store.currentRecipe` matches |
| createRecipe calls POST | `createRecipe(payload)` | API returns created | `api.post` called with correct path/body |
| updateRecipe calls PUT | `updateRecipe(id, payload)` | API returns updated | `api.put` called with `recipes/${id}` |
| deleteRecipe calls DELETE | `deleteRecipe(id)` | API returns 204 | `api.delete` called |
| markCooked calls POST cook endpoint | `markCooked(id)` | API returns updated | `api.post` called with `recipes/${id}/cook` |
| isRecentlyCooked — within 7 days | `lastCookedAt` = 3 days ago | — | Returns true |
| isRecentlyCooked — outside 7 days | `lastCookedAt` = 8 days ago | — | Returns false |
| isRecentlyCooked — never cooked | `lastCookedAt` = null | — | Returns false |
| isRecentlyCooked — custom window | `lastCookedAt` = 3 days ago, `days=2` | — | Returns false |
| fetchRecipes normalises relative imageUrl | `fetchRecipes()` returns recipe with `imageUrl: '/uploads/images/foo.jpg'` | — | `store.recipes[0].imageUrl` equals `${apiBaseUrl}/uploads/images/foo.jpg` |
| fetchRecipes leaves absolute imageUrl unchanged | API returns recipe with `imageUrl: 'http://cdn.example.com/img.jpg'` | — | `store.recipes[0].imageUrl` unchanged |
| fetchRecipes leaves null imageUrl as empty string | API returns recipe with `imageUrl: null` | — | `store.recipes[0].imageUrl` is `''` (see `assetUrl` in `api.spec.js`) |

#### ingredients store

| Test | Action | Mock | Assertion |
|---|---|---|---|
| fetchIngredients populates state | `fetchIngredients()` | API returns list | `store.ingredients` populated |
| fetchIngredients with search | `fetchIngredients('fl')` | API | `api.get` called with `?search=fl` |
| fetchCategories caches result | `fetchCategories()` called twice | API called once | Second call skips API |
| fetchCategories fetches if empty | First call | API returns categories | `store.categories` populated |
| createIngredient calls POST | `createIngredient(payload)` | API success | `api.post` called with correct body |
| updateIngredient calls PUT | `updateIngredient(id, payload)` | API success | `api.put` called with `ingredients/${id}` |

### 4.3 Unit Tests — API Service

File: `frontend/src/services/api.spec.js`

| Test | Scenario | Assertion |
|---|---|---|
| assetUrl with relative path | `/uploads/images/foo.jpg` | Returns `${VITE_API_BASE_URL_root}/uploads/images/foo.jpg` |
| assetUrl with http URL | `http://example.com/image.jpg` | Returned unchanged |
| assetUrl with null | `null` | Returns `''` (falsy input short-circuits to empty string) |
| Error interceptor — response error | Axios error with `response.data.message` | Rejects with `{ status, message, original }` |
| Error interceptor — network error | Axios error without response | Rejects with message from `error.message` |

### 4.4 Component Tests

File: `frontend/src/components/layout/*.spec.js`, `frontend/src/views/*.spec.js`

Mount components with `@vue/test-utils`. Stub Vuetify and Vue Router as needed, or use a lightweight test Vuetify instance.

#### AppBottomNav

| Test | Scenario | Assertion |
|---|---|---|
| Renders 4 nav items | Mount | 4 navigation links present |
| Navigates to recipes | Click recipes tab | Router push called with `{ name: 'recipes' }` |
| Navigates to home | Click home tab | Router push called with `{ name: 'home' }` |

#### RecipesView

| Test | Scenario | Assertion |
|---|---|---|
| Shows loading state | `store.loading = true` | Loading indicator visible |
| Shows recipe list | `store.recipes` has 2 items | 2 recipe cards rendered |
| Shows empty state | `store.recipes = []` | Empty-state message visible |
| Search input debounced | Type in search box | `fetchRecipes` called with search param |
| Navigate to detail | Click recipe card | Card `:to` prop equals `{ name: 'recipe-detail', params: { id: recipe.id } }` |
| Navigate to create | Click new recipe FAB button | Button `:to` prop equals `{ name: 'recipe-create' }` |

#### RecipeDetailView

| Test | Scenario | Assertion |
|---|---|---|
| Renders recipe name | `store.currentRecipe` set | Name displayed in heading |
| Portion selector defaults to Regular | Mount | Regular (x1.0) selected |
| Portion selector scales amounts | Switch to Double | Amounts multiplied by 2.0 |
| Grayscale class on recently cooked | `isRecentlyCooked` returns true | Image has `grayscale` CSS class |
| No grayscale class if not recent | `isRecentlyCooked` returns false | No `grayscale` class |
| Mark as cooked button calls action | Click button | `store.markCooked(id)` called |
| Steps list renders in order | Recipe with 3 steps | Steps rendered as 1, 2, 3 |

#### RecipeFormView

| Test | Scenario | Assertion |
|---|---|---|
| Create mode — no pre-fill | Route name `recipe-create` | Form fields empty |
| Edit mode — pre-fills fields | Route name `recipe-edit`, `store.currentRecipe` set | Name, description pre-filled |
| Add ingredient row | Click "Add Ingredient" | New ingredient row appears |
| Remove ingredient row | Click remove on row | Row removed |
| Add step row | Click "Add Step" | New step row appears |
| Submit calls createRecipe | Fill form, submit | `store.createRecipe` called with correct payload |
| Submit calls updateRecipe in edit mode | Edit mode, submit | `store.updateRecipe` called |
| Validation — name required | Submit with empty name | Error shown; no API call |
| Validation — at least one ingredient | Submit with no ingredients | Error shown |

---

## 5. Test Data Helpers

### Backend

Create a `TestDataBuilder` static class in the test project:

```csharp
// Example patterns — not exhaustive
TestDataBuilder.Ingredient(name: "flour", category: "DRY_GOODS")
TestDataBuilder.Recipe(name: "Test Cake", servings: 4)
TestDataBuilder.CreateRecipeRequest(ingredientCount: 2, stepCount: 1)
```

Builders should accept optional overrides and provide sensible defaults. This prevents test data from becoming a maintenance burden.

### Frontend

Create `frontend/src/test/factories.js` with factory functions:

```js
export const makeRecipe = (overrides = {}) => ({
  id: 'test-id-1',
  name: 'Test Recipe',
  description: null,
  imageUrl: null,
  servings: 4,
  ingredientCount: 2,
  lastCookedAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

export const makeIngredient = (overrides = {}) => ({ ... })
export const makeRecipeDetail = (overrides = {}) => ({ ... })
```

---

## 6. Test Organization

### Backend

```
backend/RecipeApp.Tests/
├── RecipeApp.Tests.csproj
├── Infrastructure/
│   ├── DatabaseFixture.cs          (Testcontainers setup)
│   └── RecipeAppFactory.cs         (WebApplicationFactory)
├── Validators/
│   ├── IngredientValidatorTests.cs
│   └── RecipeValidatorTests.cs
├── DTOs/
│   └── MappingsTests.cs
├── Services/
│   ├── RecipeServiceTests.cs       (integration — hits real DB)
│   └── ImageServiceTests.cs        (unit — no I/O)
└── Endpoints/
    ├── HealthEndpointTests.cs
    ├── IngredientsEndpointTests.cs
    └── RecipesEndpointTests.cs
```

### Frontend

```
frontend/src/
├── stores/
│   ├── recipes.js
│   ├── recipes.spec.js
│   ├── ingredients.js
│   └── ingredients.spec.js
├── services/
│   ├── api.js
│   └── api.spec.js
├── views/
│   ├── RecipesView.vue
│   ├── RecipesView.spec.js
│   ├── RecipeDetailView.vue
│   ├── RecipeDetailView.spec.js
│   ├── RecipeFormView.vue
│   └── RecipeFormView.spec.js
├── components/layout/
│   ├── AppBottomNav.vue
│   └── AppBottomNav.spec.js
└── test/
    ├── setup.js                    (global test setup, MSW handlers)
    └── factories.js                (test data factories)
```

---

## 7. What We Are Not Testing (and Why)

| Area | Reason |
|---|---|
| EF migration designer files | Auto-generated; no logic |
| DataSeeder data content | It's test data itself; verify it runs via readiness check |
| Vuetify component internals | Third-party library |
| Bootstrap utility classes | Third-party library |
| `AppTopBar.vue` | No logic; purely presentational |
| `HomeView.vue`, `MealPlanView.vue`, `ShoppingView.vue` | Stub pages with no logic (yet) |
| `Program.cs` DI registration | Covered implicitly by integration tests booting successfully |
| Router `scrollBehavior` | Browser API; not meaningful in jsdom |

---

## 8. Priority Order for Implementation

Implement in this sequence so each phase provides useful coverage immediately:

| Phase | What | Value delivered |
|---|---|---|
| 1 | Backend validator unit tests | Cheapest tests, highest logic density; instant feedback on validation rules |
| 2 | Backend mapping unit tests | Verify sort ordering and field mapping before integration complexity |
| 3 | Backend integration test infrastructure | Testcontainers + WebApplicationFactory scaffold — required for all subsequent backend integration work |
| 4 | RecipeService integration tests | Core business logic with real DB; confidence in CRUD correctness |
| 5 | Recipe endpoint HTTP tests | Full request-response-database cycle; matches what the frontend actually calls |
| 6 | Ingredient endpoint HTTP tests | Completes API surface coverage |
| 7 | Frontend store unit tests | State management logic; fast, no DOM required |
| 8 | Frontend component tests (RecipeDetailView, RecipesView) | Critical path UI |
| 9 | Frontend component tests (RecipeFormView) | Most complex UI; form + dynamic lists |
| 10 | ImageService unit tests + image endpoint tests | Isolated but important for upload feature |

---

## 9. Running Tests

### Backend

```bash
cd backend
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj

# With coverage
dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj --coverage --coverage-output-format cobertura
```

The suite runs on **Microsoft.Testing.Platform** (MTP), not VSTest — xunit.v3 4.0 dropped the
VSTest bridge, and the .NET 10 SDK refuses to run MTP test projects through the VSTest target.
The repository-root `global.json` opts `dotnet test` into the MTP runner; without it every
backend test run fails with *"Testing with VSTest target is no longer supported"*. Coverage
comes from `Microsoft.Testing.Extensions.CodeCoverage` and is written to
`RecipeApp.Tests/TestResults/`, so the old VSTest `--collect:"XPlat Code Coverage"` collector
no longer applies.

Requires Docker (for Testcontainers). The PostgreSQL container is pulled automatically on first run.

### Frontend

```bash
cd frontend

# Run once
npm run test

# Watch mode
npm run test:watch

# With coverage
npm run test:coverage
```

Add to `package.json`:
```json
"scripts": {
  "test": "vitest run",
  "test:watch": "vitest",
  "test:coverage": "vitest run --coverage"
}
```

---

## 10. Phase 3 Considerations (Future)

When Recipe Scraping via Claude AI is implemented, extend this spec with:

- **RecipeScrapeService unit tests** — mock the Anthropic SDK; verify the parsed response is correctly mapped to a `CreateRecipeRequest`
- **Scrape endpoint integration test** — stub the Claude API at the HTTP level (MSW or similar); verify the round-trip through validation and persistence
- **Failure cases** — malformed Claude response, network timeout, unsupported URL, ingredient not in catalogue

These are out of scope here; document them as a separate spec when Phase 3 begins.
