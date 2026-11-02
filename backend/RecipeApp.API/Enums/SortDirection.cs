namespace RecipeApp.API.Enums;

/// <summary>Whether a list sort runs ascending or descending.</summary>
public static class SortDirection
{
    public const string Ascending  = "ASC";
    public const string Descending = "DESC";

    public const string Default = Descending;

    public static readonly IReadOnlyList<string> All = [Ascending, Descending];

    public static bool IsValid(string value) => All.Contains(value);
}
