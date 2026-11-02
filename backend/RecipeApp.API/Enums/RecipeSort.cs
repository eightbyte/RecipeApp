namespace RecipeApp.API.Enums;

/// <summary>The field the recipe list is ordered by.</summary>
public static class RecipeSort
{
    public const string Name       = "NAME";
    public const string DateAdded  = "DATE_ADDED";
    public const string LastCooked = "LAST_COOKED";

    public const string Default = DateAdded;

    public static readonly IReadOnlyList<string> All = [Name, DateAdded, LastCooked];

    public static bool IsValid(string value) => All.Contains(value);
}
