namespace RecipeApp.API.Enums;

/// <summary>How many times the whole recipe is made, applied at display or query time and never modifying base recipe amounts.</summary>
public static class BatchSize
{
    public const string Half    = "HALF";
    public const string Regular = "REGULAR";
    public const string Double  = "DOUBLE";

    public static readonly IReadOnlyList<string> All = [Half, Regular, Double];

    public static bool IsValid(string value) => All.Contains(value);

    /// <summary>Numeric multiplier for a Batch Size. Defaults to 1.0 for unknown values.</summary>
    public static decimal Multiplier(string value) => value switch
    {
        Half   => 0.5m,
        Double => 2.0m,
        _      => 1.0m,
    };
}
