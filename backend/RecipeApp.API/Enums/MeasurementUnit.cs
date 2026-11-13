using System.Diagnostics.CodeAnalysis;

namespace RecipeApp.API.Enums;

/// <summary>Physical dimension a measurement unit belongs to.</summary>
public enum UnitDimension { Mass, Volume, Count }

/// <summary>The authoritative set of storable measurement units, always stored in canonical spelling, so use <see cref="TryCanonicalise"/> on anything from outside.</summary>
public static class MeasurementUnit
{
    public const string Gram       = "g";
    public const string Kilogram   = "kg";
    public const string Millilitre = "ml";
    public const string Litre      = "L";
    public const string Piece      = "pcs";
    public const string Teaspoon   = "tsp";
    public const string Tablespoon = "tbsp";
    public const string Cup        = "cup";

    /// <summary>Millilitres per cup (the US legal cup), a definition rather than a preference, so it is not configurable.</summary>
    public const decimal MillilitresPerCup = 240m;

    /// <summary>Canonical spellings in display order. This is the storable set.</summary>
    public static readonly IReadOnlyList<string> All =
        [Gram, Kilogram, Millilitre, Litre, Piece, Teaspoon, Tablespoon, Cup];

    /// <summary>Dimension and base-unit factor (to g or ml) for every storable unit.</summary>
    public static readonly IReadOnlyDictionary<string, (UnitDimension Dimension, decimal BaseFactor)>
        Table = new Dictionary<string, (UnitDimension, decimal)>(StringComparer.Ordinal)
        {
            [Gram]       = (UnitDimension.Mass,   1m),
            [Kilogram]   = (UnitDimension.Mass,   1000m),
            [Millilitre] = (UnitDimension.Volume, 1m),
            [Litre]      = (UnitDimension.Volume, 1000m),
            [Teaspoon]   = (UnitDimension.Volume, 5m),
            [Tablespoon] = (UnitDimension.Volume, 15m),
            [Cup]        = (UnitDimension.Volume, MillilitresPerCup),
            [Piece]      = (UnitDimension.Count,  1m),
        };

    public const string Ounce      = "oz";
    public const string Pound      = "lb";
    public const string FluidOunce = "fl oz";
    public const string Pint       = "pint";
    public const string Quart      = "quart";

    /// <summary>Entry-only units: the Unit picker offers them, and every persistence path converts them to <c>g</c> or <c>ml</c> on save, so none is ever stored.</summary>
    public static readonly IReadOnlyList<string> EntryOnly = [Ounce, Pound, FluidOunce, Pint, Quart];

    /// <summary>Strict: true only for an entry-only unit in its canonical spelling.</summary>
    public static bool IsEntryOnly([NotNullWhen(true)] string? unit) =>
        unit is not null && EntryOnly.Contains(unit.Trim());

    /// <summary>True for every unit a request may carry, which is a storable unit or an entry-only one, both in canonical spelling.</summary>
    public static bool IsAccepted([NotNullWhen(true)] string? unit) => IsValid(unit) || IsEntryOnly(unit);

    /// <summary>Every unit a request may carry, for validation messages.</summary>
    public static readonly IReadOnlyList<string> Accepted = [.. All, .. EntryOnly];

    /// <summary>Units whose stored form may be resolved by ingredient context on import.</summary>
    public static readonly IReadOnlyList<string> ImportResolvable = [Cup];

    /// <summary>Spellings the scraper and LLM commonly emit, mapped to the canonical unit, excluding units with no fixed size such as <c>clove</c> or <c>pinch</c>.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gram"]        = Gram,       ["grams"]      = Gram,
        ["kilogram"]    = Kilogram,   ["kilograms"]  = Kilogram,   ["kilo"] = Kilogram,
        ["millilitre"]  = Millilitre, ["millilitres"] = Millilitre,
        ["milliliter"]  = Millilitre, ["milliliters"] = Millilitre,
        ["litre"]       = Litre,      ["litres"]     = Litre,
        ["liter"]       = Litre,      ["liters"]     = Litre,
        ["piece"]       = Piece,      ["pieces"]     = Piece,
        ["pc"]          = Piece,      ["each"]       = Piece,
        ["teaspoon"]    = Teaspoon,   ["teaspoons"]  = Teaspoon,
        ["tablespoon"]  = Tablespoon, ["tablespoons"] = Tablespoon, ["tbs"] = Tablespoon,
        ["cups"]        = Cup,
    };

    /// <summary>Every spelling <see cref="TryCanonicalise"/> will accept, derived here so an LLM prompt naming the units cannot drift from the check.</summary>
    public static readonly IReadOnlyList<string> AcceptedSpellings = [.. All, .. Aliases.Keys];

    /// <summary>Strict: true only for a canonical spelling, which guarantees stored units are canonical so consolidation can group by equality.</summary>
    public static bool IsValid([NotNullWhen(true)] string? unit) =>
        unit is not null && Table.ContainsKey(unit.Trim());

    /// <summary>Lenient: resolves canonical spellings in any case and known aliases to the canonical unit, and every path receiving a unit from outside canonicalises first and validates second.</summary>
    public static bool TryCanonicalise(string? unit, [NotNullWhen(true)] out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(unit)) return false;

        var trimmed = WithoutTrailingPeriod(unit);

        var exact = All.FirstOrDefault(u => string.Equals(u, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            canonical = exact;
            return true;
        }

        return Aliases.TryGetValue(trimmed, out canonical);
    }

    /// <summary>The unit trimmed and without the one abbreviating full stop a recipe writes after it (<c>tsp.</c>, <c>oz.</c>), for the lenient lookups only.</summary>
    public static string WithoutTrailingPeriod(string unit)
    {
        var trimmed = unit.Trim();
        return trimmed.Length > 1 && trimmed[^1] == '.' && trimmed[^2] != '.' ? trimmed[..^1].TrimEnd() : trimmed;
    }

    /// <summary>Dimension of a canonical unit, or null if the unit is not storable.</summary>
    public static UnitDimension? DimensionOf(string? unit) =>
        unit is not null && Table.TryGetValue(unit.Trim(), out var entry) ? entry.Dimension : null;

    /// <summary>Converts an amount to the dimension's base unit (g or ml). Null if the unit is unknown.</summary>
    public static decimal? ToBase(decimal amount, string? unit) =>
        unit is not null && Table.TryGetValue(unit.Trim(), out var entry) ? amount * entry.BaseFactor : null;
}
