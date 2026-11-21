# RecipeApp

A personal kitchen app: store recipes, plan which ones to cook, and turn that plan into a shopping
list.

## Language

### Recipes and ingredients

**Ingredient**:
An entry in the Ingredient Catalogue: a single foodstuff such as "onion", with its Category and
alternate names. It has no amount.
_Avoid_: Catalogue item, food

**Staple**:
An Ingredient the user always has on hand. Staples are left out of recipe suggestions and kept
apart on the shopping list.
_Avoid_: Pantry item, common item

**Recipe Ingredient**:
One line in a recipe's ingredient list: an amount and unit (or none), a note such as "finely
chopped", and the one Ingredient it refers to.
_Avoid_: Ingredient line, ingredient (when the line is meant)

**Measurement**:
A Recipe Ingredient's amount and unit in the app's own units, the form it is scaled and summed in.
_Avoid_: Quantity, converted amount

**Source Measurement**:
A Recipe Ingredient's amount and unit exactly as its source stated them, kept as a record only and
never scaled or summed.
_Avoid_: Original amount, raw amount

**Unquantified Ingredient**:
A Recipe Ingredient whose source states no amount, or only one with no real size such as "a pinch"
or "a dash". The absence is deliberate data, not an unknown amount.
_Avoid_: To taste, missing amount, zero amount

**Sizeless Unit**:
A unit that names no real size, such as "pinch", "dash" or "to taste". A Recipe Ingredient measured
in one is an Unquantified Ingredient, and its Source Measurement keeps the wording. A word that only
counts things, such as "clove", is a Count Word instead.

**Count Word**:
A word in a unit's place that only counts things, such as "clove" or "sprig". It is stored as `pcs`,
with the wording kept as the Source Measurement.

**Entry-only Unit**:
A customary unit the Unit picker offers, such as `oz`, `lb` or `pint`, that is converted to `g` or
`ml` on save and never stored. The measurement as typed is kept as the Source Measurement.
_Avoid_: Extended unit, imperial unit

**Servings**:
The number of people a recipe's stated amounts feed, or that a Planned Meal is made for. A recipe
is scaled by the servings wanted ÷ its own Servings.
_Avoid_: Yield, portions

**Batch Size**:
The recipe view's ½×, 1× and 2× shortcut onto Servings: a multiple of the whole recipe, never of
one serving. It is never stored.
_Avoid_: Portion, portion size

**Unit System**:
The units a single recipe's Measurements are read in: Metric (as stored) or US (`oz`/`lb`, and
`tsp`/`tbsp`/`cup`, as fractions). Each device chooses one on the recipe view, and Cooking Mode
follows it. It only changes the display; nothing is stored in US units.
_Avoid_: Imperial, measurement system

**Photo**:
The image of a recipe's prepared dish, stored with the recipe and shown with it. A Photo is one
kind of image; an image that does not show the finished dish is not a Photo.
_Avoid_: Picture, thumbnail, image (when the Photo is meant)

**Cooked**:
A recipe that has been marked as made. Cooking is recorded against the recipe, not against a
Planned Meal.
_Avoid_: Completed, made

**Recently Cooked**:
A recipe Cooked within the past 7 days.

### Meal planning

**Meal Plan**:
A named set of Planned Meals that a shopping list is built from.
_Avoid_: Menu, schedule

**Planned Meal**:
One occurrence of a recipe in a Meal Plan, with its own Servings and an optional date. It starts
at the recipe's Servings and keeps its own count when the recipe's Servings is edited. The same
recipe can appear as several Planned Meals. Opening a Planned Meal's recipe from anywhere shows it
at that Planned Meal's Servings.
_Avoid_: Meal plan recipe, plan entry

**Unscheduled**:
A Planned Meal with no date.
_Avoid_: Undated

**Active Meal Plan**:
The single Meal Plan currently in use. Creating a new Meal Plan closes the previous one.
_Avoid_: Current plan, open plan

**Past Meal Plan**:
A Meal Plan that has been closed. It is kept for reference.
_Avoid_: Archived plan, inactive plan, old plan

### Shopping

**Category**:
The grocery grouping an Ingredient or shopping list item belongs to, such as Produce or Dairy.
_Avoid_: Aisle, section

**Aisle Order**:
The fixed order in which Categories appear on a shopping list.

**Generated Item**:
A shopping list item derived from the Active Meal Plan. Regenerating the list replaces every
Generated Item.
_Avoid_: Recipe item, auto item

**Item Source**:
A Planned Meal that contributed to a Generated Item, recorded when the list is generated, with the
Measurement it contributed.
_Avoid_: Origin, contributor, source meal

**Custom Item**:
A shopping list item the user added by hand. Regenerating the list leaves it in place.
_Avoid_: Manual item, extra item

**Stale List**:
A shopping list whose Meal Plan has changed since the list was generated.
_Avoid_: Outdated list

**Needs Review**:
A shopping list item whose amounts could not be combined into a single quantity, so the user must
judge it.
_Avoid_: Conflict, error

**Purchased**:
A shopping list item the user has checked off.
_Avoid_: Bought, done

**List Refresh**:
Loading the shopping list again so one phone shows changes made on another, applied only while
the user isn't touching the screen.
_Avoid_: Sync, poll

**Pending Change**:
A shopping list write made on this phone that the server hasn't confirmed yet: a tick or untick, or
adding, editing or deleting a Custom Item. Every shopping list write waits as one until it is sent,
online or offline.
_Avoid_: Sync queue, outbox, offline write

### Getting recipes in

**Scrape**:
Turning one external source, a web page or pasted text, into a draft recipe that the user
reviews and confirms before it is saved.
_Avoid_: Parse, extract, import (for a single page)

**Site Reader**:
A reader for one site's own page data, adding detail its JSON-LD leaves out.
_Avoid_: Adapter, plugin, template, scraper

**Import**:
Loading a Recipe Pack whose recipes are already structured, with no review step.
_Avoid_: Scrape (for a pack), seed (as a user action)

**LLM Provider**:
Whoever answers Scraping's language-model requests, chosen once per deployment: the Model Server
(the default) or the Claude API. The MyPlate seed pipeline always uses the Model Server.
_Avoid_: Model Server (for the Claude API), backend, engine

**Model Server**:
The separate process on this network that hosts a local language model, used by the MyPlate seed
pipeline and, when it is the LLM Provider, by Scraping. It runs only while someone has started
it. The app works without it, except for those two.
_Avoid_: LLM service, companion app, Ollama, recipe reader

**LLM Call**:
One request to an LLM Provider, recorded with how long it took, the tokens it used and its
estimated cost, whether it succeeded or failed. A Scrape makes one or two. A request that never
reached the provider is not an LLM Call.
_Avoid_: Request log, usage event, inference

### Seeding and distribution

**Ingredient Catalogue**:
Every Ingredient the app knows, however it arrived. The First-Run Seed supplies the curated set,
and Scraping adds new ones.
_Avoid_: Ingredient database, master list

**Removed Ingredient**:
An Ingredient taken out of the Ingredient Catalogue but kept, because recipes and shopping lists
still refer to it. They keep showing it, while ingredient pickers, Scraping and Imports no longer
see it. Its name and aliases are free for another Ingredient, and it can be restored while no
active Ingredient has taken them.
_Avoid_: Deleted ingredient, archived ingredient

**Hand-set Density**:
An Ingredient's density, or its deliberate absence, as last saved by hand. The curated density
seed never replaces it.
_Avoid_: Custom density, override

**Recipe Pack**:
A file holding a set of recipes in the app's importable format.
_Avoid_: Sample pack, recipe bundle

**Sample Recipes**:
The 12 original recipes shipped as a Recipe Pack and given to every fresh install.
_Avoid_: Seed recipes, demo recipes

**MyPlate Library**:
The 1,123-recipe collection imported for personal use only. It is never distributed.
_Avoid_: Seed recipes, seed library

**First-Run Seed**:
What a fresh install receives exactly once: the Ingredient Catalogue and the Sample Recipes.
_Avoid_: Default data, bootstrap

### Administration

**Admin**:
The part of the app for controlling its data and appearance, reached from the gear in the top bar.
Anyone using the app can use it; it has no separate access. It is a list of Admin Tools.
_Avoid_: Settings, back office, control panel

**Admin Tool**:
One screen inside Admin, such as the LLM cost report or ingredient editing, filed under a section
(Data, Appearance or Reports).
_Avoid_: Admin page, admin module

**Theme**:
A named set of the 12 colour tokens with a light or dark base. Light and Dark are built in; others
are custom. Each device uses one.
_Avoid_: skin, palette, colour scheme
