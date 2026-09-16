namespace RecipeApp.API.Enums;

/// <summary>
/// Shopping list category groupings for ingredients.
///
/// <para><b>The order of <see cref="All"/> is the shopping list's aisle order</b> —
/// <c>ShoppingListService</c> sorts by index. A new category belongs beside its nearest relative,
/// not at the end.</para>
/// </summary>
public static class IngredientCategory
{
    public const string Produce       = "PRODUCE";
    public const string MeatSeafood   = "MEAT_SEAFOOD";
    public const string Dairy         = "DAIRY";
    public const string Canned        = "CANNED";
    public const string Frozen        = "FROZEN";
    public const string DryGoods      = "DRY_GOODS";

    /// <summary>Rice, oats, quinoa, couscous, barley, bulgur.</summary>
    public const string GrainsRice    = "GRAINS_RICE";

    /// <summary>Dried pasta and noodles, and jarred pasta sauces. Plain canned tomato sauce is <see cref="Canned"/>.</summary>
    public const string PastaSauces   = "PASTA_SAUCES";

    /// <summary>The baking and spices aisle: flour, sugar, leaveners, extracts, cocoa, dried herbs, spices, salt, pepper.</summary>
    public const string BakingSpices  = "BAKING_SPICES";

    public const string Bakery        = "BAKERY";
    public const string Condiments    = "CONDIMENTS";

    /// <summary>Jams, preserves, nut butters, honey and syrups.</summary>
    public const string JamNutButter  = "JAM_NUT_BUTTER";

    public const string Beverages     = "BEVERAGES";
    public const string CoffeeTea     = "COFFEE_TEA";

    /// <summary>Non-food supplies a recipe uses: foil, parchment, skewers, toothpicks, muffin liners.</summary>
    public const string Kitchen       = "KITCHEN";

    /// <summary>Non-food with no recipe use — paper towels, dish soap. Reached by custom shopping items.</summary>
    public const string Household     = "HOUSEHOLD";

    public const string Other         = "OTHER";

    public static readonly IReadOnlyList<string> All =
    [
        Produce, MeatSeafood, Dairy, Canned, Frozen,
        DryGoods, GrainsRice, PastaSauces, BakingSpices, Bakery,
        Condiments, JamNutButter, Beverages, CoffeeTea, Kitchen,
        Household, Other
    ];

    public static bool IsValid(string category) => All.Contains(category);
}
