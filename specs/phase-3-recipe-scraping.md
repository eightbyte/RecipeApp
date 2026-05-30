# Phase 3 — Recipe Scraping via Claude AI

**Version:** 1.0  
**Date:** 2026-05-30  
**Status:** Draft  
**Depends on:** Phase 2 (Recipe CRUD) complete

---

## 1. Overview

Phase 3 allows users to import a recipe by providing a URL. The backend fetches the page HTML, strips it to readable text, sends it to the Claude AI API for structured extraction, normalises the result (ingredient matching, unit conversion), and returns a preview DTO to the frontend. The user can review and edit the extracted data before confirming the save via a dedicated confirm endpoint.

---

## 2. Deliverable

> Paste a recipe URL → Claude extracts it → user reviews/edits a pre-filled form → recipe is saved.

---

## 3. New NuGet Packages (Backend)

Add the following packages to `backend/RecipeApp.API/RecipeApp.API.csproj`:

| Package | Purpose | Version |
|---|---|---|
| **Official Anthropic .NET SDK** | Claude API client | Latest stable available with `dotnet add package Anthropic` documentation at `https://platform.claude.com/docs/en/api/sdks/csharp` |
| `AngleSharp` | HTML parsing and DOM stripping | Latest stable (`>= 1.1`) |

> **SDK note:** Use the official first-party Anthropic .NET SDK. If no official SDK exists at implementation time, fall back to `Anthropic.SDK` (community package by tghamm on NuGet). Record the chosen package name and version in `CLAUDE.md` once decided.

---

## 4. Configuration

### 4.1 New appsettings Keys

Add to `appsettings.json` (with sensible production defaults) and override in `appsettings.Development.json` as needed:

```json
"RecipeScraping": {
  "AnthropicApiKey": "",
  "Model": "claude-sonnet-4-6",
  "HtmlFetchTimeoutSeconds": 15,
  "ClaudeTimeoutSeconds": 60,
  "MaxHtmlCharacters": 50000
}
```

| Key | Description | Default |
|---|---|---|
| `AnthropicApiKey` | Anthropic API key — store in `appsettings.Development.json` (gitignored) or environment variable | _empty_ |
| `Model` | Claude model ID to use | `claude-sonnet-4-6` |
| `HtmlFetchTimeoutSeconds` | Timeout for fetching the recipe page HTML | `15` |
| `ClaudeTimeoutSeconds` | Timeout for the Claude API call | `60` |
| `MaxHtmlCharacters` | Maximum characters of stripped text sent to Claude | `50000` |

> `appsettings.Development.json` is gitignored. Never commit API keys.

### 4.2 Options Class

Create `Services/RecipeScrapingOptions.cs`:

```csharp
namespace RecipeApp.API.Services;

public class RecipeScrapingOptions
{
    public const string SectionName = "RecipeScraping";

    public string AnthropicApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-4-6";
    public int HtmlFetchTimeoutSeconds { get; init; } = 15;
    public int ClaudeTimeoutSeconds { get; init; } = 60;
    public int MaxHtmlCharacters { get; init; } = 50_000;
}
```

Register in `Program.cs`:

```csharp
builder.Services.Configure<RecipeScrapingOptions>(
    builder.Configuration.GetSection(RecipeScrapingOptions.SectionName));
```

---

## 5. Backend

### 5.1 New Files

```
backend/RecipeApp.API/
├── Services/
│   ├── RecipeScrapeService.cs       Main scraping orchestration
│   └── RecipeScrapingOptions.cs     Options/config class
├── DTOs/
│   └── Scrape/
│       ├── ScrapeRecipeRequest.cs
│       ├── ScrapePreviewResponse.cs
│       ├── ScrapeConfirmRequest.cs
│       └── ScrapeConfirmResponse.cs   (alias — returns RecipeDetailResponse)
├── Validators/
│   └── ScrapeValidators.cs
└── Endpoints/
    └── RecipeScrapeEndpoints.cs
```

---

### 5.2 RecipeScrapeService

**File:** `Services/RecipeScrapeService.cs`

The service is responsible for three sequential stages:

1. **Fetch & strip HTML** — retrieve the page; strip markup to plain text.
2. **Claude extraction** — send text to Claude; receive structured JSON.
3. **Normalisation** — match ingredients, categorise new ones, convert units.

#### 5.2.1 HTML Fetching and Stripping

Use a named `HttpClient` registered in DI (separate from any other HttpClient):

```
Timeout: RecipeScrapingOptions.HtmlFetchTimeoutSeconds
User-Agent: "RecipeApp/1.0 (+https://github.com/your-repo)"
```

**Stripping strategy using AngleSharp:**

1. Pass the raw HTML string to `AngleSharp.Html.Parser.HtmlParser.ParseDocumentAsync()`.
2. Remove the following elements before extracting text:
   - `script`, `style`, `noscript`
   - `nav`, `header`, `footer`, `aside`
   - `iframe`, `form`, `button`, `input`, `select`, `textarea`
   - Any element whose inline `style` attribute contains `display:none` or `visibility:hidden`
3. Extract `document.Body.TextContent`.
4. Normalize whitespace: collapse all sequences of `\r`, `\n`, `\t`, and multiple spaces into a single space; trim.
5. Truncate to `RecipeScrapingOptions.MaxHtmlCharacters` characters if longer.

#### 5.2.2 Claude API Integration

Authenticate with `RecipeScrapingOptions.AnthropicApiKey`. Use the SDK's async client.

Apply a `CancellationTokenSource` with timeout `RecipeScrapingOptions.ClaudeTimeoutSeconds`.

**Recommended approach — tool use (structured output):**

Define a tool called `extract_recipe` with the following JSON schema as its `input_schema`. Call Claude with `tool_choice = { type: "tool", name: "extract_recipe" }` to force the structured response.

#### 5.2.3 Claude Tool Schema

```json
{
  "name": "extract_recipe",
  "description": "Extract a recipe from the given web page text and return it as structured data.",
  "input_schema": {
    "type": "object",
    "properties": {
      "name": {
        "type": "string",
        "description": "The recipe name."
      },
      "description": {
        "type": ["string", "null"],
        "description": "A short food description. Null if not present."
      },
      "servings": {
        "type": "integer",
        "description": "Number of servings the base recipe yields. Default 4 if not stated."
      },
      "ingredients": {
        "type": "array",
        "description": "All ingredients required by the recipe.",
        "items": {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "Ingredient name in lowercase, singular, normalised (e.g. 'onion', 'chicken breast')."
            },
            "display_name": {
              "type": "string",
              "description": "Ingredient name as it should be displayed (e.g. 'Onion', 'Chicken Breast')."
            },
            "amount": {
              "type": "number",
              "description": "Numeric quantity. Use the amount as stated on the page."
            },
            "unit": {
              "type": "string",
              "description": "Unit of measurement as stated on the page (e.g. 'g', 'ml', 'cup', 'oz', 'lb', 'tsp', 'tbsp', 'pcs'). Preserve the original unit; do not convert."
            },
            "notes": {
              "type": ["string", "null"],
              "description": "Preparation notes (e.g. 'finely chopped', 'optional'). Null if none."
            }
          },
          "required": ["name", "display_name", "amount", "unit"]
        }
      },
      "steps": {
        "type": "array",
        "description": "Ordered cooking steps.",
        "items": {
          "type": "object",
          "properties": {
            "step_number": {
              "type": "integer",
              "description": "1-based step number."
            },
            "instruction": {
              "type": "string",
              "description": "Full step instruction text."
            },
            "ingredient_indexes": {
              "type": "array",
              "items": { "type": "integer" },
              "description": "0-based indexes into the ingredients array for ingredients used in this step. Empty array if no specific ingredients apply."
            }
          },
          "required": ["step_number", "instruction", "ingredient_indexes"]
        }
      }
    },
    "required": ["name", "servings", "ingredients", "steps"]
  }
}
```

#### 5.2.4 System Prompt

```
You are a recipe data extraction assistant. 
Extract the complete recipe from the web page text provided by the user.
If any information is missing or ambiguous, make a reasonable best-guess rather than omitting it.
Return all data using the extract_recipe tool — do not include any explanation outside the tool call.
```

#### 5.2.5 User Message Template

```
Extract the recipe from the following web page text:

---
{strippedText}
---
```

#### 5.2.6 Ingredient Normalisation and Matching

After receiving Claude's response, for each extracted ingredient:

1. **Normalise** the ingredient `name` to lowercase, trimmed.
2. **Exact match** against `ingredients.name` in the database (case-insensitive).
3. If matched: use the existing `Ingredient` entity. Set `IsNew = false`.
4. If not matched:
   - Mark `IsNew = true`.
   - Apply the **keyword heuristic** (see §5.2.7) to assign a `SuggestedCategory`.
5. Do **not** create new `Ingredient` rows at this stage — that happens in the confirm endpoint.

#### 5.2.7 Keyword Heuristic for Ingredient Categorisation

Implemented as a static lookup in `Services/RecipeScrapeService.cs` (or a helper class). Evaluate each category's keywords against the normalised ingredient name (case-insensitive, substring match). Use the **first matching category** in the priority order below. Default to `OTHER` if no keyword matches.

| Priority | Category | Example Keywords |
|---|---|---|
| 1 | `MEAT_SEAFOOD` | chicken, beef, pork, lamb, turkey, duck, bacon, ham, sausage, mince, steak, fillet, breast, thigh, salmon, tuna, cod, prawn, shrimp, crab, lobster, mussel, anchovy, chorizo, salami, pepperoni |
| 2 | `DAIRY` | milk, cream, butter, cheese, yogurt, yoghurt, egg, parmesan, mozzarella, cheddar, ricotta, brie, ghee, crème fraîche, sour cream |
| 3 | `CANNED` | canned, tinned, kidney bean, chickpea, black bean, cannellini, coconut milk, chopped tomato, diced tomato, tomato paste |
| 4 | `FROZEN` | frozen |
| 5 | `BAKERY` | bread, roll, bun, baguette, pita, tortilla, wrap, crumpet, croissant |
| 6 | `BEVERAGES` | stock, broth, wine, beer, juice, coffee, tea |
| 7 | `CONDIMENTS` | oil, vinegar, sauce, soy, fish sauce, worcestershire, mustard, ketchup, mayonnaise, honey, miso, tahini, paprika, cumin, turmeric, cinnamon, nutmeg, curry, chilli flake, cayenne, vanilla, extract, seasoning, spice, herb (when used as a dry spice) |
| 8 | `DRY_GOODS` | flour, sugar, salt, rice, pasta, noodle, oat, breadcrumb, lentil, quinoa, couscous, baking powder, baking soda, yeast, cornstarch, cornflour, cocoa, chocolate, almond, walnut, cashew, peanut, sesame, seed, nut |
| 9 | `PRODUCE` | onion, garlic, carrot, celery, tomato, potato, lettuce, spinach, kale, broccoli, pepper, capsicum, zucchini, cucumber, avocado, lemon, lime, orange, apple, banana, mushroom, corn, asparagus, pea, parsley, basil, coriander, thyme, rosemary, mint, dill, chive, scallion, leek, shallot, ginger, chilli, eggplant, beetroot, pumpkin, squash, berry |
| 10 | `OTHER` | _(default if no keyword matches)_ |

> **Implementation note:** Maintain the keyword lists as a `static readonly Dictionary<string, string[]>` keyed by category constant, iterated in priority order. A keyword matches if the normalised ingredient name **contains** the keyword as a substring.

#### 5.2.8 Imperial-to-Metric Unit Conversion

After ingredient normalisation, run a conversion pass over all ingredient amounts before building the preview DTO. This is the authoritative conversion step — Claude is instructed to preserve original units.

| Detected Unit(s) | Convert to | Factor |
|---|---|---|
| `oz`, `ounce`, `ounces` | `g` | × 28.3495 |
| `lb`, `lbs`, `pound`, `pounds` | `g` | × 453.592 |
| `fl oz`, `fluid oz`, `fluid ounce` | `ml` | × 29.5735 |
| `cup`, `cups` | `ml` | × 240 |
| `pt`, `pint`, `pints` | `ml` | × 473.176 |
| `qt`, `quart`, `quarts` | `ml` | × 946.353 |
| `gal`, `gallon`, `gallons` | `L` | × 3.78541 |

Units to **pass through unchanged** (already metric or universal): `g`, `kg`, `ml`, `L`, `tsp`, `tbsp`, `pcs`, `piece`, `pieces`, `unit`, `units`, `clove`, `cloves`, `whole`, `pinch`, `bunch`, `sprig`, `can`, `slice`.

**Matching:** Unit detection is case-insensitive, trimmed, and matches the full unit string (not substring) to avoid false positives (e.g. "plum" should not match "lb").

**Rounding:** After conversion, round to 3 decimal places (`Math.Round(value, 3)`).

---

### 5.3 Endpoints

**File:** `Endpoints/RecipeScrapeEndpoints.cs`

Register as `app.MapRecipeScrapeEndpoints()` in `Program.cs`.

#### 5.3.1 POST /recipes/scrape

| | |
|---|---|
| **Method** | `POST` |
| **Route** | `/api/v1/recipes/scrape` |
| **Request body** | `ScrapeRecipeRequest` |
| **Success response** | `200 OK` — `ScrapePreviewResponse` |
| **Error responses** | `400 Bad Request` (invalid/missing URL), `422 Unprocessable Entity` (URL unreachable or content cannot be processed) |
| **Tag** | `Recipes` |

**Behaviour:**
1. Validate the request (URL must be non-empty, must be a valid absolute `http`/`https` URL).
2. Call `RecipeScrapeService.ScrapeAsync(url, cancellationToken)`.
3. Return the `ScrapePreviewResponse` — even if extraction is partial (e.g., some fields missing). The frontend allows the user to fill in gaps.
4. If the HTML fetch times out or returns a non-success status, return `422` with a descriptive problem detail message.
5. If the Claude API call fails or times out, return `422` with a descriptive problem detail message.
6. If Claude returns empty/null for required fields (`name`, `ingredients`, `steps`), return `422`.
7. Claude call failures should be logged at `Warning` level with the URL and error details.

#### 5.3.2 POST /recipes/scrape/confirm

| | |
|---|---|
| **Method** | `POST` |
| **Route** | `/api/v1/recipes/scrape/confirm` |
| **Request body** | `ScrapeConfirmRequest` |
| **Success response** | `201 Created` — `RecipeDetailResponse` (same shape as `GET /recipes/{id}`) |
| **Error responses** | `400 Bad Request` (validation failure) |
| **Tag** | `Recipes` |

**Behaviour:**
1. Validate the request.
2. For each ingredient where `IngredientId` is `null` (new ingredient):
   - Create a new `Ingredient` row using `NewIngredientName`, `NewIngredientDisplayName`, and `Category`.
   - Normalise `NewIngredientName` to lowercase before inserting (matches the `ingredients.name` unique constraint pattern).
   - If an ingredient with the same normalised name already exists (race condition), use the existing record.
3. Create the `Recipe` entity, setting `SourceUrl` from the request.
4. Create `RecipeIngredient` rows in `DisplayOrder` order.
5. Create `RecipeStep` rows.
6. Create `RecipeStepIngredient` rows based on `IngredientIndexes`.
7. Return `RecipeDetailResponse` with `201 Created` and `Location: /api/v1/recipes/{newId}` header.

---

### 5.4 DTOs

**Directory:** `DTOs/Scrape/`

#### ScrapeRecipeRequest

```csharp
namespace RecipeApp.API.DTOs.Scrape;

public record ScrapeRecipeRequest(string Url);
```

#### ScrapePreviewResponse

```csharp
namespace RecipeApp.API.DTOs.Scrape;

public record ScrapePreviewResponse(
    string Name,
    string? Description,
    int Servings,
    string SourceUrl,
    List<ScrapePreviewIngredient> Ingredients,
    List<ScrapePreviewStep> Steps
);

public record ScrapePreviewIngredient(
    Guid? IngredientId,          // null = new ingredient not yet in catalogue
    string Name,                 // normalised lowercase
    string DisplayName,
    decimal Amount,
    string Unit,
    string? Notes,
    bool IsNew,
    string SuggestedCategory,    // from keyword heuristic; for new ingredients only
    int DisplayOrder
);

public record ScrapePreviewStep(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes  // 0-based indexes into Ingredients list
);
```

#### ScrapeConfirmRequest

```csharp
namespace RecipeApp.API.DTOs.Scrape;

public record ScrapeConfirmRequest(
    string Name,
    string? Description,
    string SourceUrl,
    int Servings,
    List<ScrapeConfirmIngredient> Ingredients,
    List<ScrapeConfirmStep> Steps
);

/// <summary>
/// Either IngredientId (existing) OR the New* fields (new ingredient) must be provided.
/// </summary>
public record ScrapeConfirmIngredient(
    Guid? IngredientId,
    string? NewIngredientName,
    string? NewIngredientDisplayName,
    string? Category,
    decimal Amount,
    string Unit,
    string? Notes,
    int DisplayOrder
);

public record ScrapeConfirmStep(
    int StepNumber,
    string Instruction,
    List<int> IngredientIndexes
);
```

---

### 5.5 Validators

**File:** `Validators/ScrapeValidators.cs`

**`ScrapeRecipeRequestValidator`:**
- `Url` is not empty.
- `Url` is a valid absolute `http` or `https` URI.

**`ScrapeConfirmRequestValidator`:**
- `Name` not empty, max 200 characters.
- `Servings` between 1 and 100.
- `SourceUrl` not empty.
- `Ingredients` not empty.
- Each `ScrapeConfirmIngredient`:
  - Either `IngredientId` has a value, **or** `NewIngredientName`, `NewIngredientDisplayName`, and `Category` are all non-empty.
  - If `Category` is provided, it must be a valid value from `IngredientCategory.All`.
  - `Amount` > 0.
  - `Unit` not empty, max 20 characters.
  - `Notes` max 200 characters (if provided).
- Each `ScrapeConfirmStep`:
  - `StepNumber` ≥ 1.
  - `Instruction` not empty.
  - All `IngredientIndexes` are valid (0-based, within bounds of the `Ingredients` list).

---

### 5.6 Error Handling

| Scenario | HTTP Status | Detail message |
|---|---|---|
| Invalid or non-HTTP(S) URL | `400 Bad Request` | "A valid http or https URL is required." |
| URL fetch timeout | `422 Unprocessable Entity` | "The recipe page could not be retrieved within the allowed time." |
| URL returns non-success HTTP status | `422 Unprocessable Entity` | "The recipe page returned HTTP {statusCode}." |
| Claude API timeout | `422 Unprocessable Entity` | "Recipe extraction timed out. Please try again." |
| Claude API error (non-timeout) | `422 Unprocessable Entity` | "Recipe extraction failed. Please try again or create the recipe manually." |
| Claude returns no name/ingredients/steps | `422 Unprocessable Entity` | "No recipe content could be extracted from this page." |

Partial extraction (e.g., description missing, some steps lacking ingredient mappings) is **not** an error. Return the partial `ScrapePreviewResponse` and let the user complete it.

---

## 6. Frontend

### 6.1 New Files

```
frontend/src/
├── components/
│   └── RecipeUrlBottomSheet.vue      URL input sheet + loading state
├── views/
│   └── RecipeScrapePreviewView.vue   Full-page editable preview
└── stores/
    └── recipes.js                    (modified — add scrape actions)
```

### 6.2 Router Changes

Add to `router/index.js`:

```js
{
  path: '/recipes/scrape/preview',
  name: 'scrape-preview',
  component: () => import('@/views/RecipeScrapePreviewView.vue')
}
```

### 6.3 RecipeUrlBottomSheet.vue

**Trigger:** "Import from URL" button on the recipe list page (`RecipesView.vue`). Add this button alongside the existing "Create Recipe" action (e.g., as a secondary FAB or a menu item).

**Component responsibilities:**
- Render as a `VBottomSheet` (Vuetify).
- URL text field with `v-text-field` and `type="url"`.
- "Import" button calls the `scrapeRecipe(url)` store action.
- While loading: disable the Import button; show a `VProgressLinear` or `VProgressCircular` inside the sheet with the message _"Extracting recipe… this may take up to 30 seconds."_
- On success: close the sheet; navigate to `{ name: 'scrape-preview' }`.
- On error: display the error message returned by the API inside the sheet (do not navigate). Allow the user to retry with a different URL or dismiss.

### 6.4 RecipeScrapePreviewView.vue

Full-page editable preview of the scraped recipe. The form structure mirrors `RecipeFormView.vue` but is pre-populated with the scraped data.

**Fields editable by the user:**
- Recipe name
- Description
- Servings
- Source URL (read-only display; not editable post-scrape)
- Each ingredient: amount, unit, notes
- Each ingredient marked `IsNew = true`: additionally shows a `VSelect` for **Category** (pre-filled with `SuggestedCategory`)
- Each step: instruction text
- Step-ingredient associations (via the existing checkbox overlay pattern from Phase 2)

**New ingredient visual indicator:** Display new ingredients (where `IsNew = true`) with a chip or badge labeled _"New"_ so the user can easily identify ingredients that will be created in the catalogue.

**Actions:**
- **Save Recipe** — validates the form locally, then calls `confirmScrape(data)` → navigates to `{ name: 'recipe-detail', params: { id: newRecipeId } }` on success.
- **Cancel** — navigates back to `{ name: 'recipes' }` (discards the preview; no confirmation dialog needed).

**Loading state during save:** Disable the Save button and show a `VProgressLinear` at the top of the page while the confirm API call is in progress.

### 6.5 Pinia Store Changes

Modify `stores/recipes.js` to add:

```js
// State
scrapePreview: null,   // ScrapePreviewResponse | null
scrapeError: null,     // string | null
scrapeLoading: false,
confirmLoading: false,

// Actions
async scrapeRecipe(url) { ... }   // POST /recipes/scrape
async confirmScrape(data) { ... } // POST /recipes/scrape/confirm
clearScrapePreview() { ... }      // reset scrapePreview and scrapeError
```

- `scrapeRecipe` sets `scrapeLoading = true`, calls `api.post('/recipes/scrape', { url })`, stores the response in `scrapePreview`, clears `scrapeLoading` on completion. Sets `scrapeError` on failure.
- `confirmScrape` sets `confirmLoading = true`, calls `api.post('/recipes/scrape/confirm', data)`, adds the returned recipe to the existing `recipes` list in store state, clears `confirmLoading`. Returns the new recipe ID.
- `clearScrapePreview` resets `scrapePreview`, `scrapeError` to `null`.

### 6.6 User-Facing Copy

| UI Location | Copy |
|---|---|
| Import button (RecipesView) | **Import from URL** |
| Bottom sheet title | **Import Recipe** |
| Bottom sheet loading message | _Extracting recipe… this may take up to 30 seconds._ |
| Preview page title | **Review Imported Recipe** |
| New ingredient badge | **New** |
| Category select label (new ingredient) | **Category** |
| Save button | **Save Recipe** |
| Cancel button | **Cancel** |

---

## 7. Data Flow

```
User pastes URL
  → RecipeUrlBottomSheet calls scrapeRecipe(url)
    → POST /api/v1/recipes/scrape
      → Backend fetches HTML (AngleSharp HttpClient, HtmlFetchTimeoutSeconds)
      → Strips HTML to plain text (AngleSharp DOM manipulation)
      → Sends text to Claude API (claude-sonnet-4-6, tool use, ClaudeTimeoutSeconds)
      → Claude returns structured JSON via extract_recipe tool
      → Backend normalises ingredients (DB lookup, keyword heuristic, unit conversion)
      → Returns ScrapePreviewResponse
  → Frontend navigates to RecipeScrapePreviewView
    → User reviews and edits the pre-filled form
    → User selects category for any "New" ingredients
  → User clicks Save
    → confirmScrape(editedData) called
      → POST /api/v1/recipes/scrape/confirm
        → Backend creates new Ingredient rows for IsNew ingredients
        → Backend creates Recipe, RecipeIngredient, RecipeStep, RecipeStepIngredient rows
        → Returns RecipeDetailResponse (201 Created)
    → Frontend navigates to RecipeDetailView for the new recipe
```

---

## 8. Testing

### 8.1 Backend Tests

Add to `backend/RecipeApp.Tests/`:

```
Services/
  RecipeScrapeServiceTests.cs   Unit tests with mocked HttpClient and Claude client
Endpoints/
  RecipeScrapeEndpointsTests.cs Integration tests using RecipeAppFactory
```

**Unit tests for `RecipeScrapeService`:**
- HTML stripping removes `<script>`, `<style>`, `<nav>`, `<footer>` content.
- Imperial-to-metric conversion: verify each conversion factor (oz→g, lb→g, fl oz→ml, cup→ml, pt→ml, qt→ml, gal→L).
- Metric units pass through unchanged.
- Keyword heuristic: sample words map to expected categories; unknown word maps to `OTHER`.
- Ingredient matching: existing ingredient is matched by normalised name; unmatched returns `IsNew = true`.

**Integration tests for endpoints:**
- `POST /recipes/scrape` — mock `RecipeScrapeService`; verify `200 OK` with `ScrapePreviewResponse` shape.
- `POST /recipes/scrape` — invalid URL returns `400`.
- `POST /recipes/scrape` — service throws on unreachable URL → `422`.
- `POST /recipes/scrape/confirm` — creates recipe with new ingredients; returns `201` with correct `Location` header.
- `POST /recipes/scrape/confirm` — validation failure (empty name) returns `400`.

### 8.2 Frontend Tests

Add test files:

```
components/
  RecipeUrlBottomSheet.spec.js
views/
  RecipeScrapePreviewView.spec.js
stores/
  recipes.spec.js              (extend existing file with scrape action tests)
```

**Component tests:**
- `RecipeUrlBottomSheet`: renders URL input; shows loading state while `scrapeLoading` is true; displays error message from `scrapeError`.
- `RecipeScrapePreviewView`: renders pre-filled fields from `scrapePreview`; new ingredients show category select; save button calls `confirmScrape`.

**Store tests (MSW):**
- `scrapeRecipe`: sets `scrapeLoading`, stores response in `scrapePreview`, clears loading.
- `scrapeRecipe`: on API error, sets `scrapeError`.
- `confirmScrape`: calls correct endpoint, adds recipe to state on success.

---

## 9. Out of Scope for Phase 3

The following are explicitly deferred:

- Auto-categorisation of ingredients via a separate Claude call (listed as Future Functionality in SPEC.md).
- Recipe-from-image (photo upload to Claude Vision).
- Anti-bot bypass measures (if a site actively blocks scraping, the user should find a different URL or create the recipe manually).
- Caching scraped results.
- Editing `source_url` on the preview screen (it is displayed read-only; it can be edited later via `PUT /recipes/{id}`).
