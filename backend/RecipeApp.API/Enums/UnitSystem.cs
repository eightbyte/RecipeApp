namespace RecipeApp.API.Enums;

/// <summary>The units a single recipe's Measurements are read in, chosen per device and never stored.</summary>
public static class UnitSystem
{
    public const string Metric = "metric";
    public const string Us     = "us";

    public const string Default = Metric;

    public static readonly IReadOnlyList<string> All = [Metric, Us];

    public static bool IsValid(string value) => All.Contains(value);
}
