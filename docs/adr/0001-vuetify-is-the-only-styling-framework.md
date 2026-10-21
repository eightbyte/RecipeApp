---
status: proposed
date: 2026-10-18
issue: "#31"
---

# Vuetify is the frontend's only styling framework

The frontend loads Vuetify 3.12 and Bootstrap 5.3. Vuetify renders every component and owns the
theme. Bootstrap's full stylesheet is imported after it for "grid and utility classes only". The
app uses none of Bootstrap's grid. Every Bootstrap class in use has a Vuetify equivalent. Where the
two share a class name, Bootstrap overrides Vuetify with different values, and its base styles
restyle elements that Vuetify renders. We drop Bootstrap and keep Vuetify as the only styling
framework. `src/assets/main.css` keeps only app-specific rules. Bootstrap adds nothing the app
needs, and keeping it means two spacing scales, two typefaces and two colour palettes.

An earlier discussion kept both frameworks without recording why. Bootstrap arrived with the
Phase 1 scaffold (`bf7d967`).

## Evidence

Measured on 2026-10-18 on `develop` at `823776f` against the Vite dev server, in headless Chromium
at 383×856 on Windows 11. Each screen was rendered with the computed style of every element
recorded. Bootstrap's stylesheet was then disabled and the styles recorded again. Screens: Home,
Recipes, Recipe, Recipe form, Cooking Mode, Meal Plan, New Meal Plan, Past Meal Plans, Meal Plan
detail, shopping list, and the shopping list's add-item sheet. To reproduce one screen, run this
in the browser console:

```js
document.querySelector('style[data-vite-dev-id*="bootstrap"]').sheet.disabled = true
```

Neither stylesheet uses cascade layers, so plain specificity and source order decide every
conflict below. Bootstrap loads after `vuetify/styles` (`src/main.js`). Vuetify injects its
theme stylesheet at runtime, after both.

### Usage

The 22 SFC templates use 92 distinct classes, 565 times in all.

| Defined by | Classes | Uses | Notes |
|---|---|---|---|
| Vuetify only | 36 | 251 | Typography (`text-h6`, `text-caption`, …), `align-*`, `justify-*`, `pa-*`, `ml-*`/`mr-*`/`pl-*`, spacing steps 6 and up |
| Both, same rendered value | 23 | 159 | `d-flex`, `flex-*`, spacing steps 0–2, `text-center`, `text-truncate`, `text-white`. `text-primary` and `text-warning` render the same only because of stylesheet order (conflict 5) |
| Both, different rendered value | 11 | 90 | `mb-3` `mb-4` `mb-5` `mt-3` `mt-4` `pb-3` `pb-4` `pt-4` `px-4` `py-4` `rounded` |
| Bootstrap only | 4 | 37 | `gap-1` `gap-2` `gap-3` (Vuetify: `ga-*`), `visually-hidden` (Vuetify: `d-sr-only`) |
| The app | 17 | 27 | `main.css` helpers and scoped styles |
| Nothing | 1 | 1 | `date-chip` (`MealPlanView.vue:64`) has no rule anywhere |

Vuetify components also add classes at runtime. Bootstrap defines seven of them under the same
names: `bg-primary`, `bg-success`, `text-primary`, `text-secondary`, `text-success`, `text-info`
and `text-warning`.

The app uses no Bootstrap grid class, component or JavaScript.

### Conflicts

"Today" is what renders with both frameworks loaded. "Vuetify alone" is what renders with
Bootstrap's stylesheet disabled.

| # | Conflict | Today | Vuetify alone | Reach |
|---|---|---|---|---|
| 1 | Spacing steps 3–5 share names but not values: Bootstrap's scale is 4/8/16/24/48px, Vuetify's is 4px per step | `-3` 16px, `-4` 24px, `-5` 48px | 12px, 16px, 20px | 89 uses in 17 files |
| 2 | Bootstrap's spacing classes stop at 5, so Vuetify-only names sit beside them on a different scale | `mb-5` 48px is larger than `mb-6` 24px. `px-4` 24px and `pa-4` 16px appear on the same screens | consistent | — |
| 3 | `gap-*` is defined twice. Bootstrap's rule is `!important` and `main.css`'s is not, so the `main.css` helpers never apply | `gap-3` 16px | `gap-3` 12px (from `main.css`) | 36 uses in 15 files |
| 4 | `rounded` | 6px | 4px | 1 use |
| 5 | Theme colour classes (`bg-primary`, `text-success`, …). Bootstrap's versions use its own palette: its primary is blue `#0d6efd` | Vuetify's theme colour, only because Vuetify's runtime theme stylesheet is injected after Bootstrap's | Vuetify's theme colour | every themed component |
| 6 | Base style `hr { margin: 1rem 0 }` lands on every `<v-divider>`, which sets no margin of its own | 16px above and below | 0 | 24 dividers across Recipe, Meal Plan, Meal Plan detail and the shopping list (#22) |
| 7 | Base style on `h1`–`h6`: `margin-bottom: .5rem`. Size, weight and line-height are overridden by the Vuetify typography class on every heading | 8px below headings without a margin class | 0 | 5 headings: `HomeView.vue:13`, `MealPlanView.vue:21`, `MealPlanDetailView.vue:21`, `RecipeDetailView.vue:43`, `MealPlanBuilderView.vue:45` |
| 8 | Typeface. Bootstrap's `body` font stack (`system-ui`, …) replaces Vuetify's `html` font (`"Roboto", sans-serif`) for everything that inherits. Vuetify's typography classes (76 uses) set `"Roboto", sans-serif` themselves. The app does not load Roboto | Two typefaces on one screen. Windows, measured: Segoe UI for plain text, Arial for typography classes. iOS, expected: SF Pro and Helvetica. Stock Android, expected: Roboto for both | One typeface (`"Roboto", sans-serif`, falling back to Arial or Helvetica wherever Roboto isn't installed) | every screen |
| 9 | Base style on `body`: colour `#212529`, background `#fff`. `.v-application` sets its own, so only `#app` and the overlay container inherit these. Dialogs and bottom sheets are teleported to `<body>`, outside `.v-application` | `#212529` on overlay wrappers | browser default (black) | no visible text today. Matters for a dark theme (#30) |
| 10 | `:root { scroll-behavior: smooth }` (unless reduced motion is requested) | Every route change animates the scroll to top (`router` `scrollBehavior: () => ({ top: 0 })`) | jumps to top | every navigation |
| 11 | `visually-hidden` exists only in Bootstrap | `ShoppingView.vue:4` heading hidden | a 32px "Shopping list" heading appears above the list | 1 use |
| 12 | Base styles for elements no template uses raw: `a` (Bootstrap blue, underlined), `ul`/`ol` (32px indent, 16px below), `p` (16px below), `small` (14px), `label` (`inline-block`) | Vuetify components that render these elements override them. Measured: no change on Vuetify-rendered `a`, `label`, `button` or `input` | — | any raw element added later |

Page heights at 383px width, today → Vuetify alone: Recipe 2261 → 1885px, shopping list 1886 →
1550px, Recipes 1928 → 1872px, Recipe form 999 → 943px. The other screens fit on one screen
either way.

### Theming (#30)

#30 needs one source of theme colours. Today there are two:

- **Vuetify's theme**, `recipeLight` in `src/plugins/vuetify.js`.
- **Bootstrap's `--bs-*` variables** on `:root`. They colour `body`, raw links, and Bootstrap's
  colour utility classes, and they have their own light/dark switch (`data-bs-theme`). With both
  frameworks kept, a Vuetify dark theme would leave Bootstrap's white body and dark text behind
  the overlay wrappers. Keeping the two in step would mean mapping `--bs-*` to Vuetify's theme
  variables, or toggling `data-bs-theme` alongside the Vuetify theme.

These colours are hard-coded outside both and stay #30's to move into the theme:
`RecipeDetailView.vue:275` and `CookingModeView.vue:347` (primary green at 8% and 4%);
`CookingModeView.vue:309`, `:322`, `:339` (black at 8% for shadows and a border); and the PWA
colours in `index.html` (`theme-color`) and `vite.config.js:24-25`.

## Considered options

**A. Keep both, with stated rules.** For example: use only class names that render the same in
both, and override Bootstrap's base styles in `main.css` as they are found (#22 plans the first).
A variant imports `bootstrap-utilities.min.css` (85 KB, 11 KB gzipped) instead of the full
`bootstrap.min.css` (232 KB, 31 KB gzipped). That removes the base-style conflicts (6–10 and 12)
but keeps 1–5 and the `--bs-*` palette.
*Cost:* lowest now. *Risk:* every rule is enforced by memory, not tooling. A class name renders
differently from Vuetify's documentation. Each base-style collision is found after it ships.
Themed colours stay correct only while stylesheet order holds. #30 has to keep two theme systems
in step. Either way, Bootstrap supplies no class or feature that Vuetify lacks.

**B. Drop Bootstrap. (Recommended.)**
*Cost:* about 130 class edits across 18 files. Most are mechanical renames. The typeface needs a
decision, and three docs need updating. Three PRs (see Consequences).
*Risk:* visual regressions that the jsdom unit tests cannot see. Mitigation: keep every rendered
value as it is today, except changes the maintainer accepts, and compare before/after
screenshots of every screen.

**C. Drop Vuetify.**
*Cost:* rewrite the whole UI. Vuetify renders 42 distinct components (291 tags) across all 22
SFCs, plus the app layout (`main.css` depends on `--v-layout-bottom`), the theme and the icon
aliases. 20 of the 29 spec files reference Vuetify directly. Bootstrap 5 ships no Vue components, so
replacing them means a further component library or hand-built components with Bootstrap's
JavaScript, which `frontend/CLAUDE.md` rules out.
*Risk:* high, and it reverses the project's "Vuetify first" convention.

## Consequences

The migration keeps today's rendered values, so the framework change and the design changes (#22,
#13) are reviewed separately. Each step below is one PR:

1. **Replace Bootstrap-only classes** (#34). `gap-1` → `ga-1`, `gap-2` → `ga-2`, `gap-3` → `ga-4`
   (16px, today's rendered value), `visually-hidden` → `d-sr-only`. Delete the `gap-*` helpers
   from `main.css`, which never apply. The new names exist only in Vuetify, so this lands with
   Bootstrap still loaded and changes nothing on screen.
2. **Set the app's typeface explicitly** (#35). The maintainer chooses the typeface. This lands before
   step 3, so that removing Bootstrap changes no typeface.
3. **Remove Bootstrap** (#36). Delete the import and the `bootstrap` dependency. Rename the conflicting
   spacing classes to the Vuetify class that renders today's value:

   | Today | Renders | Becomes |
   |---|---|---|
   | `mb-3` `mt-3` `pb-3` | 16px | `mb-4` `mt-4` `pb-4` |
   | `mb-4` `mt-4` `pb-4` `pt-4` `px-4` `py-4` | 24px | `mb-6` `mt-6` `pb-6` `pt-6` `px-6` `py-6` |
   | `mb-5` | 48px | `mb-12` |

   Rename steps 5 and 4 before step 3. Otherwise a `-3` already renamed to `-4` gets renamed
   again. Add `mb-2` to the five headings in conflict 7. Update `SPEC.md`, `CLAUDE.md`,
   `frontend/CLAUDE.md`, and the comments in `src/main.js` and `src/assets/main.css`.

   Accepted changes in that PR, which the reviewer confirms: divider margins drop to 0 (what #22
   step 1 asks for app-wide); the one `rounded` element goes from 6px to 4px; route changes jump to
   the top instead of scrolling smoothly.

Effects on the issues this review blocks:

- **#22** If Bootstrap is removed first, its step 1 is already done, as #22 anticipates. If #22
  lands first, the removal PR deletes its `.v-divider` margin override. Its notes on Bootstrap's
  spacing scale stop applying.
- **#13** No Bootstrap dependency of its own. Its before/after screenshots are simplest to take
  after the removal.
- **#30** Vuetify's theme becomes the only source of theme colours. The hard-coded colours listed
  under Theming remain.

Afterwards, a class name in a template means what Vuetify's documentation says it means.
