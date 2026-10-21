# RecipeApp

A personal kitchen app: store recipes, plan which ones to cook, and turn that plan into a shopping
list.

## Language

### Recipes and ingredients

**Ingredient**:
An entry in the Ingredient Catalogue: a single foodstuff such as "onion", with its Category and
alternate names. It has no amount.
_Avoid_: Catalogue item, food

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
A Recipe Ingredient whose source states no amount, such as "salt" or "cooking spray". The absence
is deliberate data, not an unknown amount.
_Avoid_: To taste, missing amount, zero amount

**Servings**:
The number of people a recipe's stated amounts feed.
_Avoid_: Yield, portions

**Batch Size**:
How many times the whole recipe is made: a multiple of the recipe, never of one serving. Its
current values are Half, Regular and Double. Asking for a number of servings is a way of choosing
a Batch Size, not a separate setting.
_Avoid_: Portion, portion size, custom servings

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
One occurrence of a recipe in a Meal Plan, with its own Batch Size and an optional date. The same
recipe can appear as several Planned Meals.
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

### Getting recipes in

**Scrape**:
Turning one external source, such as a web page, into a draft recipe that the user reviews and
confirms before it is saved.
_Avoid_: Parse, extract, import (for a single page)

**Import**:
Loading a Recipe Pack whose recipes are already structured, with no review step.
_Avoid_: Scrape (for a pack), seed (as a user action)

### Seeding and distribution

**Ingredient Catalogue**:
Every Ingredient the app knows, however it arrived. The First-Run Seed supplies the curated set,
and Scraping adds new ones.
_Avoid_: Ingredient database, master list

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
