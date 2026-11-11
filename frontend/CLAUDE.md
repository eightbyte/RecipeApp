# Frontend — Vue 3 SPA

Measurement and category rules live in `.claude/rules/` (repo root). Read them before touching
amount formatting, Servings scaling, unit pickers or category lists.

Read `CODING_STANDARDS.md` (repo root) before writing or reviewing code: comments, naming, prop types, hard-coded values, Vuetify.

## Layout (`src/`)

| Folder | Holds |
|---|---|
| `views/` | Route-level pages (lazy-loaded) |
| `components/` | Shared components; `layout/` has `AppTopBar`, `AppBottomNav` |
| `stores/` | Pinia, one file per domain (`recipes`, `ingredients`, `mealPlans`, `shoppingList`, `ui`) |
| `services/api.js` | The pre-configured Axios instance |
| `constants/` | `units.js`, `categories.js`, `recipeSorts.js` — mirrors of the backend enums; `batchSizes.js` holds the recipe view's ½×/1×/2× shortcuts onto Servings, which exist only in the frontend; `recipeLimits.js` mirrors `RecipeLimits` (lengths and the Servings range); `servings.js` formats a servings count; `modelServer.js` mirrors `ModelServerState` and its `UnavailableReason()` wording |
| `composables/` | `useWakeLock.js`; `useRecipePages.js` pages a recipe list and holds the shared `SEARCH_DEBOUNCE_MS`; `useHold.js` is the 1-second press-and-hold on pointer events (editing a Custom Item); `useDelayedHide.js` keeps each just-ticked Purchased item shown for 3 seconds on its own timer; `useIngredientRowLayout.js` lays out a recipe form's Amount, Unit and Notes, splitting them onto two lines on a phone; `useRecipeValidation.js` decides which recipe-form problems are showing (on load, on leaving a field, after Save) |
| `utils/` | `dateOnly.js` formats date-only values; `storage.js` wraps `localStorage` for device-local preferences; `recipeValidation.js` is the one rule set that blocks a recipe save, shared by the recipe form and the import preview |
| `styles/` | `settings.scss` — Vuetify Sass settings |
| `router/index.js` | All routes |
| `router/navigation.js` | `viewKey` (the key App.vue mounts a view under) and `scrollPosition` (the router's scroll behaviour), pure so they test without mounting `App` |
| `router/recipeRoutes.js` | Builds and reads the recipe view and Cooking Mode links with their scale, kept apart so importing it doesn't load the router |
| `test/` | Vitest `setup.js`, `factories.js`, `timeZone.js` (run a describe block in a given timezone) and `infiniteScrollStub.js` (stands in for `VInfiniteScroll`, which never loads under jsdom) |

Tests sit beside their subject as `*.spec.js` (Vitest + Vue Test Utils). Run with `npm test`.
`src/test/setup.js` unmounts every wrapper after each test (`enableAutoUnmount`), so mount inside
each test or in `beforeEach`; a wrapper mounted in `beforeAll` is gone after the first test.

## Conventions

- **`<script setup>` only.** No Options API.
- **HTTP:** import `api` from `@/services/api.js`. Never use raw `fetch` or `axios`.
- **Pinia:** `use<Domain>Store()`. Every API call goes through a store action.
- **Typeface:** the system font stack, set once as `$body-font-family` in `src/styles/settings.scss`.
  Vuetify compiles it into `html` and every typography class (`text-h6`, `text-caption`, …).
- **Never hard-code a unit, category, Batch Size or recipe sort list.** Consume `constants/units.js`,
  `constants/categories.js`, `constants/batchSizes.js` / `constants/recipeSorts.js`.
- **Device-local preferences** (such as the Recipes tab sort) go through `utils/storage.js`, never
  `localStorage` directly, because storage can throw. Each key is named once beside its constants.
- **Grayscale rule:** add class `grayscale` to a recipe image when `lastCookedAt` is within 7 days.
- **Planned Meals** on Home and Meal Plan render through `components/PlannedMealCard.vue`. Every
  count of Servings is stepped with `components/ServingsStepper.vue` and shown on a Planned Meal or
  Item Source with `components/ServingsChip.vue`.
- **Date-only values** (`scheduledDate`, `firstScheduledDate`, …) display through `formatDateOnly` from
  `utils/dateOnly.js`, so they show the stored day in every timezone. Timestamps such as `createdAt`
  keep local-time formatting.
- **Routes:** use the names defined in `router/index.js` (`home`, `recipes`, `recipe-detail`,
  `recipe-cooking`, `meal-plan`, `shopping`, …). A route with `meta.fullscreen` hides the top and
  bottom bars (Cooking Mode). App.vue remounts a view, and the router scrolls to the top, on every
  navigation, including a query-only change. A route with `meta.keepOnQueryChange` (the recipe
  view only) stays mounted and keeps its scroll position while only its query changes, so it must
  read the query reactively. Both rules live in `router/navigation.js`. Link to a scaled recipe
  view or Cooking Mode, and read their scale, only through `router/recipeRoutes.js`.
- **Loading/empty/error states:** use `EmptyState`, `ErrorState`, and the global snackbar
  (`stores/ui.js` + `AppSnackbar`). Store fetches treat a 404 as "not found" (empty state), not
  as an error.
- **PWA:** `vite-plugin-pwa` runtime-caches the recipe list (`/api/v1/recipes`, NetworkFirst in
  `recipe-list`, so the pages joined on scroll come from one version of the list), single recipes
  (`/api/v1/recipes/{id}`, StaleWhileRevalidate in `recipe-api`) and `/uploads/images/`. Change the
  caching rules with that in mind.
- **Recipe lists** (Recipes tab, `RecipeBrowser`) load a page at a time through
  `composables/useRecipePages.js`, one instance per list, and render through
  `components/RecipeInfiniteScroll.vue`. A recipe mutation does not patch any list, because each
  list refetches page 1 when it mounts.

## Environment (`.env.development`)

| Variable | Purpose |
|---|---|
| `VITE_APP_TITLE` | App name in the title bar |
| `VITE_API_BASE_URL` | API base URL including `/api/v1` |
