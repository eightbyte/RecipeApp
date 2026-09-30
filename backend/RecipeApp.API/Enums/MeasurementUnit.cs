using System.Diagnostics.CodeAnalysis;

namespace RecipeApp.API.Enums;

/// <summary>Physical dimension a measurement unit belongs to.</summary>
public enum UnitDimension { Mass, Volume, Count }

/// <summary>
/// The authoritative set of storable measurement units.
/// Mass base unit is g; volume base unit is ml. Stored values are always the
/// canonical spelling below — use <see cref="TryCanonicalise"/> on anything from outside.
/// </summary>
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

    /// <summary>
    /// Millilitres per cup. The app's canonical definition (US legal cup),
    /// matching the 240.0 factor RecipeScrapeService.UnitConversions used until Phase 8.5.1.
    /// A definition rather than a preference — changing it would silently reinterpret
    /// every already-stored value, so it is deliberately not configurable.
    /// </summary>
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

    /// <summary>
    /// Units whose stored form may be resolved by ingredient context on import
    /// (see <c>MeasurementConverter.ResolveForImport</c> and Phase 8.5.1 §5.4).
    /// </summary>
    public static readonly IReadOnlyList<string> ImportResolvable = [Cup];

    /// <summary>
    /// Spellings the scraper/LLM commonly emits, mapped to the canonical unit. Case-insensitive.
    /// Deliberately excludes units with no fixed size (clove, pinch, can, slice, bunch) — those
    /// cannot be canonicalised and must be corrected by a human or rejected by the seeder gate.
    /// </summary>
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

    /// <summary>
    /// Every spelling <see cref="TryCanonicalise"/> will accept — the canonical set plus the aliases
    /// above.
    ///
    /// <para>Exists for the one caller that has to <i>state</i> the vocabulary rather than test
    /// against it: an LLM prompt naming the units it may use. Listing only <see cref="All"/> there
    /// understates what the pipeline takes, and the prompt then contradicts itself the moment an
    /// example says <c>teaspoon</c>. Derived here so the promise and the check cannot drift.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedSpellings = [.. All, .. Aliases.Keys];

    /// <summary>
    /// Strict: true only for a canonical spelling. Validators use this, which guarantees stored
    /// units are always canonical — that is what lets consolidation group by plain equality.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? unit) =>
        unit is not null && Table.ContainsKey(unit.Trim());

    /// <summary>
    /// Lenient: resolves canonical spellings in any case ("ML", "Tbsp") and known aliases
    /// ("teaspoons") to the canonical unit. Returns false for anything else; the caller decides
    /// what that means. Every path receiving a unit from outside canonicalises first, validates second.
    /// </summary>
    public static bool TryCanonicalise(string? unit, [NotNullWhen(true)] out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(unit)) return false;

        var trimmed = unit.Trim();

        var exact = All.FirstOrDefault(u => string.Equals(u, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            canonical = exact;
            return true;
        }

        return Aliases.TryGetValue(trimmed, out canonical);
    }

    /// <summary>Dimension of a canonical unit, or null if the unit is not storable.</summary>
    public static UnitDimension? DimensionOf(string? unit) =>
        unit is not null && Table.TryGetValue(unit.Trim(), out var entry) ? entry.Dimension : null;

    /// <summary>Converts an amount to the dimension's base unit (g or ml). Null if the unit is unknown.</summary>
    public static decimal? ToBase(decimal amount, string? unit) =>
        unit is not null && Table.TryGetValue(unit.Trim(), out var entry) ? amount * entry.BaseFactor : null;
}
