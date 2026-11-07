using System.Diagnostics.CodeAnalysis;

namespace RecipeApp.API.Enums;

/// <summary>The one table of Sizeless Units, whose scraped lines are stored unquantified because any amount in them would be invented.</summary>
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
