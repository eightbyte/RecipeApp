using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace RecipeApp.API.Enums;

/// <summary>The one table of Count Words and size words, both of which a scraped line can carry in its unit's place and both of which are stored as <c>pcs</c>.</summary>
public static class CountWords
{
    /// <summary>Words that only count things, kept as the Source Measurement because their wording is all <c>pcs</c> loses; <c>can</c>, <c>jar</c> and the like are left out because they carry a real size.</summary>
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
        "bunch",  "bunches",
    };

    /// <summary>"Stick" counts celery or cinnamon but measures butter and margarine, whose stick has a real size pcs would lose.</summary>
    private static readonly HashSet<string> StickSpellings = new(StringComparer.OrdinalIgnoreCase) { "stick", "sticks" };

    private static readonly Regex SizedStickIngredient =
        new(@"\b(butter|margarine)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Words that describe an item's size rather than measure it, so a row carrying one has no unit and the word belongs in its notes.</summary>
    private static readonly HashSet<string> SizeSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "small",
        "medium",
        "large",
        "extra-large",
    };

    /// <summary>True when the unit is a Count Word for this ingredient, in any case and optionally led by a size word ("small bunch").</summary>
    public static bool IsCountWord([NotNullWhen(true)] string? unit, string ingredientName)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;

        var words     = unit.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var countWord = words.Length == 2 && IsSizeWord(words[0]) ? words[1] : unit.Trim();

        if (CountSpellings.Contains(countWord)) return true;
        return StickSpellings.Contains(countWord) && !SizedStickIngredient.IsMatch(ingredientName);
    }

    /// <summary>True when the unit is a size word, in any case and ignoring surrounding whitespace.</summary>
    public static bool IsSizeWord([NotNullWhen(true)] string? unit) =>
        unit is not null && SizeSpellings.Contains(unit.Trim());
}
