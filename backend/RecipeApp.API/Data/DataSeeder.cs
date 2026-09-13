using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Data;

/// <summary>
/// Seeds two sample recipes so the app is usable immediately after first launch.
///
/// <para><b>It is no longer a second source of catalogue truth.</b> Phase 9.3 removed the 41-name
/// ingredient block this used to insert. Those names were written before the recipe corpus existed
/// and matched only 22.9% of it, and six named nothing in the library at all — three of them
/// British/Australian spellings of things the corpus has under another name (§2.2). The sample
/// recipes now resolve through the corpus-derived catalogue, where <c>capsicum</c> is an alias of
/// <c>bell pepper</c> rather than a second row sitting permanently beside it.</para>
///
/// <para><b>The entry guard keys on recipes, not ingredients.</b> Returning early when any
/// ingredient existed was a sound "already seeded" test while this was the only seeder. It is not
/// one any more: <see cref="IngredientCatalogueFileSeeder"/> runs first by design, so that guard
/// would now fire on every run and the sample recipes would silently never appear.</para>
/// </summary>
public static class DataSeeder
{
    /// <summary>
    /// The ingredients the two sample recipes use, and nothing else.
    ///
    /// <para>Each is resolved against the catalogue by name or alias first and inserted only when
    /// nothing matches — so a fresh install with the catalogue loaded gains no duplicate rows, and
    /// one without it still gets two working recipes.</para>
    /// </summary>
    private static readonly IReadOnlyList<SampleIngredient> SampleIngredients =
    [
        new("pasta",            "Pasta",            IngredientCategory.DryGoods,    "g"),
        new("ground beef",      "Ground Beef",      IngredientCategory.MeatSeafood, "g"),
        new("onion",            "Onion",            IngredientCategory.Produce,     "pcs"),
        new("garlic",           "Garlic",           IngredientCategory.Produce,     "pcs"),
        new("carrot",           "Carrot",           IngredientCategory.Produce,     "pcs"),
        new("diced tomatoes",   "Diced Tomatoes",   IngredientCategory.Canned,      "g"),
        new("tomato paste",     "Tomato Paste",     IngredientCategory.Canned,      "tbsp"),
        new("beef broth",       "Beef Broth",       IngredientCategory.Canned,      "ml"),
        new("dried oregano",    "Dried Oregano",    IngredientCategory.DryGoods,    "tsp"),
        new("olive oil",        "Olive Oil",        IngredientCategory.Condiments,  "tbsp"),
        new("salt",             "Salt",             IngredientCategory.DryGoods,    "tsp"),
        new("black pepper",     "Black Pepper",     IngredientCategory.DryGoods,    "tsp"),
        new("parmesan cheese",  "Parmesan Cheese",  IngredientCategory.Dairy,       "g"),
        new("chicken breast",   "Chicken Breast",   IngredientCategory.MeatSeafood, "g"),
        new("bell pepper",      "Bell Pepper",      IngredientCategory.Produce,     "pcs"),
        new("zucchini",         "Zucchini",         IngredientCategory.Produce,     "pcs"),
        new("soy sauce",        "Soy Sauce",        IngredientCategory.Condiments,  "tbsp"),
        new("honey",            "Honey",            IngredientCategory.Condiments,  "tbsp"),
        new("rice",             "Rice",             IngredientCategory.DryGoods,    "g"),
    ];

    /// <param name="Name">Normalised catalogue name, matched against <c>Name</c> or <c>Aliases</c>.</param>
    private record SampleIngredient(string Name, string DisplayName, string Category, string Unit);

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Recipes.AnyAsync()) return;

        var ingredients = await ResolveIngredientsAsync(db);

        // ── Sample Recipe 1: Spaghetti Bolognese ───────────────────────────────
        CreateBolognese(db, ingredients);
        await db.SaveChangesAsync();

        // ── Sample Recipe 2: Simple Stir-Fry Chicken ──────────────────────────
        CreateStirFry(db, ingredients);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Resolves every sample ingredient to a catalogue row, inserting only what is genuinely absent.
    ///
    /// <para>The alias arm is what fixes §2.2's real problem: with the catalogue loaded,
    /// <c>bell pepper</c> already answers to <c>capsicum</c> and <c>beef broth</c> to
    /// <c>beef stock</c>, so both spellings keep working and only one row exists.</para>
    /// </summary>
    private static async Task<Dictionary<string, Ingredient>> ResolveIngredientsAsync(AppDbContext db)
    {
        var catalogue = await db.Ingredients.ToListAsync();

        // Names first, then aliases with TryAdd, so a name always outranks an alias.
        var byName = new Dictionary<string, Ingredient>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in catalogue) byName[row.Name] = row;
        foreach (var row in catalogue)
            foreach (var alias in row.Aliases) byName.TryAdd(alias, row);

        var resolved = new Dictionary<string, Ingredient>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in SampleIngredients)
        {
            if (byName.TryGetValue(sample.Name, out var existing))
            {
                resolved[sample.Name] = existing;
                continue;
            }

            var created = new Ingredient
            {
                Id          = Guid.NewGuid(),
                Name        = sample.Name,
                DisplayName = sample.DisplayName,
                Category    = sample.Category,
                DefaultUnit = sample.Unit,
                CreatedAt   = DateTime.UtcNow,
            };

            db.Ingredients.Add(created);
            byName[sample.Name]   = created;
            resolved[sample.Name] = created;
        }

        await db.SaveChangesAsync();
        return resolved;
    }

    // ── Sample recipes ────────────────────────────────────────────────────────

    private static void CreateBolognese(AppDbContext db, Dictionary<string, Ingredient> all)
    {
        var recipe = new Recipe
        {
            Id          = Guid.NewGuid(),
            Name        = "Spaghetti Bolognese",
            Description = "A classic Italian meat sauce served over spaghetti.",
            Servings    = 4,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.Recipes.Add(recipe);

        var riList = new List<RecipeIngredient>
        {
            RI(recipe.Id, all, "pasta",          400, "g",     0),
            RI(recipe.Id, all, "ground beef",    500, "g",     1),
            RI(recipe.Id, all, "onion",            1, "pcs",   2, "finely diced"),
            RI(recipe.Id, all, "garlic",           3, "pcs",   3, "minced"),
            RI(recipe.Id, all, "carrot",           1, "pcs",   4, "finely diced"),
            RI(recipe.Id, all, "diced tomatoes", 400, "g",     5),
            RI(recipe.Id, all, "tomato paste",     2, "tbsp",  6),
            RI(recipe.Id, all, "beef broth",     100, "ml",    7),
            RI(recipe.Id, all, "dried oregano",    1, "tsp",   8),
            RI(recipe.Id, all, "olive oil",        2, "tbsp",  9),
            RI(recipe.Id, all, "salt",           0.5m, "tsp",  10),
            RI(recipe.Id, all, "black pepper",   0.5m, "tsp",  11),
            RI(recipe.Id, all, "parmesan cheese", 40, "g",     12, "grated, to serve"),
        };
        db.RecipeIngredients.AddRange(riList);

        // Index map for readability
        int pasta = 0, beef = 1, onion = 2, garlic = 3, carrot = 4,
            tomatoes = 5, paste = 6, broth = 7, oregano = 8,
            oil = 9, salt = 10, pepper = 11, parmesan = 12;

        AddSteps(db, recipe.Id, riList,
            (1, "Bring a large pot of salted water to a boil.",
                [salt]),
            (2, "Heat the olive oil in a large pan over medium heat. Add the onion and carrot and cook for 5 minutes until softened.",
                [oil, onion, carrot]),
            (3, "Add the garlic and cook for 1 more minute.",
                [garlic]),
            (4, "Increase heat to high, add the ground beef and cook until browned, breaking it up with a spoon.",
                [beef]),
            (5, "Stir in the tomato paste, diced tomatoes, beef broth, and oregano. Season with salt and pepper.",
                [paste, tomatoes, broth, oregano, salt, pepper]),
            (6, "Reduce heat and simmer for 20 minutes, stirring occasionally.",
                []),
            (7, "Cook the pasta according to packet instructions. Drain, reserving a cup of pasta water.",
                [pasta]),
            (8, "Toss the pasta with the sauce, adding a splash of pasta water if needed. Serve topped with grated parmesan.",
                [parmesan])
        );
    }

    private static void CreateStirFry(AppDbContext db, Dictionary<string, Ingredient> all)
    {
        var recipe = new Recipe
        {
            Id          = Guid.NewGuid(),
            Name        = "Chicken Stir-Fry",
            Description = "A quick and easy stir-fry with tender chicken and crisp vegetables.",
            Servings    = 2,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.Recipes.Add(recipe);

        var riList = new List<RecipeIngredient>
        {
            RI(recipe.Id, all, "chicken breast", 400, "g",     0, "sliced thinly"),
            RI(recipe.Id, all, "bell pepper",       1, "pcs",  1, "sliced"),
            RI(recipe.Id, all, "zucchini",          1, "pcs",  2, "sliced"),
            RI(recipe.Id, all, "garlic",            2, "pcs",  3, "minced"),
            RI(recipe.Id, all, "soy sauce",         3, "tbsp", 4),
            RI(recipe.Id, all, "honey",             1, "tbsp", 5),
            RI(recipe.Id, all, "olive oil",         2, "tbsp", 6),
            RI(recipe.Id, all, "rice",            200, "g",    7),
            RI(recipe.Id, all, "black pepper",   0.5m, "tsp",  8),
        };
        db.RecipeIngredients.AddRange(riList);

        int chicken = 0, bellPepper = 1, zucchini = 2, garlic = 3,
            soy = 4, honey = 5, oil = 6, rice = 7, pepper = 8;

        AddSteps(db, recipe.Id, riList,
            (1, "Cook rice according to packet instructions.",
                [rice]),
            (2, "Mix soy sauce, honey and a pinch of pepper in a small bowl to make the sauce.",
                [soy, honey, pepper]),
            (3, "Heat half the oil in a wok or large frying pan over high heat. Add chicken and stir-fry for 4–5 minutes until cooked through. Remove and set aside.",
                [oil, chicken]),
            (4, "Add remaining oil to the wok. Stir-fry the bell pepper and zucchini for 2 minutes.",
                [oil, bellPepper, zucchini]),
            (5, "Add garlic and cook for 30 seconds.",
                [garlic]),
            (6, "Return chicken to the wok, pour over the sauce and toss to combine. Serve over steamed rice.",
                [chicken, soy, honey, rice])
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RecipeIngredient RI(
        Guid recipeId, Dictionary<string, Ingredient> all, string name,
        decimal amount, string unit, int order, string? notes = null)
    {
        var ingredient = all[name];
        return new RecipeIngredient
        {
            Id           = Guid.NewGuid(),
            RecipeId     = recipeId,
            IngredientId = ingredient.Id,
            Amount       = amount,
            Unit         = unit,
            Notes        = notes,
            DisplayOrder = order,
        };
    }

    private static void AddSteps(
        AppDbContext db,
        Guid recipeId,
        List<RecipeIngredient> riList,
        params (int number, string instruction, int[] ingredientIndexes)[] steps)
    {
        foreach (var (number, instruction, indexes) in steps)
        {
            var step = new RecipeStep
            {
                Id          = Guid.NewGuid(),
                RecipeId    = recipeId,
                StepNumber  = number,
                Instruction = instruction,
            };
            db.RecipeSteps.Add(step);

            foreach (var idx in indexes)
            {
                if (idx >= 0 && idx < riList.Count)
                {
                    db.RecipeStepIngredients.Add(new RecipeStepIngredient
                    {
                        StepId             = step.Id,
                        RecipeIngredientId = riList[idx].Id,
                    });
                }
            }
        }
    }
}
