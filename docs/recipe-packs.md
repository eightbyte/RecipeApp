# Recipe packs

A **recipe pack** is a JSON file of recipes in one fixed format that any source can produce.
`backend/RecipeApp.API/seed-data/sample-recipes.json` is the pack every fresh install receives, and
`import-recipes` imports any other.

## Format

```jsonc
{
  "formatVersion": 1,
  "contentVersion": 1,
  "recipes": [
    {
      "name": "Spaghetti Bolognese",
      "description": "…",
      "servings": 4,
      "sourceUrl": null,                  // optional. The duplicate key on re-import
      "image": null,                      // optional. Path relative to the pack file
      "ingredients": [
        { "ingredient": "ground beef", "amount": 500, "unit": "g", "notes": null },
        { "ingredient": "salt", "amount": null, "unit": null, "notes": "to taste" }
      ],
      "steps": [
        { "instruction": "…", "ingredients": ["olive oil", "onion"] }
      ]
    }
  ]
}
```

- `formatVersion` is the file's shape. The loader rejects any value other than `1`.
  `contentVersion` is raised on every edit to the pack and is never rejected.
- `name`, `servings`, `ingredients` and `steps` are required. `description`, `sourceUrl` and `image`
  may be left out. Every ingredient row must state `amount` and `unit`, even when they are null, so a
  forgotten amount is an error and an unquantified row says so.
- **Ingredients are referenced by catalogue name.** A name resolves by ingredient name, then by alias.
  The catalogue must already be in the database.
- **Steps reference ingredients by name, not by position.** A step name resolves the same way, then
  links every row in that recipe that resolved to the same ingredient. A step that names an
  ingredient the recipe does not list is an error. Step numbers come from array order.
- Units are canonical `MeasurementUnit` values (`g`, `kg`, `ml`, `L`, `pcs`, `tsp`, `tbsp`, `cup`).
  `amount` and `unit` are both null or both set, and zero is never used. See
  `.claude/rules/measurements.md`.
- `SourceAmount` and `SourceUnit` are left null, because a pack states its amounts directly.

## Import rules

- **Persistence goes through the normal API path.** Each recipe becomes a `CreateRecipeRequest`, is
  checked by `CreateRecipeRequestValidator` and saved by `RecipeService.CreateAsync`. An imported
  recipe is exactly what a user could have saved by hand.
- **All or nothing per pack.** The whole pack is resolved and validated before anything is written,
  and every error is reported, not just the first. The writes then happen in one transaction.
  Errors include an unresolved ingredient, a bad unit, a validator failure, a missing or disallowed
  image, and a `sourceUrl` repeated within the pack.
- **Unknown ingredients** are rejected by default, listing every miss. With `--create-missing`, each
  becomes a new ingredient in category `OTHER` with no default unit, created inside the pack's
  transaction.
- **Duplicates.** A recipe whose `sourceUrl` matches an existing recipe is skipped and reported. With
  `--force` the existing recipe is replaced, except that a recipe a meal plan uses is never replaced
  (it is reported as in use, and the pack still succeeds). A recipe with no `sourceUrl` is always
  inserted, so re-importing such a pack duplicates it.
- **Images** are resolved relative to the pack file. Extension and size are checked up front against
  `ImageService`'s rules. Files are copied before the commit and deleted again if the transaction
  rolls back, so a failed pack leaves no orphans.

## Entry points

| Entry point | Pack | `--create-missing` | `--force` | Ledger |
|---|---|---|---|---|
| First run (`FirstRunSeeder`) | Built-in `sample-recipes.json` | Never | Never | `sample-recipes` |
| `dotnet run -- import-recipes <pack.json> [--create-missing] [--force]` | Any | Optional | Optional | Not used |

- **First run.** A failure aborts the step, logs every error, writes no ledger row and lets the app
  start. The step retries on the next start.
- **`import-recipes`** migrates, then runs a non-force `import-catalogue`, so it works on an empty
  database. It exits 0 on success, including when recipes are skipped or in use, and 1 on any
  validation failure.
- The built-in pack's path is `RecipePacks:SamplePackFilePath` in `appsettings.json`. A relative path
  resolves against the content root.

Code lives in `Services/RecipePacks/`: `RecipePackFile` (model), `RecipePackFileStore` (load),
`RecipePackImporter` (resolve, validate, write) and `ImportRecipesCommand` (CLI).
