# Phase 7 — Polish, UX & Cooking Mode

**Version:** 1.0  
**Date:** 2026-06-04  
**Status:** Draft  
**Depends on:** Phases 1–5 complete (Phase 6 Food Waste Tracking is **deferred** — see `specs/phase-6-food-waste-tracking.md`)

> Phase 7 is the **final v1 phase**. It contains no new data model and (almost) no backend work —
> it is a frontend quality pass that turns the working feature set into a production-quality mobile
> experience. The marquee feature is **Cooking Mode**; the rest is consistency work (states, error
> handling, accessibility, responsiveness) plus turning the app into an installable **PWA** with
> read-only offline recipe viewing.

---

## 1. Overview

Phase 7 delivers eight workstreams:

1. **Cooking Mode** — a distraction-free, full-screen view of a recipe's steps for use at the stove
   (large type, current-step emphasis, per-step ingredient highlighting, screen keep-awake).
2. **Mark as Cooked everywhere** — surface the existing cook action on the meal-plan recipe card
   (the one place it is still missing) and give every cook action user feedback.
3. **Loading, empty & error states** — standardise the inconsistent per-view states behind a small
   set of reusable components (`EmptyState`, `ErrorState`, skeletons).
4. **Error handling & retry** — a global feedback channel (snackbar) plus a consistent
   fetch-fails → `ErrorState` + **Try again** pattern, building on the existing Axios interceptor.
5. **PWA** — installable app via **vite-plugin-pwa** (Workbox): web manifest, service worker,
   app-shell precache, and **runtime caching of recipes as you view them** so any opened recipe is
   readable offline.
6. **Responsive polish** — verify and fix layout across iPhone SE / iPhone 15 / common Android
   widths, including iOS safe-area insets and dynamic viewport height.
7. **Accessibility audit** — accessible names on icon-only controls, image `alt` text, heading
   hierarchy, contrast, live regions, and reduced-motion support.
8. **Docker production build** — already exists (`frontend/Dockerfile`, `frontend/nginx.conf`, the
   `frontend` compose service); Phase 7 only closes the small gaps that the PWA introduces.

> **What already exists (verified, do not rebuild):** the `POST /recipes/{id}/cook` endpoint
> ([RecipesEndpoints.cs:99-105](../backend/RecipeApp.API/Endpoints/RecipesEndpoints.cs#L99-L105)),
> the `markCooked` store action ([recipes.js:91-98](../frontend/src/stores/recipes.js#L91-L98)),
> the "Mark as cooked" button on the recipe detail view
> ([RecipeDetailView.vue:143-154](../frontend/src/views/RecipeDetailView.vue#L143-L154)) and the
> home meal-plan card ([HomeView.vue:43-50](../frontend/src/views/HomeView.vue#L43-L50)), the
> production Docker/Nginx setup, and skeletons on the Recipes, Recipe-detail and Shopping views.

---

## 2. Deliverable

> Open a recipe → tap **Start cooking** → a full-screen, large-type step list with the current step
> emphasised and its ingredients highlighted; the screen stays awake; tap a step to mark it done;
> **Mark as cooked & finish** updates `LastCookedAt` and returns to the recipe. Every view shows a
> shaped skeleton while loading, a friendly empty state when there's nothing, and an
> **error + Try again** panel when a fetch fails; mutations confirm via a snackbar. The app is
> installable to the home screen, opens offline, and any recipe you've already viewed is readable
> with no network. Layout holds on small phones with iOS safe-areas respected; icon controls have
> accessible names.

---

## 3. Cooking Mode

### 3.1 Decision: scrollable step list

Cooking Mode is a **single full-screen scrollable list** of all steps in large type. The current
step is emphasised (accent border + heavier weight) and its ingredient chips are highlighted;
completed steps are dimmed. This is the chosen layout (over a one-step-per-screen carousel); a
**Next step** affordance still supports tap-to-advance within the list, satisfying SPEC §7's
"swipe or tap-next" intent without hiding the surrounding steps.

### 3.2 New & modified files

```
frontend/src/
├── views/
│   └── CookingModeView.vue        NEW — full-screen cooking experience
├── composables/                   NEW directory
│   └── useWakeLock.js             NEW — Screen Wake Lock API wrapper
├── router/index.js                MODIFIED — add the recipe-cooking route
├── App.vue                        MODIFIED — hide app chrome on full-screen routes
├── views/RecipeDetailView.vue     MODIFIED — add "Start cooking" CTA
└── views/MealPlanView.vue         MODIFIED — overflow menu "Start cooking" + "Mark as cooked"
```

> `composables/` is a standard Vue 3 location but is **new** to this project (no composables exist
> yet). It is the right home for the reusable, testable Wake Lock logic.

### 3.3 Route + full-screen chrome

Add to [router/index.js](../frontend/src/router/index.js) (after the `recipe-edit` route, keeping
the `/recipes/:id/...` grouping):

```js
{
  path: '/recipes/:id/cooking',
  name: 'recipe-cooking',
  component: () => import('@/views/CookingModeView.vue'),
  meta: { title: 'Cooking', fullscreen: true },
  props: true,
},
```

> Path is `/cooking` (not `/cook`) to avoid confusion with the `POST .../cook` API action and the
> "Mark as cooked" concept.

Cooking Mode needs minimal chrome, but [App.vue](../frontend/src/App.vue) always renders
`AppTopBar` and `AppBottomNav`. Gate them on the route's `fullscreen` meta so the cooking view owns
the whole viewport and supplies its own header:

```vue
<script setup>
import { useRoute } from 'vue-router'
import AppTopBar    from '@/components/layout/AppTopBar.vue'
import AppBottomNav from '@/components/layout/AppBottomNav.vue'
const route = useRoute()
</script>

<template>
  <v-app>
    <AppTopBar v-if="!route.meta.fullscreen" />
    <v-main>
      <router-view v-slot="{ Component, route: r }">
        <transition name="fade" mode="out-in">
          <component :is="Component" :key="r.fullPath" />
        </transition>
      </router-view>
    </v-main>
    <AppBottomNav v-if="!route.meta.fullscreen" />
  </v-app>
</template>
```

### 3.4 CookingModeView behaviour

`<script setup>`, `props: { id: String }`, uses `useRecipeStore`.

- **Data:** on mount, if `store.currentRecipe?.id !== id` call `store.fetchRecipe(id)`; otherwise
  reuse the cached `currentRecipe`. Show a skeleton while `store.loading`, an `ErrorState`
  (§5) with **Try again** if the fetch fails, and a "Recipe not found" empty state if the recipe is
  missing.
- **Portion:** accept an optional `?portion=HALF|REGULAR|DOUBLE` query (default `REGULAR`) so
  launching from a meal plan can carry the planned portion; reuse the same client multiplier and
  `formatAmount` logic as
  [RecipeDetailView](../frontend/src/views/RecipeDetailView.vue#L183-L201). No stored amounts are
  mutated (project rule).
- **Header (sticky):** a close button (`mdi-close`, `aria-label="Exit cooking mode"`) calling
  `router.back()`, the recipe name, a **Step {currentIndex + 1} / {total}** counter, and a thin
  `v-progress-linear` reflecting completion. Respects the top safe-area inset (§8).
- **Step list:** one block per `recipe.steps` entry (already ordered by `stepNumber`), rendered in
  large type (`text-h6`/`text-h5`). Each block shows the step number, the instruction, and the
  step's ingredient chips (reuse the `step.recipeIngredientIds → recipe.ingredients` lookup from
  [RecipeDetailView.vue:203-208](../frontend/src/views/RecipeDetailView.vue#L203-L208)).
- **Current step + done state:** a `doneStepIds` reactive set. Tapping a step (or its checkbox)
  toggles done; the **current** step is the first not-done step. The current step gets an accent
  left border and is `scrollIntoView`'d; completed steps are dimmed (`opacity-50`,
  strike-through optional). A floating **Next step** button marks the current step done and scrolls
  to the next — the "tap-next" path.
- **Ingredient highlight:** the **current** step's ingredient chips render filled/`color="primary"`;
  all other chips stay `variant="tonal"` — "current step's ingredients highlighted" (SPEC §7).
- **Wake lock:** `useWakeLock()` (§3.5) keeps the screen awake while mounted, where supported.
- **Finish:** a persistent footer **Mark as cooked & finish** button (respecting the bottom
  safe-area inset) calls `store.markCooked(id)`, fires a success snackbar (§4/§5), and
  `router.replace({ name: 'recipe-detail', params: { id } })`. Marking is also reachable mid-cook;
  it does not require all steps to be done.

> Step "done" state is **ephemeral** (component-local) — it is cooking-session UI, not persisted.
> Out of scope: timers, voice control, and persisting per-step progress (§10).

### 3.5 `useWakeLock` composable

`frontend/src/composables/useWakeLock.js` — a small wrapper around the
[Screen Wake Lock API](https://developer.mozilla.org/docs/Web/API/Screen_Wake_Lock_API), guarded
for unsupported browsers (notably iOS Safari < 16.4) so it degrades to a no-op.

```js
import { ref, onMounted, onUnmounted } from 'vue'

export function useWakeLock() {
  const isSupported = typeof navigator !== 'undefined' && 'wakeLock' in navigator
  const sentinel = ref(null)

  async function acquire() {
    if (!isSupported) return
    try {
      sentinel.value = await navigator.wakeLock.request('screen')
    } catch {
      // user gesture / power-save can reject; non-fatal — screen simply may sleep
      sentinel.value = null
    }
  }

  async function release() {
    try { await sentinel.value?.release() } catch { /* ignore */ }
    sentinel.value = null
  }

  // Re-acquire when the tab becomes visible again (the lock auto-releases on hide).
  function onVisibility() {
    if (document.visibilityState === 'visible') acquire()
  }

  onMounted(() => {
    acquire()
    document.addEventListener('visibilitychange', onVisibility)
  })
  onUnmounted(() => {
    document.removeEventListener('visibilitychange', onVisibility)
    release()
  })

  return { isSupported, acquire, release }
}
```

### 3.6 Entry points

- **Recipe detail:** add a prominent **Start cooking** button (e.g. `mdi-chef-hat`/`mdi-play`,
  `color="primary"`, near the chips row or as a sticky CTA) routing to
  `{ name: 'recipe-cooking', params: { id: recipe.id } }`. Keep the existing **Mark as cooked**
  button.
- **Meal plan view:** add **Start cooking** to the per-meal overflow menu
  ([MealPlanView.vue:59-68](../frontend/src/views/MealPlanView.vue#L59-L68)), routing with the
  meal's `recipeId` and `?portion=` set from `meal.portionSize`.

---

## 4. Mark as Cooked — close the gap

The cook action exists end-to-end; the only **missing surface** is the meal-plan recipe card's
overflow menu, and no cook action currently gives feedback.

- **MealPlanView overflow menu** ([MealPlanView.vue:63-67](../frontend/src/views/MealPlanView.vue#L63-L67)):
  add a **Mark as cooked** item (`mdi-chef-hat`) that calls a handler:

  ```js
  async function markMealCooked(meal) {
    await recipesStore.markCooked(meal.recipeId)
    await mealPlanStore.fetchActivePlan()           // refresh greyscale / recently-cooked
    ui.notify({ message: `Marked “${meal.recipeName}” as cooked`, color: 'success' })
  }
  ```

- **HomeView cook button** ([HomeView.vue:96-99](../frontend/src/views/HomeView.vue#L96-L99)):
  add the same success snackbar after `markCooked`.
- **No backend or store changes** — `POST /recipes/{id}/cook`, `markCooked`, and the
  `isRecentlyCooked` greyscale rule already exist and are reused as-is.

> The grayscale "cooked within 7 days" rule (CLAUDE.md, SPEC §6.1.1) is already applied via
> `recipesStore.isRecentlyCooked`; refreshing the active plan after marking keeps the badge/greyscale
> in sync.

---

## 5. Loading, Empty & Error States

### 5.1 Current state (audit)

| View | Loading | Empty | Fetch error |
|---|---|---|---|
| RecipesView | ✅ skeleton (`card` ×3) | ✅ | ✅ inline `v-alert` (closable) |
| RecipeDetailView | ✅ skeleton (`image, article`) | ✅ "Recipe not found" | ⚠️ conflated with not-found; no retry |
| ShoppingView | ✅ skeleton (`list-item-two-line` ×3) | ✅ (two variants) | ❌ none |
| HomeView | ❌ none | ✅ | ❌ none |
| MealPlanView | ❌ none | ✅ | ❌ none |
| MealPlanDetailView | ⚠️ spinner (`v-progress-circular`) | ✅ "No recipes" | ⚠️ "Plan not found"; no retry |
| PastPlansView | ⚠️ spinner | ✅ | ❌ none |
| MealPlanBuilderView / RecipeFormView / RecipeScrapePreviewView | *audit & align* | *audit & align* | *audit & align* |

> The remaining three views are not asserted above (not re-verified line-by-line for this spec);
> Phase 7 audits each and applies the standard pattern below where a state is missing.

### 5.2 Reusable components

Add three small shared components plus a UI store, then replace the ad-hoc blocks above.

```
frontend/src/
├── components/
│   ├── EmptyState.vue     NEW — icon + title + text + optional CTA
│   ├── ErrorState.vue     NEW — icon + message + "Try again" (emits retry)
│   └── AppSnackbar.vue     NEW — global snackbar, mounted once in App.vue
└── stores/
    └── ui.js               NEW — Pinia store: snackbar queue + notify()
```

**`EmptyState.vue`** — props `icon` (default `mdi-information-outline`), `title`, `text`,
`actionLabel?`, `actionTo?` (router target) / emits `action`. Mirrors the existing centred empty
blocks (`text-center py-12`, `mdi` icon at `size="64" color="grey-lighten-2"`).

**`ErrorState.vue`** — props `message` (default *"Something went wrong."*); renders a centred icon
(`mdi-alert-circle-outline`), the message, and a **Try again** button that `emit('retry')`. Views
wire `@retry` to re-invoke the failed fetch.

**`AppSnackbar.vue`** — a single `v-snackbar` bound to `useUiStore()` state; auto-dismiss
(~3.5s), colour from the notification, `role="status"` / `aria-live="polite"` (§9). Mounted once in
`App.vue` so it is available to every view (including full-screen Cooking Mode).

**`stores/ui.js`** — composition store:

```js
export const useUiStore = defineStore('ui', () => {
  const snackbar = ref({ show: false, message: '', color: 'success' })
  function notify({ message, color = 'success' }) {
    snackbar.value = { show: true, message, color }
  }
  return { snackbar, notify }
})
```

### 5.3 Standard patterns to apply

- **Loading:** prefer a **shaped `v-skeleton-loader`** over a bare spinner. Replace the
  `v-progress-circular` in [MealPlanDetailView.vue:4-6](../frontend/src/views/MealPlanDetailView.vue#L4-L6)
  and [PastPlansView.vue:5-7](../frontend/src/views/PastPlansView.vue#L5-L7) with list/card
  skeletons. Add skeletons to **HomeView** and **MealPlanView** (active-plan card skeletons).
- **Empty:** route ad-hoc empty blocks through `EmptyState` (keep existing copy/icons).
- **Error:** every fetch action exposes `error`; views render `ErrorState` with `@retry` calling
  the fetch again. Distinguish **error** (network/5xx → `ErrorState`) from **legitimately empty /
  not-found** (→ `EmptyState`) — fixing the current conflation in RecipeDetailView /
  MealPlanDetailView.
- **Feedback:** mutations (cook, add/delete custom item, regenerate, create/update/delete recipe)
  confirm via `ui.notify` on success and on failure.

---

## 6. Error Handling & Retry

The Axios instance already normalises failures to `{ status, message, original }`
([api.js:35-44](../frontend/src/services/api.js#L35-L44)). Phase 7 builds the **UX** on top of it —
the interceptor stays as-is (it must not import a Pinia store, to avoid a circular dependency).

- **Fetch actions** (`fetchRecipes`, `fetchRecipe`, `fetchActivePlan`, `fetchPlan`, `fetchPlans`,
  shopping `fetchActive`, …) already `try/catch` and set `error`. Audit each store so **every**
  fetch sets `error` on failure and clears it on success; views bind it to `ErrorState`.
- **Retry** is **manual** via `ErrorState`'s **Try again** (re-invokes the last fetch). Optionally
  add a single automatic retry for **idempotent GETs** on a network error (no response) in an Axios
  response-interceptor branch, with a short backoff — documented as optional; the manual path is the
  baseline.
- **Mutation actions** wrap the call and, on rejection, surface `ui.notify({ color: 'error', … })`
  so the user always gets feedback (today most mutations fail silently). The normalised
  `error.message` is the snackbar text.
- **404 semantics** are preserved: "no active plan" and "recipe/plan not found" remain **empty
  states**, never error toasts (mirrors the existing `fetchActive`/`fetchActivePlan` 404-as-null
  handling).

---

## 7. PWA — Manifest, Service Worker & Offline Recipe Cache

### 7.1 Decision: vite-plugin-pwa (Workbox) + cache-as-you-view

Use **vite-plugin-pwa** (Workbox under the hood) to generate the manifest and service worker,
precache the built app shell, and **runtime-cache recipe GETs and images as the user views them** —
so any recipe already opened is readable fully offline with no explicit "save" step.

### 7.2 Dependency & config

Add the dev dependency:

```bash
cd frontend
npm install -D vite-plugin-pwa
```

Register it in [vite.config.js](../frontend/vite.config.js#L8-L11) alongside the existing
plugins:

```js
import { VitePWA } from 'vite-plugin-pwa'

plugins: [
  vue(),
  vuetify({ autoImport: true }),
  VitePWA({
    registerType: 'autoUpdate',
    includeAssets: ['favicon.svg', 'icons.svg'],
    manifest: {
      name: 'RecipeApp',
      short_name: 'RecipeApp',
      description: 'Store recipes, plan meals, and generate shopping lists.',
      theme_color: '#2E7D32',          // matches the recipeLight primary / index.html theme-color
      background_color: '#F5F5F5',     // matches the theme background
      display: 'standalone',
      start_url: '/',
      icons: [
        { src: '/icons/pwa-192.png', sizes: '192x192', type: 'image/png' },
        { src: '/icons/pwa-512.png', sizes: '512x512', type: 'image/png' },
        { src: '/icons/pwa-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
      ],
    },
    workbox: {
      navigateFallback: '/index.html',
      runtimeCaching: [
        {
          // Recipe API reads — viewed recipes & the list become available offline.
          urlPattern: ({ url }) => url.pathname.includes('/api/v1/recipes'),
          handler: 'StaleWhileRevalidate',
          options: {
            cacheName: 'recipe-api',
            expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 7 },
            cacheableResponse: { statuses: [0, 200] },
          },
        },
        {
          // Recipe images served from the backend uploads path.
          urlPattern: ({ url }) => url.pathname.startsWith('/uploads/images/'),
          handler: 'CacheFirst',
          options: {
            cacheName: 'recipe-images',
            expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 30 },
            cacheableResponse: { statuses: [0, 200] },
          },
        },
      ],
    },
    devOptions: { enabled: false },     // SW disabled in `npm run dev`; enable ad-hoc to test
  }),
],
```

> **Cross-origin note (dev):** in development the SPA is `http://localhost:3000` and the API is a
> different origin (`VITE_API_BASE_URL = http://localhost:5000/api/v1`). In **production** Nginx
> proxies `/api` and `/uploads` under the same origin as the SPA (`frontend/nginx.conf`), so the
> same-origin `urlPattern`s above match. The offline-recipe feature is therefore validated against
> the **production** (Nginx) build, which is the deployment target — call this out in QA (§11).

### 7.3 Icons

`public/` currently holds only `favicon.svg` and `icons.svg`. Add PNG launcher icons referenced by
the manifest:

```
frontend/public/icons/
├── pwa-192.png
├── pwa-512.png
└── pwa-maskable-512.png      (safe-zone padded for Android maskable)
```

Generate from the existing brand mark / `favicon.svg` (green `#2E7D32`). This is an **asset task**,
not code.

### 7.4 Update flow

With `registerType: 'autoUpdate'` the service worker activates new builds automatically. Optionally
surface the virtual `registerSW` `onNeedRefresh` hook via `ui.notify` (*"A new version is available
— refresh."*) with a reload action. Registration is injected by the plugin; `index.html` keeps its
existing `theme-color` and apple-mobile meta tags ([index.html:13-17](../frontend/index.html#L13-L17)) —
the plugin injects the manifest `<link>`.

### 7.5 Scope

Offline is **read-only**: viewing already-cached recipes and the app shell. Mutations
(create/edit/cook/shopping changes) require connectivity and should fail with the standard error
snackbar (§6) when offline. Full offline-first with a write queue / background sync is **Future
Functionality** ("Offline-First Mode"), not Phase 7.

---

## 8. Responsive Polish

**Target widths:** iPhone SE (375 px), iPhone 15 (393 px), common Android (360 / 412 px); the
existing `max-width="600"` containers already centre nicely on larger screens.

Concrete checks & fixes:

- **Safe-area insets (iOS):** pad the bottom nav, FABs, and Cooking Mode footer with
  `env(safe-area-inset-bottom)` so the iOS home indicator doesn't overlap controls. Pair with
  `viewport-fit=cover` (extend the existing viewport meta in
  [index.html:8-11](../frontend/index.html#L8-L11)). Add a small global helper in
  `assets/main.css`.
- **Dynamic viewport height:** Cooking Mode uses `100dvh` (not `100vh`) so mobile browser chrome
  doesn't clip the sticky header/footer.
- **FAB vs bottom nav overlap:** the Shopping FAB already sits at `bottom: 80px`
  ([ShoppingView.vue:151-159](../frontend/src/views/ShoppingView.vue#L151-L159)); apply the same
  clearance pattern anywhere a FAB coexists with the bottom nav, plus the safe-area inset.
- **Tap targets:** ensure interactive icons are ≥ 44×44 px (Vuetify `size="small"` icon buttons are
  borderline — bump where needed, especially the meal overflow and image-edit buttons).
- **Text overflow:** keep the existing line-clamp on descriptions; verify long recipe/ingredient
  names wrap or truncate gracefully in cards, chips, and the cooking header.

---

## 9. Accessibility Audit

- **Accessible names on icon-only controls:** add `aria-label` (or visible text) to every
  icon-only `v-btn` — the meal overflow `mdi-dots-vertical`
  ([MealPlanView.vue:61](../frontend/src/views/MealPlanView.vue#L61)), the image-edit overlay
  ([RecipeDetailView.vue:23-29](../frontend/src/views/RecipeDetailView.vue#L23-L29)), the
  Shopping FAB and delete buttons, dialog close buttons, the home cook button, and the Cooking Mode
  close/next buttons.
- **Image alt text:** pass `:alt="recipe.name"` to recipe `v-img`s (list, detail, meal cards);
  decorative placeholders get `alt=""`.
- **Heading hierarchy:** one `<h1>` per page (the detail view uses `text-h5` as `h1`; list views
  start at `h2` with no `h1`) — make the top heading of each page an `h1` for screen-reader
  structure.
- **Live regions:** the snackbar is `role="status"` / `aria-live="polite"`; Cooking Mode's
  step-counter change is announced politely.
- **Focus management:** Vuetify dialogs trap and restore focus — verify the meal date/portion
  dialogs and the custom-item dialog restore focus to their trigger on close. Cooking Mode moves
  focus to its header on entry and back to **Start cooking** on exit.
- **Contrast:** primary green `#2E7D32` on white passes WCAG AA. The warning/secondary orange
  `#F57C00` **fails AA for small text on white** — keep it for fills/borders/large text only; don't
  use it as small body text.
- **Reduced motion:** wrap the global fade transition ([App.vue:31-41](../frontend/src/App.vue#L31-L41))
  and Cooking Mode auto-scroll in `@media (prefers-reduced-motion: reduce)` so they are disabled for
  users who request it.
- **Tooling:** run Lighthouse + axe DevTools on each route as the audit's acceptance check; record
  the score in the PR.

---

## 10. Docker Production Build — close the PWA gaps

The production build **already exists** and works: `frontend/Dockerfile` builds the Vue dist and
serves it via Nginx, which proxies `/api` to the backend; the `frontend` service is wired in
`docker-compose.yml`. Phase 7 only adds what the PWA needs:

- **Don't cache the service worker / manifest forever.** The current static rule
  ([nginx.conf:23-26](../frontend/nginx.conf#L23-L26)) sets `Cache-Control: public, immutable`
  for `*.js` — which would also pin `sw.js` for a year and break updates. Add a location that
  serves the service worker and manifest with `no-cache`:

  ```nginx
  # Service worker & manifest must always revalidate so updates ship.
  location ~* (sw\.js|workbox-.*\.js|manifest\.webmanifest)$ {
      add_header Cache-Control "no-cache";
      types { application/manifest+json webmanifest; }
  }
  ```

  (Place it **before** the broad static-asset rule so it takes precedence.) Hashed precache assets
  keep the existing 1-year immutable caching.
- **Gzip the manifest:** add `application/manifest+json` to the `gzip_types`
  ([nginx.conf:30-31](../frontend/nginx.conf#L30-L31)).
- **Verify** `npm run build` emits the manifest, service worker, and `public/icons/*` into `dist/`,
  and that the SPA fallback ([nginx.conf:8-10](../frontend/nginx.conf#L8-L10)) does not shadow
  `sw.js` (it won't — `try_files` checks the real file first).

No backend or compose changes are required.

---

## 11. Testing

### 11.1 Frontend tests (Vitest + Vue Test Utils, MSW)

```
frontend/src/
├── views/
│   └── CookingModeView.spec.js     NEW
├── composables/
│   └── useWakeLock.spec.js         NEW
├── components/
│   ├── EmptyState.spec.js          NEW
│   ├── ErrorState.spec.js          NEW
│   └── AppSnackbar.spec.js         NEW
├── stores/
│   └── ui.spec.js                  NEW
└── (extend existing)
    ├── views/MealPlanView.spec.js          + "Mark as cooked" / "Start cooking" menu items
    ├── views/RecipeDetailView.spec.js      + "Start cooking" CTA, error-vs-not-found, retry
    ├── views/HomeView.spec.js              + loading skeleton, cook snackbar
    ├── views/MealPlanDetailView.spec.js    + skeleton, ErrorState retry
    └── views/PastPlansView.spec.js         + skeleton
```

- **CookingModeView:** renders all steps in order; tapping a step marks it done (dims it) and moves
  the "current" emphasis to the next step; the current step's ingredient chips are highlighted;
  **Next step** advances; **Mark as cooked & finish** calls `markCooked(id)`, fires a snackbar, and
  navigates to `recipe-detail`; fetch failure shows `ErrorState` and **Try again** refetches.
- **useWakeLock:** acquires when `navigator.wakeLock` exists; is a silent no-op when absent;
  releases on unmount; re-acquires on `visibilitychange → visible`. (Stub `navigator.wakeLock` in
  the test — extend `src/test/setup.js` like the existing `ResizeObserver`/`visualViewport` stubs.)
- **EmptyState / ErrorState:** render props; `ErrorState` **Try again** emits `retry`.
- **ui store / AppSnackbar:** `notify` sets visible/message/colour; snackbar renders and
  auto-dismisses; `role="status"` present.
- **Extended views:** new skeletons render while loading; `ErrorState` shows on fetch error and its
  retry re-calls the fetch; the meal overflow menu exposes **Mark as cooked** (calls
  `markCooked` + refreshes plan) and **Start cooking** (navigates with `?portion=`).

### 11.2 Service worker / PWA — manual verification

Service workers and Workbox runtime caching aren't meaningfully unit-tested here; verify via a
**manual checklist** against the production (Nginx) build:

1. `docker compose up --build frontend api postgres` → load the app → DevTools ▸ Application shows a
   registered service worker and an installable manifest (icons, theme colour).
2. Open recipe A (cache it) → go offline (DevTools ▸ Network ▸ Offline) → reload → app shell loads
   and recipe A is viewable; an un-opened recipe B shows the standard offline error state.
3. Confirm `sw.js` / `manifest.webmanifest` respond with `Cache-Control: no-cache`; hashed assets
   with `immutable`.
4. Lighthouse PWA + Accessibility audits pass on the main routes (record scores in the PR).

### 11.3 Backend tests

**None** — Phase 7 adds no backend code (the cook endpoint and its tests already exist). Run the
existing suite to confirm no regressions.

Run with the existing commands: `npm test` / `npm run test:coverage` (frontend);
`dotnet test RecipeApp.Tests/RecipeApp.Tests.csproj` (backend, requires Docker for Testcontainers).

---

## 12. Out of Scope for Phase 7

- **Food Waste Tracking** — deferred (Phase 6 / `specs/phase-6-food-waste-tracking.md`).
- **Full offline-first** (write queue, background sync, offline mutations) — Future Functionality
  "Offline-First Mode". Phase 7 offline is **read-only**.
- **Push notifications**, **barcode scanning**, **shared plans**, **dark theme**, **imperial
  toggle**, **nutrition**, **tags/collections**, **arbitrary serving counts** — all Future
  Functionality (SPEC §9).
- **Cooking-mode extras** — timers, voice/hands-free control, and persisting per-step progress
  across sessions (step "done" state stays component-local).
- **Auth / multi-user** — single-user assumption unchanged.

---

## 13. Definition of Done

- [ ] **Cooking Mode:** `CookingModeView`, `useWakeLock` composable, `recipe-cooking` route, and
      full-screen chrome gating in `App.vue` implemented; **Start cooking** entry points on recipe
      detail and the meal overflow menu; large-type scroll list with current-step emphasis,
      per-step ingredient highlight, done-step dimming, **Next step**, wake-lock, and
      **Mark as cooked & finish**.
- [ ] **Mark as Cooked:** meal-plan overflow menu **Mark as cooked** added (refreshes the plan);
      home + meal cook actions give snackbar feedback.
- [ ] **States:** `EmptyState`, `ErrorState`, `AppSnackbar`, and the `ui` store added; bare spinners
      replaced with shaped skeletons; HomeView/MealPlanView gain loading skeletons; error vs
      empty/not-found disambiguated with **Try again** retry across views.
- [ ] **Error handling:** every fetch sets/clears `error`; every mutation reports success/failure via
      the snackbar; 404s remain empty states.
- [ ] **PWA:** `vite-plugin-pwa` configured (manifest, app-shell precache, recipe + image runtime
      caching); launcher icons added; app installs; a previously-viewed recipe is readable offline on
      the Nginx build.
- [ ] **Responsive:** safe-area insets + `100dvh` + FAB/nav clearance verified on SE / 15 / Android
      widths.
- [ ] **Accessibility:** icon controls have accessible names; images have `alt`; heading hierarchy,
      live regions, focus restore, contrast, and reduced-motion addressed; Lighthouse/axe pass.
- [ ] **Docker:** Nginx serves `sw.js`/`manifest` with `no-cache` (precache assets still immutable);
      manifest gzipped; production build verified end-to-end.
- [ ] Frontend tests added/extended and passing; backend suite green (no backend changes).
- [ ] `CLAUDE.md` "Current phase" line updated to **Phase 7** with structure notes; `SPEC.md` §7
      Phase 7 task checkboxes ticked (v1 feature-complete).
```