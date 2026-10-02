# Frontend — Vue 3 SPA

Measurement and category rules live in `.claude/rules/` (repo root). Read them before touching
amount formatting, portion scaling, unit pickers or category lists.

## Layout (`src/`)

| Folder | Holds |
|---|---|
| `views/` | Route-level pages (lazy-loaded) |
| `components/` | Shared components; `layout/` has `AppTopBar`, `AppBottomNav` |
| `stores/` | Pinia, one file per domain (`recipes`, `ingredients`, `mealPlans`, `shoppingList`, `ui`) |
| `services/api.js` | The pre-configured Axios instance |
| `constants/` | `units.js`, `categories.js` — mirrors of the backend enums |
| `composables/` | `useWakeLock.js` |
| `router/index.js` | All routes |
| `test/` | Vitest `setup.js` and `factories.js` |

Tests sit beside their subject as `*.spec.js` (Vitest + Vue Test Utils). Run with `npm test`.

## Conventions

- **`<script setup>` only.** No Options API.
- **HTTP:** import `api` from `@/services/api.js`. Never use raw `fetch` or `axios`.
- **Pinia:** `use<Domain>Store()`. Every API call goes through a store action.
- **Vuetify first.** Prefer a Vuetify component over raw HTML wherever one exists.
- **Bootstrap:** utility classes (`d-flex`, `gap-*`) and the grid only. No Bootstrap JS or components.
  It is imported after Vuetify.
- **Never hard-code a unit or category list.** Consume `constants/units.js` / `constants/categories.js`.
- **Grayscale rule:** add class `grayscale` to a recipe image when `lastCookedAt` is within 7 days.
- **Routes:** use the names defined in `router/index.js` (`home`, `recipes`, `recipe-detail`,
  `recipe-cooking`, `meal-plan`, `shopping`, …). A route with `meta.fullscreen` hides the top and
  bottom bars (Cooking Mode).
- **Loading/empty/error states:** use `EmptyState`, `ErrorState`, and the global snackbar
  (`stores/ui.js` + `AppSnackbar`). Store fetches treat a 404 as "not found" (empty state), not
  as an error.
- **PWA:** `vite-plugin-pwa` runtime-caches `/api/v1/recipes` and `/uploads/images/`. Change the
  caching rules with that in mind.

## Environment (`.env.development`)

| Variable | Purpose |
|---|---|
| `VITE_APP_TITLE` | App name in the title bar |
| `VITE_API_BASE_URL` | API base URL including `/api/v1` |
