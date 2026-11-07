using System.Diagnostics.CodeAnalysis;

namespace RecipeApp.API.Enums;

/// <summary>
/// The one table of units with no real size, such as <c>pinch</c> or "to taste", whose lines a scrape stores as Unquantified
/// Ingredients because any amount in them would be invented, leaving count words such as <c>clove</c> to a human.
/// </summary>
public static class SizelessUnit
{
    private static readonly HashSet<string> Spellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "pinch",    "pinches",
        "dash",     "dashes",
        "smidgen",  "smidgens",
        "splash",   "splashes",
        "drizzle",  "drizzles",
        "sprinkle", "sprinkles",
        "handful",  "handfuls",
        "to taste",
    };

    /// <summary>True when the unit names no real size, in any case and ignoring surrounding whitespace.</summary>
    public static bool IsSizeless([NotNullWhen(true)] string? unit) =>
        unit is not null && Spellings.Contains(unit.Trim());
}
