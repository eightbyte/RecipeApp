using System.Diagnostics.CodeAnalysis;

namespace RecipeApp.API.Enums;

/// <summary>The one table of Count Words and size words, both of which a scraped line can carry in its unit's place and both of which are stored as <c>pcs</c>.</summary>
public static class CountWords
{
    /// <summary>Words that only count things, kept as the Source Measurement because their wording is all <c>pcs</c> loses; <c>can</c>, <c>stick</c> and the like are left out because they carry a real size.</summary>
    private static readonly HashSet<string> CountSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "clove",  "cloves",
        "sprig",  "sprigs",
        "slice",  "slices",
        "head",   "heads",
        "piece",  "pieces",
        "whole",
        "ear",    "ears",
        "leaf",   "leaves",
        "stalk",  "stalks",
        "rib",    "ribs",
        "fillet", "fillets",
    };

    /// <summary>Words that describe an item's size rather than measure it, so a row carrying one has no unit and the word belongs in its notes.</summary>
    private static readonly HashSet<string> SizeSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "small",
        "medium",
        "large",
        "extra-large",
    };

    /// <summary>True when the unit is a Count Word, in any case and ignoring surrounding whitespace.</summary>
    public static bool IsCountWord([NotNullWhen(true)] string? unit) =>
        unit is not null && CountSpellings.Contains(unit.Trim());

    /// <summary>True when the unit is a size word, in any case and ignoring surrounding whitespace.</summary>
    public static bool IsSizeWord([NotNullWhen(true)] string? unit) =>
        unit is not null && SizeSpellings.Contains(unit.Trim());
}
