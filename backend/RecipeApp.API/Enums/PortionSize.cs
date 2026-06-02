namespace RecipeApp.API.Enums;

/// <summary>
/// Portion multipliers applied at display/query time. Base recipe amounts are never modified.
/// </summary>
public static class PortionSize
{
    public const string Half    = "HALF";
    public const string Regular = "REGULAR";
    public const string Double  = "DOUBLE";

    public static readonly IReadOnlyList<string> All = [Half, Regular, Double];

    public static bool IsValid(string value) => All.Contains(value);

    /// <summary>Numeric multiplier for a portion size. Defaults to 1.0 for unknown values.</summary>
    public static decimal Multiplier(string value) => value switch
    {
        Half   => 0.5m,
        Double => 2.0m,
        _      => 1.0m,
    };
}
