# Phase 9.2 — Recipe Sections (Multi-Part Recipes)

**Version:** 1.0
**Date:** 2026-09-08
**Status:** Rejects. Deemed not required and an acceptable artifact of the seed import.
**Depends on:** Phase 9 Stages 1–3 complete (the cached pages and the parsed corpus are the evidence base)
**Answers:** Phase 9.1 §9 Q3 · Phase 9 §22.1 item 3 · Phase 9 §10.2b
**Supersedes:** nothing. Phase 9 §10.2a's label-folding and §10.2b's heading-drop are both *replaced* by §5 below, but neither was wrong — they were the best available handling before the recipe had anywhere to put a section.

> **Why this is its own spec and not an edit to Phase 9.** A group heading is not a parse bug with
> a parse fix. The parser correctly identifies these lines; it discards them because
> `Recipe` has no vocabulary for "this part of the list belongs to the dressing". Giving it one is
> a schema change on a v1-feature-complete app, with a migration and a visible product change to
> the recipe form, the detail view and Cooking Mode. That is the same class of change as
> Phase 9.1 and it should be reviewable on the same terms.

---

## 1. The question under review

From Phase 9.1 §9 Q3 and the answer recorded against it:

> **Group headings (§6) — fix in Phase 9 Stage 4, or defer?** They are ~40 lines across the corpus
> and Phase 9.1 makes them pass the gate rather than fail it.
>
> — *this brings into question recipes with multiple parts. Can we add a recipe step type for
> header? Or is there an easier solution to indicate steps are for a different part of the recipe?
> Sounds like a 9.2 item potentially*

The question is right to widen the frame. **Dropping a heading and representing a part are not the
same problem, and the corpus shows the heading problem is the smaller half of it.** Two findings
decide the design, and both are measured in §2:

1. **The multi-part shape is real and it is on both lists.** 75 of 1,089 recipes (6.9%) carry a
   part structure. 56 of them express it in the *ingredient* list and 28 in the *step* list — so a
   step-only fix, which is what a header step type is, addresses the smaller side.
2. **A header *step type* is the more expensive of the two shapes and the less capable one.** It
   adds rows to a sequence that four consumers count, number and tick off, and it still has nothing
   to say about ingredients. §4.2 sets this out in full.

**Recommendation: a nullable `Section` label carried *on* each row of both lists** — no new rows, no
new table, no join between the two sides. §4 argues it; §3 shows what it is being preferred over.

---

## 2. Measured evidence

Measured against all 1,089 cached pages in `seed-data/myplate/raw/`, using the same selectors and
the same list-walking rules as [MyPlateRecipeParser](../backend/RecipeApp.API/Services/Seeding/MyPlateRecipeParser.cs),
so every count below is directly comparable to the parser's own behaviour.

### 2.1 Ingredient group headings

| | Result |
|---|---:|
| Pages examined | 1,089 |
| `<li>` items wholly wrapped in `<b>`/`<strong>` | 126 |
| — colon-terminated → **dropped by the parser today** | **86 on 56 pages** |
| — colon-less → **kept as ingredient lines today** | **40 on 26 pages** |
| `<li>` ending in a colon that is **not** wholly bold | **0** |
| Ingredient fields holding more than one `<ul>`/`<ol>` | 3 |

**The bold requirement costs nothing.** Zero colon-terminated ingredient items in the corpus are
unbolded, so §10.2b's conjunction never rejects a heading it should have caught. The colon is the
only load-bearing half of the rule.

**Grouping is not expressed structurally on the ingredient side.** Only 3 pages split ingredients
across more than one list; the other 1,086 put every item, headings included, in one flat `<ul>`.
So unlike the step side (§2.2), there is no markup boundary to read — the heading item *is* the
boundary.

Headings per recipe, and how much of the list each governs:

| Headings in the recipe | Recipes |
|---:|---:|
| 1 | 27 |
| 2 | 28 |
| 3 | 1 |

| | Result |
|---|---:|
| Groups introduced by a heading | 86 |
| Median group size | 5 items |
| Range | 1–17 items |
| Recipes whose list *opens* with a heading | 30 |
| Recipes with ungrouped items *before* the first heading | 26 |

That last pair matters for the rendering rule: **a section does not always start at the top of the
list.** 26 recipes list the main ingredients unlabelled and then introduce `For the Dressing:`
partway down. Whatever represents a section has to allow an unlabelled leading run.

The 40 colon-less bolded items are the residue §10.2b knowingly accepted. Classified by hand:

| | Lines | Examples |
|---|---:|---|
| Genuine heading | **27** | `Dressing`, `Salad`, `Topping`, `Cookie Crust`, `Dipping Sauce`, `Final Sauce`, `Optional Gravy`, `Other Necessary Tools/Equipment` |
| Real item (equipment) | 11 | `aluminum foil`, `craft sticks`, `toothpicks`, `wooden sticks`, `6 spoons`, `paper cups (3-ounce)` |
| Note, not an item | 2 | `Note: "Minced" means cut up into tiny pieces.` |

**These 27 cannot be separated from the 13 structurally.** On `frozen-banana-pops` the heading
`Other Necessary Tools/Equipment` and the two items under it, `craft sticks` and `foil`, are all
wholly bold and none carries a colon. Bolding is how that page marks both. The discriminator is
semantic — *is this the name of a component of the dish, or a thing you handle?* — which is why
§6 leaves these to Stage 4 and everything else to Stage 3.

### 2.2 Step section labels

| | Result |
|---|---:|
| Pages with one `<ol>` | 1,061 |
| Pages with two | 23 |
| Pages with three | 5 |
| **Pages with more than one** | **28** |
| Colon-terminated labels sitting between or before those lists | **53** |
| Bare label `<li>` *inside* a step `<ol>` | **0** |

**The step side is the one that does have a structural boundary.** A label is always a sibling
element between two lists, never a list item, so a label and the steps it governs are already
distinguishable without any text heuristic. The parser reads this correctly today — it just spends
the information immediately by concatenating the label onto the next step
([MyPlateRecipeParser.cs:257](../backend/RecipeApp.API/Services/Seeding/MyPlateRecipeParser.cs#L257)).

**7 of the 28 label a cooking *method*, not a part of the dish:**

`au-gratin-potatoes`, `baked-chicken-nuggets`, `basic-custard`, `dilled-fish-fillets`,
`granola-oatmeal-coconut-pecan`, `old-fashioned-bread-pudding`, `scalloped-potatoes-ii` — carrying
labels like `Microwave Method:`, `Conventional Oven:`, `Stovetop version:`, `Quickest Method:`.

This is a hard constraint on the design and it is easy to miss. A model that treats a label as a
*part* asserts that the parts compose into one dish — you make the crust **and** the filling. These
seven say the opposite: you take one route **or** the other. Any representation that implies
composition (a `RecipePart` entity, a per-part ingredient set, a per-part scaling) is wrong for a
quarter of the pages that have step labels. A representation that is only a *display label* is
correct for all 28, because the source's own words carry the meaning.

### 2.3 The two sides do not align

This is the finding that rules out the relational model.

| | Recipes |
|---|---:|
| Ingredient headings only | 47 |
| Step labels only | 19 |
| **Both** | **9** |
| **Either (the multi-part corpus)** | **75 (6.9%)** |

Of the 9 with both, the two label sets match textually on **2**:

| Slug | Ingredient headings | Step labels |
|---|---|---|
| `argentinean-grilled-steak-salsa-criolla` | For the sauce: / For the steak: | For the sauce: / For the steak: |
| `mock-southern-sweet-potato-pie` | Crust: / Filling: | Crust: / Filling: |
| `chicken-and-dumplings` | Dumplings: | **Make Dumplings:** |
| `cuban-salad` | For the Dressing: / For the Salad: | **To make the dressing: / To make the salad:** |
| `frosted-cake` | Icing: | **Cake: / Icing:** |
| `grilled-fish-tacos-peach-salsa` | For the salsa: / For the fish: | **Make the salsa:** / For the fish: |
| `pulled-pork-sandwich-red-cabbage-and-carrot-slaw` | For the Carrot Slaw: | **Pulled Pork: / Red Cabbage and Carrot Slaw:** |
| `stovetop-tamale-pie` | Quick Chili: / Tamale Pie: | **Prepare Chili: / Prepare Tamale Pie:** |
| `vegetarian-matzo-ball-soup` | Ingredients for Matzo Balls: / Ingredients for Broth: | **To Make Matzo Balls: / To Make Broth:** |

**A shared "part" that owns both its ingredients and its steps is unfilled for 66 of the 75
multi-part recipes, and on 7 of the remaining 9 it can only be filled by semantically matching
`Dumplings:` to `Make Dumplings:`.** The source does not model parts. It labels two lists,
independently, in whatever words suited the page author. The representation should say exactly
that much and no more.

---

## 3. Options

| | Approach | Verdict |
|---|---|---|
| **A** | **`RecipeStep.Type` enum** (`Instruction` \| `Header`) — a header is a contentless step row | **Rejected.** Solves the smaller half (28 recipes) and leaves the larger half (56) unaddressed. Puts a non-step into a sequence that `StepNumber` orders, `RecipeStepIngredient` joins, the §11.3 gate checks for contiguity, and Cooking Mode counts, numbers, tick-boxes and progress-bars. §4.2. |
| **B** | **Nullable `Section` on `RecipeStep` and `RecipeIngredient`** — a per-row label; the UI renders a heading where it changes | **Recommended.** One nullable column on each of two tables. No new rows, so ordering, numbering, step→ingredient links and the gate are all untouched. Covers both lists with one idea, keeps the two sides independent exactly as the source has them, and is inert (`null` everywhere) for every existing row and every recipe that has no parts. |
| **C** | **A `RecipePart` entity** owning its own ingredients and steps | **Rejected.** The data does not support it (§2.3): unfilled on 66 of 75, and requires semantic label matching on 7 of the remaining 9. It also asserts composition, which is false for the 7 method-alternative recipes (§2.2). Largest blast radius by a wide margin — new table, nested DTOs, ordering within *and* across parts, and a form rework from two flat lists to a tree. |
| **D** | **Do nothing structural** — keep folding step labels onto the first step, and have Stage 4 drop the 27 colon-less headings on semantics | **Viable and honest, and it is the cheap answer to "is there an easier solution".** It ships Stage 4 with no migration. What it costs is stated in §3.1. |

### 3.1 What Option D actually costs

D is not a straw man. It requires only the Stage 4 heading classifier that §6 needs anyway, and it
leaves the schema alone. Weigh it on three points:

- **The ingredient-side grouping — the larger half — is discarded permanently at import.** 86
  headings on 56 recipes become nothing. `cuban-salad` imports as 9 loose ingredients where the
  source reads as `For the Dressing` (5) then `For the Salad` (4), and nothing in the imported
  recipe says which five go in the jar.
- **It is recoverable, but not cheaply.** `raw/` is cached locally (112 MB, not committed), so the
  headings can be re-derived by re-parsing. But Stage 4 is a **4–9 hour** LLM pass and Stage 5
  persists ~1,000 rows, so adding sections after the import means re-running both or writing a
  backfill against 1,000 recipes. Doing it before Stage 4 costs one `--parse --force` (~4 s).
- **Steps still read slightly wrong.** `frosted-cake` step 9 is stored as
  `"Icing: Cream together cream cheese and milk until smooth."` — the heading is inside the
  instruction, so Cooking Mode reads the section name aloud as part of the step, and a user editing
  that step sees a label they cannot remove without losing it.

**The judgement:** the cost of D is not correctness, it is fidelity — and it is paid at the exact
moment (a one-time 1,000-recipe import) when it is cheapest to avoid. Recommend **B**. If the
schedule cannot take B before Stage 4, D is a defensible fallback **provided** the Stage 4
classifier from §6 ships regardless, because without it the 27 headings persist as junk
ingredients whichever option is chosen.

---

## 4. Design

### 4.1 The convention

> **A section is a display label carried on a row. Adjacent rows sharing a label form a group; the
> UI renders the label once, above the first row of the group. `null` means "no section", which is
> what almost every row is.**

```csharp
// Models/RecipeIngredient.cs
/// <summary>Optional group heading this row falls under, e.g. "For the Dressing". Null when the
/// recipe has no parts, which is the overwhelming majority.</summary>
public string? Section { get; set; }

// Models/RecipeStep.cs — identical field, independent value
public string? Section { get; set; }
```

Four properties follow from it being a label rather than an entity, and each is deliberate:

- **The two lists are independent.** A recipe may section its ingredients and not its steps (47
  recipes), or the reverse (19). Nothing joins `RecipeIngredient.Section` to `RecipeStep.Section`,
  and nothing requires the same wording on both. That is what §2.3 measured.
- **A section is not a container, so it cannot mislead.** `Microwave Method` as a label over three
  steps says what the page says. It never implies those steps compose with the `Oven Method` steps,
  because there is no part object claiming they do.
- **An unlabelled leading run is free.** The 26 recipes that list main ingredients before the first
  heading get `Section = null` on those rows and a value from the heading onwards. No sentinel, no
  synthetic `"Main"` group.
- **It is inert until used.** Every existing row and every single-part recipe carries `null`, and
  every view falls back to exactly today's rendering.

**Section text is stored as the source wrote it, minus the trailing colon.** `For the Dressing:` →
`For the Dressing`; `To make the dressing:` → `To make the dressing`. The second reads verbosely as
a heading, and rewriting it to `Dressing` would read better — but that is inventing text the source
did not write, which this project treats the same way it treats a fabricated gram figure (CLAUDE.md,
"never destroy the input"). See §13 Q2.

### 4.2 Why not a header step type

The suggestion in §1 is the natural first thought, and it is worth setting out precisely what it
costs, because the cost is not in the model. It is in the four places that treat the step list as a
list of things you *do*, plus the half of the problem it never reaches.

| Consumer | Today | With a header row |
|---|---|---|
| [`RecipeStep.StepNumber`](../backend/RecipeApp.API/Models/RecipeStep.cs) | 1-based, contiguous, *is* the display number | A header consumes a number, so either the UI shows `Step 4` on a heading, or `StepNumber` stops being the display number and every consumer must derive one |
| Phase 9 §11.3 gate | "step numbers are not contiguous from 1" → reject | Has to be redefined around which rows count |
| [CookingModeView.vue:192–207](../frontend/src/views/CookingModeView.vue#L192-L207) | `totalSteps`, `displayIndex`, progress % and `currentIndex` all count rows | A header is counted as a step to complete: `Step 3 / 12` overstates the work, the progress bar never reaches 100% without ticking a heading, and **Next step** stops on a row with nothing to do |
| [`RecipeStepIngredient`](../backend/RecipeApp.API/Models/RecipeStepIngredient.cs) | Composite key `(StepId, RecipeIngredientId)` | Meaningless for a header; a nullable relationship on a row that can never use it |
| Ingredient list | — | **Unaddressed.** The 56 recipes with ingredient headings — twice as many as have step labels — get nothing |

A `Section` column has none of these. It changes what a row *says about itself*, not how many rows
there are, so `StepNumber`, contiguity, the gate, the progress bar and the step→ingredient join all
keep their exact current meaning.

### 4.3 Rendering

One rule, both lists: **render the section heading before any row whose `Section` differs from the
previous row's.** A leading run of `null` renders no heading. This handles the 26 ungrouped-prefix
recipes and, incidentally, a hand-entered recipe that interleaves sections — it simply renders the
heading again, which is what the user typed.

| View | Change |
|---|---|
| [RecipeDetailView.vue:104](../frontend/src/views/RecipeDetailView.vue#L104) | Ingredient list: a subheader row on change of section |
| [RecipeDetailView.vue:132](../frontend/src/views/RecipeDetailView.vue#L132) | Step list: a subheader above the step card |
| [CookingModeView.vue:95](../frontend/src/views/CookingModeView.vue#L95) | Section shown as an overline beside `Step N`, on the first step of the group. Numbering stays global (`1…N`), never restarting per section — it is the ordering key |
| [RecipeFormView.vue](../frontend/src/views/RecipeFormView.vue) | An optional per-row section field on each ingredient row and step row (§4.4) |
| [RecipeScrapePreviewView.vue](../frontend/src/views/RecipeScrapePreviewView.vue) | Same field, so an imported section is visible and editable before confirm |
| `ShoppingView.vue` | **No change.** §4.5 |

### 4.4 The form affordance

The fiddly part, and the one place where "a label on a row" needs a small piece of UI design. Both
lists are flat `v-for` rows today ([RecipeFormView.vue:87](../frontend/src/views/RecipeFormView.vue#L87),
[:184](../frontend/src/views/RecipeFormView.vue#L184)).

**Recommendation: one optional `v-combobox` per row, labelled `Section`, whose items are the
sections already used elsewhere in that recipe.** A combobox rather than a text field because it
makes the common case — putting a second row into the group you just created — a tap rather than
retyping a string that has to match exactly. Free-text entry is still allowed, so a new section is
one line of typing.

This keeps both lists flat. The alternative, a nested "add a section" container that owns rows,
turns the form into a tree, which is a substantially larger change for a field that will be empty on
almost every recipe a user writes by hand.

### 4.5 What a section is explicitly not

- **Not a shopping-list grouping.** `ShoppingListService` consolidates one ingredient across every
  recipe in a plan; a label that is local to one recipe's presentation has no meaning there. Two
  recipes' `Dressing` sections are not the same dressing. **`ShoppingListService` is not touched.**
- **Not a scaling boundary.** Portion scaling multiplies `Amount` at display time and is indifferent
  to sections.
- **Not validated for structure.** No uniqueness, no contiguity, no requirement that a section on
  the ingredient list have a counterpart on the step list. The only rules are length and non-blank
  (§7).
- **Not a category.** `Ingredient.Category` is a catalogue property used by the shopping list.
  `RecipeIngredient.Section` is a per-recipe display label. They never interact.

---

## 5. Stage 3 — the structural half (free)

Stage 3 already identifies both shapes and then throws the information away. This is the whole of
the change on the deterministic side, and it is a **~4 second re-parse** of cached pages.

| Today | With sections |
|---|---|
| A colon-terminated bold `<li>` is `continue`d ([MyPlateRecipeParser.cs:181](../backend/RecipeApp.API/Services/Seeding/MyPlateRecipeParser.cs#L181)) | It sets the current section, is not emitted as a line, and every following line carries that section until the next heading |
| A step label is concatenated onto the next step ([:257](../backend/RecipeApp.API/Services/Seeding/MyPlateRecipeParser.cs#L257)) | It becomes the section for the steps of the list it introduces; the instruction text is left alone |

Parse-model changes:

```csharp
// SeedParseModels.cs:124
public record ParsedIngredientLine(string Text, string? Note, string? Section, bool IsEmphasised);

// SeedParseModels.cs:113 — steps stop being bare strings
public required IReadOnlyList<ParsedStep> Steps { get; init; }
public record ParsedStep(string Text, string? Section);
```

`IsEmphasised` records "this `<li>` was wholly bold but carried no colon" — the 40 candidates of
§2.1 that Stage 4 must judge. Carrying the flag rather than re-deriving it is what lets Stage 4
decide on **40 marked lines** instead of guessing across all 8,601.

> **Bumping `ParsedSeedRecipe.CurrentVersion` to 2 will not by itself force a re-parse.**
> [`HasParsed`](../backend/RecipeApp.API/Services/Seeding/SeedCacheStore.cs#L220) is a file-existence
> check and nothing compares the stored `Version` against the constant, so a stale v1 file is
> silently reused. Add the version comparison — a v1 file is treated as absent — so the cache is
> self-invalidating rather than relying on someone remembering `--parse --force`. Same latent gap
> exists on the manifest and state models ([SeedModels.cs:26,48](../backend/RecipeApp.API/Services/Seeding/SeedModels.cs#L26)).

**Expected Stage 3 result after the change: the totals do not move.** Ingredient lines stay at
**8,601** and steps at **6,639** — the 86 headings are already excluded from that count because the
parser drops them today, and a step label was already folded into a step rather than becoming one.
What changes is that the same rows carry sections: populated on 56 recipes' ingredients and 28
recipes' steps, with the label text removed from 53 instructions. **A moved total is a regression
signal, not progress.**

---

## 6. Stage 4 — the semantic half (the 40)

The residue Stage 3 cannot decide: **40 wholly-bold, colon-less ingredient lines on 26 pages**, of
which 27 are headings and 13 are real content (§2.1).

The fix starts at the schema, for exactly the reason Phase 9.1 §1.3 gives about `amount`: **the
model currently has nowhere to put the answer.** `RecipeSchemaJson`
([RecipeScrapeService.cs:31](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L31)) offers
an ingredient object with `name`, `amount`, `unit`, `notes` and nothing else, so a heading has to
come out as an ingredient. Grammar-constrained decoding then guarantees it does.

```jsonc
// ingredients[].
"section": {
  "type": ["string", "null"],
  "description": "The group heading this ingredient falls under, e.g. 'For the Dressing'. Null if the recipe has no parts."
}
// steps[].
"section": {
  "type": ["string", "null"],
  "description": "The section heading these steps fall under, e.g. 'Icing'. Null if the recipe has no parts."
}
```

`JsonSchemaGrammar` already emits `(string | null)` for this form (Phase 9.1 §1.3), so there is no
grammar work.

The Stage 4 prompt gains one instruction, scoped to the flagged lines: **a line marked as
emphasised that names a component of the dish rather than something you buy or handle is a section
heading — omit it from `ingredients` and use its text as the `section` of the lines that follow.**
Lines Stage 3 already resolved carry their section in; the model preserves it rather than
re-deriving it.

### 6.1 Gate

Two additions to §11.3, both narrow:

| Rule | Rationale |
|---|---|
| A section string that is non-null must be non-blank and ≤ 100 characters | Same shape as every other length rule |
| Ingredient count may fall below the parsed count by **the number of `IsEmphasised` lines**, in addition to the existing ±2 tolerance | Dropping a heading legitimately shortens the list. Without this, a recipe like `bugs-log` (3 headings dropped) trips "ingredient count differs by more than 2" and fails for doing the right thing |

Everything else in §11.3 is unchanged, and the Phase 9.1 gate rules stand as written.

### 6.2 Corpus verification

After Stage 4, assert: **no persisted ingredient row's display name equals a known section string
in the same recipe**, and the 27 headings of §2.1 appear as `Section` values rather than as
ingredient rows. A heading that survives as an ingredient is the failure this stage exists to catch,
and it is silent otherwise — Phase 9.1 makes it pass the amount gate as a null-amount row.

---

## 7. Work inventory

### Backend

| File | Change |
|---|---|
| `Models/RecipeIngredient.cs` | `+ string? Section` |
| `Models/RecipeStep.cs` | `+ string? Section` |
| `Data/AppDbContext.cs` | `HasMaxLength(100)` on both |
| `Data/Migrations/` | New migration `AddRecipeSections` — two nullable columns. No data change; no existing row affected |
| `DTOs/Recipes/CreateRecipeRequest.cs` | `RecipeIngredientRequest` and `RecipeStepRequest` gain `string? Section = null`. `UpdateRecipeRequest` reuses both records, so it needs no edit |
| `DTOs/Recipes/RecipeIngredientResponse.cs`, `RecipeStepResponse.cs` | `+ Section` |
| `DTOs/Scrape/ScrapeConfirmRequest.cs`, `ScrapePreviewResponse.cs` | `+ Section` on `ScrapeConfirmIngredient`, `ScrapeConfirmStep`, `ScrapePreviewIngredient`, `ScrapePreviewStep` |
| `DTOs/Mappings.cs:60,74` | Pass it through |
| `Validators/RecipeValidators.cs` | `RuleFor(x => x.Section).NotEmpty().MaximumLength(100).When(x => x.Section is not null)` on `RecipeIngredientRequestValidator` and `RecipeStepRequestValidator` |
| `Validators/ScrapeValidators.cs:24,71` | Same rule on `ScrapeConfirmIngredientValidator`. **There is no step validator class** — confirm-request steps are checked inside `ScrapeConfirmRequestValidator`'s `Custom` block, so the section check goes there beside the existing `StepNumber`/`Instruction` failures |
| `Services/RecipeService.cs:130,155` | Assign on create/update |
| `Services/RecipeScrapeService.cs:205,223` | Assign in `ConfirmAsync` |
| `Services/RecipeScrapeService.cs:31` | `RecipeSchemaJson` — `section` on ingredients and steps (§6) |
| `Services/Seeding/SeedParseModels.cs:113,124` | `ParsedStep`; `ParsedIngredientLine` gains `Section` + `IsEmphasised`; `CurrentVersion` → 2 |
| `Services/Seeding/MyPlateRecipeParser.cs:181,245–275` | Headings set a section instead of being dropped; labels set a section instead of being concatenated |
| `Services/Seeding/SeedCacheStore.cs:220` | Treat a parsed file of an older version as absent |
| `Services/Seeding/` (Stage 4, new) | Heading classification over `IsEmphasised` lines; the §6.1 gate rules |

`ShoppingListService`, `MeasurementConverter`, `MeasurementUnit`, `JsonSchemaGrammar`, and every
endpoint file: **unchanged.**

### Frontend

| File | Change |
|---|---|
| `views/RecipeDetailView.vue:104,132` | Subheader on change of section, both lists |
| `views/CookingModeView.vue:95` | Section overline on the first step of a group; numbering stays global |
| `views/RecipeFormView.vue:87,184` | Per-row `Section` combobox; round-trip through `form.ingredients` / `form.steps` |
| `views/RecipeScrapePreviewView.vue` | Same field |
| `views/ShoppingView.vue` | **No change** (§4.5) |

**Scale: ~16 backend files, 4 frontend files, 1 migration, 1 parse-cache version bump, plus tests.**
No endpoint contract changes shape; eight DTO records gain one optional nullable field, defaulted so
no existing caller breaks.

---

## 8. Blast radius outside Phase 9

As with Phase 9.1, this reaches the hand-entry and interactive-scrape paths, and that is intended
rather than incidental:

- **Hand-entered recipes can be sectioned.** A user writing a pie can label `Crust` and `Filling`.
  This is a product addition, small and optional, but it is one.
- **A scraped recipe may arrive with sections filled.** The schema change applies to Phase 3
  scraping too — a scraped page with `For the Sauce:` has always had this structure and has always
  had it flattened.
- **Existing rows are unaffected.** The migration adds two nullable columns; nothing backfills, and
  every current recipe renders exactly as it does today.

---

## 9. Out of scope

- **Reordering rows between sections in the form.** Sections follow row order; there is no
  drag-between-groups. The form has no drag reordering today either
  ([RecipeFormView.vue:92](../frontend/src/views/RecipeFormView.vue#L92) — "Drag handle future").
- **Per-section step numbering.** Numbering stays global (§4.3).
- **Linking an ingredient section to a step section.** Ruled out by §2.3.
- **Rewriting section wording** (`To make the dressing` → `Dressing`). See §13 Q2.
- **The 2 note-shaped bold lines** (`Note: "Minced" means…`). They persist as ingredients, same as
  the equipment lines Phase 9.1 §6 leaves in place.

---

## 10. Testing

### Backend — new

| Test | Asserts |
|---|---|
| `MyPlateRecipeParserTests` | A colon heading sets the section on following lines and emits no line of its own; a second heading switches it; lines before the first heading carry null; a step label becomes the section of its list and is absent from the instruction text; `IsEmphasised` is true for a colon-less bold item and false otherwise; the 3 multi-`<ul>` ingredient pages still read every list |
| `MyPlateRecipeParserTests` (regression) | `frosted-cake` still yields 11 steps and 14 ingredients, with `Cake` on steps 1–8, `Icing` on steps 9–11, no `"Cake:"`/`"Icing:"` prefix left in any instruction, and `Icing` as the section on its last 5 ingredients; `cuban-salad` still yields 9 ingredients and 3 steps, sectioned on both lists |
| `MyPlateRecipeParserTests` (Stage 4 hand-off) | `bugs-log` still yields 10 ingredient lines with **3 flagged `IsEmphasised`** and no section set — Stage 3 must not guess at a colon-less heading |
| `SeedCacheStoreTests` | A parsed file written at version 1 is treated as absent; a current-version file is loaded |
| `Stage4GateTests` | Ingredient count short by the emphasised-line count is accepted; short by more is rejected; a blank or over-length section is rejected |
| `RecipeIngredientRequestValidatorTests` / `RecipeStepRequestValidatorTests` | Null section passes; blank string fails; 101 characters fails; 100 passes |
| `RecipeServiceTests` | Section round-trips through create, read and update on both lists |
| `ShoppingListServiceTests` | Two recipes whose rows carry *different* sections for the same ingredient still consolidate into one row — proves the label is not a grouping key |

### Backend — existing suites to re-green

`MyPlateRecipeParserTests` (32) and `RecipeLibrarySeederTests` (23) both construct
`ParsedSeedRecipe` and will need the new `ParsedStep` shape. `RecipeServiceTests`,
`RecipeScrapeServiceTests` and `ScrapeValidatorTests` touch the affected DTOs.

### Frontend

| Test | Asserts |
|---|---|
| `RecipeDetailView.spec.js` | A section heading renders once per group, not once per row; a leading null run renders none; a recipe with no sections renders exactly as before |
| `CookingModeView.spec.js` | `Step N / Total` counts only steps; the progress bar reaches 100% without a heading being ticked |
| `RecipeFormView.spec.js` | Section round-trips as null when left empty; the combobox offers sections already used in the recipe |

### Corpus verification (not a unit test)

After `--parse --force`: **8,601 ingredient lines and 6,639 steps, both unchanged**, sections present
on 56 recipes' ingredients and 28 recipes' steps, 40 lines flagged `IsEmphasised`, and **1,089
recipes still parsing with zero failures**. The 27 colon-less headings leave the ingredient list at
Stage 4, not here.

---

## 11. Definition of done

- [ ] Migration applied; `Section` nullable on `RecipeIngredient` and `RecipeStep`, max length 100
- [ ] Both row validators accept null, reject blank and over-length
- [ ] Stage 3 sets sections from colon headings and step labels; no heading is emitted as a line; no label remains inside an instruction
- [ ] `ParsedSeedRecipe.CurrentVersion` = 2, and a stale-version parsed file is treated as absent
- [ ] `RecipeSchemaJson` declares nullable `section` on ingredients and steps; grammar verified to emit `null`
- [ ] Stage 4 classifies the `IsEmphasised` lines; the §6.1 count tolerance is implemented
- [ ] Detail view, Cooking Mode, form and scrape preview render and round-trip sections; Cooking Mode counts and progress are unchanged for a sectionless recipe
- [ ] `ShoppingListService` untouched, with a test proving sections do not affect consolidation
- [ ] Full backend and frontend suites green
- [ ] Corpus check after re-parse: 8,601 lines and 6,639 steps, both unchanged; 1,089 parsed, 0 failures
- [ ] Phase 9 §10.2a, §10.2b and §22.1 item 3 amended to point here; Phase 9.1 §6 and §9 Q3 marked resolved
- [ ] `CLAUDE.md` records the section convention and the Stage 3 behaviour change

---

## 12. Sequencing

Both 9.1 and 9.2 are prerequisites for Stage 4, and **they touch heavily overlapping files** —
`RecipeIngredient`, `RecipeIngredientRequest`, `RecipeIngredientResponse`, `ScrapeConfirmIngredient`,
`ScrapePreviewIngredient`, `Mappings.cs`, `RecipeService.cs`, `ConfirmAsync`, `RecipeSchemaJson`, and
four frontend views.

**Recommended order: 9.1, then 9.2, then Stage 4 — one branch, two commits, two migrations.**

- 9.1 first because it is the harder blocker: its defect is *forced* by the grammar, so Stage 4
  cannot produce correct output at all without it, whereas 9.2's defect degrades fidelity.
- Separate migrations because each should be revertible on its own.
- One branch because doing them a week apart means editing the same twelve files twice and
  re-greening the same suites twice.
- Both before Stage 4 because Stage 4 is a 4–9 hour pass over ~1,000 recipes and Stage 5 persists
  them. Fixing either afterwards means a re-run or a backfill (§3.1).

---

## 13. Open questions

1. **Confirm `Section` on the ingredient list, not only on steps.** The question as posed was about
   steps. The measurement says the ingredient side is twice as common (56 recipes vs. 28) and is the
   half that currently produces junk rows. Recommend **both**, in one migration.

2. **Should Stage 4 normalise section wording?** `To make the dressing` and `Ingredients for Matzo
   Balls` read verbosely as headings; `Dressing` and `Matzo Balls` read better. Recommend **no** —
   §4.1 stores the source's words, and a model rewriting headings is inventing text. The counter-
   argument is that this is display-only text with no provenance role, unlike an amount, so the
   risk is cosmetic. If normalising is wanted it should be a Stage 4 prompt instruction, not a
   parser rule.

3. **Should the form's section field be a combobox or a plain text field?** §4.4 recommends a
   combobox seeded from the sections already used in that recipe, on the grounds that exact string
   matching is what makes rows group. A plain text field is less code and more typos.

4. **Do the 7 method-alternative recipes need anything beyond a label?** §2.2 says a plain label is
   correct and safe for them, but it does leave `au-gratin-potatoes` presenting three alternative
   routes as one continuous 1-to-N step sequence in Cooking Mode. Recommend **no change in 9.2** —
   modelling "or" branches is a genuinely different feature — but it is worth recording as a known
   limitation rather than discovering it during the trial run.

5. **Should the trial run (Phase 9 §14) gain a section stratum?** Recommend **yes**, mirroring
   Phase 9.1 §9 Q4: of the 20 pinned slugs, at least 2 from the 56 with ingredient headings, 2 from
   the 28 with step labels, 1 from the 9 with both, and 1 from the 26 carrying colon-less bold
   candidates.
