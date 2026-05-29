using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Data;

/// <summary>
/// Seeds the database with a baseline ingredient catalogue and two sample recipes
/// so the app is usable immediately after first launch.
/// Only runs when the tables are empty.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Ingredients.AnyAsync()) return;

        // ── Ingredients ────────────────────────────────────────────────────────
        var ingredients = new List<Ingredient>
        {
            // Produce
            I("onion",          "Onion",          IngredientCategory.Produce,     "pcs"),
            I("garlic",         "Garlic",         IngredientCategory.Produce,     "pcs"),
            I("carrot",         "Carrot",         IngredientCategory.Produce,     "pcs"),
            I("tomato",         "Tomato",         IngredientCategory.Produce,     "pcs"),
            I("potato",         "Potato",         IngredientCategory.Produce,     "pcs"),
            I("spinach",        "Spinach",        IngredientCategory.Produce,     "g"),
            I("lemon",          "Lemon",          IngredientCategory.Produce,     "pcs"),
            I("fresh basil",    "Fresh Basil",    IngredientCategory.Produce,     "g"),
            I("capsicum",       "Capsicum",       IngredientCategory.Produce,     "pcs"),
            I("zucchini",       "Zucchini",       IngredientCategory.Produce,     "pcs"),

            // Meat & Seafood
            I("chicken breast",  "Chicken Breast",  IngredientCategory.MeatSeafood, "g"),
            I("ground beef",     "Ground Beef",     IngredientCategory.MeatSeafood, "g"),
            I("salmon fillet",   "Salmon Fillet",   IngredientCategory.MeatSeafood, "g"),
            I("bacon",           "Bacon",           IngredientCategory.MeatSeafood, "g"),

            // Dairy
            I("milk",            "Milk",            IngredientCategory.Dairy,       "ml"),
            I("butter",          "Butter",          IngredientCategory.Dairy,       "g"),
            I("cheddar cheese",  "Cheddar Cheese",  IngredientCategory.Dairy,       "g"),
            I("parmesan cheese", "Parmesan Cheese", IngredientCategory.Dairy,       "g"),
            I("eggs",            "Eggs",            IngredientCategory.Dairy,       "pcs"),
            I("cream",           "Cream",           IngredientCategory.Dairy,       "ml"),

            // Dry Goods
            I("pasta",           "Pasta",           IngredientCategory.DryGoods,    "g"),
            I("rice",            "Rice",            IngredientCategory.DryGoods,    "g"),
            I("plain flour",     "Plain Flour",     IngredientCategory.DryGoods,    "g"),
            I("breadcrumbs",     "Breadcrumbs",     IngredientCategory.DryGoods,    "g"),
            I("salt",            "Salt",            IngredientCategory.DryGoods,    "tsp"),
            I("black pepper",    "Black Pepper",    IngredientCategory.DryGoods,    "tsp"),
            I("paprika",         "Paprika",         IngredientCategory.DryGoods,    "tsp"),
            I("cumin",           "Cumin",           IngredientCategory.DryGoods,    "tsp"),
            I("dried oregano",   "Dried Oregano",   IngredientCategory.DryGoods,    "tsp"),

            // Canned
            I("diced tomatoes",  "Diced Tomatoes (400g can)", IngredientCategory.Canned, "g"),
            I("coconut milk",    "Coconut Milk (400ml can)",  IngredientCategory.Canned, "ml"),
            I("chickpeas",       "Chickpeas (400g can)",      IngredientCategory.Canned, "g"),
            I("tomato paste",    "Tomato Paste",              IngredientCategory.Canned, "tbsp"),
            I("beef stock",      "Beef Stock",                IngredientCategory.Canned, "ml"),
            I("chicken stock",   "Chicken Stock",             IngredientCategory.Canned, "ml"),

            // Condiments
            I("olive oil",       "Olive Oil",       IngredientCategory.Condiments,  "tbsp"),
            I("soy sauce",       "Soy Sauce",       IngredientCategory.Condiments,  "tbsp"),
            I("honey",           "Honey",           IngredientCategory.Condiments,  "tbsp"),
            I("balsamic vinegar","Balsamic Vinegar",IngredientCategory.Condiments,  "tbsp"),
            I("dijon mustard",   "Dijon Mustard",   IngredientCategory.Condiments,  "tsp"),
            I("worcestershire sauce","Worcestershire Sauce", IngredientCategory.Condiments, "tbsp"),
        };

        db.Ingredients.AddRange(ingredients);
        await db.SaveChangesAsync();

        // ── Sample Recipe 1: Spaghetti Bolognese ───────────────────────────────
        var bolognese = await CreateBologneseAsync(db, ingredients);
        await db.SaveChangesAsync();

        // ── Sample Recipe 2: Simple Stir-Fry Chicken ──────────────────────────
        var stirFry = await CreateStirFryAsync(db, ingredients);
        await db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Ingredient I(string name, string displayName, string category, string unit) =>
        new()
        {
            Id          = Guid.NewGuid(),
            Name        = name,
            DisplayName = displayName,
            Category    = category,
            DefaultUnit = unit,
            CreatedAt   = DateTime.UtcNow,
        };

    private static async Task<Recipe> CreateBologneseAsync(
        AppDbContext db, List<Ingredient> all)
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
            RI(recipe.Id, all, "beef stock",     100, "ml",    7),
            RI(recipe.Id, all, "dried oregano",    1, "tsp",   8),
            RI(recipe.Id, all, "olive oil",        2, "tbsp",  9),
            RI(recipe.Id, all, "salt",           0.5m, "tsp",  10),
            RI(recipe.Id, all, "black pepper",   0.5m, "tsp",  11),
            RI(recipe.Id, all, "parmesan cheese", 40, "g",     12, "grated, to serve"),
        };
        db.RecipeIngredients.AddRange(riList);

        // Index map for readability
        int pasta = 0, beef = 1, onion = 2, garlic = 3, carrot = 4,
            tomatoes = 5, paste = 6, stock = 7, oregano = 8,
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
            (5, "Stir in the tomato paste, diced tomatoes, beef stock, and oregano. Season with salt and pepper.",
                [paste, tomatoes, stock, oregano, salt, pepper]),
            (6, "Reduce heat and simmer for 20 minutes, stirring occasionally.",
                []),
            (7, "Cook the pasta according to packet instructions. Drain, reserving a cup of pasta water.",
                [pasta]),
            (8, "Toss the pasta with the sauce, adding a splash of pasta water if needed. Serve topped with grated parmesan.",
                [parmesan])
        );

        return recipe;
    }

    private static async Task<Recipe> CreateStirFryAsync(
        AppDbContext db, List<Ingredient> all)
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
            RI(recipe.Id, all, "capsicum",          1, "pcs",  1, "sliced"),
            RI(recipe.Id, all, "zucchini",          1, "pcs",  2, "sliced"),
            RI(recipe.Id, all, "garlic",            2, "pcs",  3, "minced"),
            RI(recipe.Id, all, "soy sauce",         3, "tbsp", 4),
            RI(recipe.Id, all, "honey",             1, "tbsp", 5),
            RI(recipe.Id, all, "olive oil",         2, "tbsp", 6),
            RI(recipe.Id, all, "rice",            200, "g",    7),
            RI(recipe.Id, all, "black pepper",   0.5m, "tsp",  8),
        };
        db.RecipeIngredients.AddRange(riList);

        int chicken = 0, capsicum = 1, zucchini = 2, garlic = 3,
            soy = 4, honey = 5, oil = 6, rice = 7, pepper = 8;

        AddSteps(db, recipe.Id, riList,
            (1, "Cook rice according to packet instructions.",
                [rice]),
            (2, "Mix soy sauce, honey and a pinch of pepper in a small bowl to make the sauce.",
                [soy, honey, pepper]),
            (3, "Heat half the oil in a wok or large frying pan over high heat. Add chicken and stir-fry for 4–5 minutes until cooked through. Remove and set aside.",
                [oil, chicken]),
            (4, "Add remaining oil to the wok. Stir-fry the capsicum and zucchini for 2 minutes.",
                [oil, capsicum, zucchini]),
            (5, "Add garlic and cook for 30 seconds.",
                [garlic]),
            (6, "Return chicken to the wok, pour over the sauce and toss to combine. Serve over steamed rice.",
                [chicken, soy, honey, rice])
        );

        return recipe;
    }

    private static RecipeIngredient RI(
        Guid recipeId, List<Ingredient> all, string name,
        decimal amount, string unit, int order, string? notes = null)
    {
        var ingredient = all.First(i => i.Name == name);
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
