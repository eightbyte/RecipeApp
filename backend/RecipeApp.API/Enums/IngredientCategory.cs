namespace RecipeApp.API.Enums;

/// <summary>
/// Shopping list category groupings for ingredients.
/// </summary>
public static class IngredientCategory
{
    public const string Produce      = "PRODUCE";
    public const string MeatSeafood  = "MEAT_SEAFOOD";
    public const string Dairy        = "DAIRY";
    public const string Canned       = "CANNED";
    public const string Frozen       = "FROZEN";
    public const string DryGoods     = "DRY_GOODS";
    public const string Bakery       = "BAKERY";
    public const string Condiments   = "CONDIMENTS";
    public const string Beverages    = "BEVERAGES";
    public const string Other        = "OTHER";

    public static readonly IReadOnlyList<string> All =
    [
        Produce, MeatSeafood, Dairy, Canned, Frozen,
        DryGoods, Bakery, Condiments, Beverages, Other
    ];

    public static bool IsValid(string category) => All.Contains(category);
}
