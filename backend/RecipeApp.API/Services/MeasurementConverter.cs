using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services;

/// <summary>
/// All unit arithmetic for the import pipeline lives here. Two stages:
///
/// <para><b>Stage A — <see cref="ToCanonical"/></b>: customary → metric conversion plus alias
/// canonicalisation. No ingredient context, so it never sees a density. <c>cup</c> is deliberately
/// <i>not</i> converted here — a cup of flour and a cup of water are the same volume but very
/// different purchases, and only Stage B knows which is which.</para>
///
/// <para><b>Stage B — <see cref="ResolveForImport"/></b>: ingredient-aware, runs only once a
/// catalogue match has resolved an ingredient. Converts <c>cup</c> to grams when the catalogue
/// carries a bulk density, and otherwise leaves the measurement exactly as stated.</para>
///
/// See Phase 8.5.1 §5.4–§5.5.
/// </summary>
public class MeasurementConverter(IOptions<MeasurementOptions> options)
{
    private readonly MeasurementOptions _options = options.Value;

    /// <summary>Decimal places every converted amount is rounded to, matching RecipeIngredient.Amount's scale.</summary>
    private const int AmountDecimals = 3;

    /// <summary>
    /// Customary units with a fixed metric equivalent. Ounces and pounds are mass; fluid ounces,
    /// pints, quarts and gallons are overwhelmingly liquid. None carries the dry/wet ambiguity that
    /// keeps <c>cup</c> out of this table.
    /// </summary>
    private static readonly Dictionary<string, (string MetricUnit, double Factor)> UnitConversions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["oz"]           = (MeasurementUnit.Gram,       28.3495),
            ["ounce"]        = (MeasurementUnit.Gram,       28.3495),
            ["ounces"]       = (MeasurementUnit.Gram,       28.3495),
            ["lb"]           = (MeasurementUnit.Gram,      453.592),
            ["lbs"]          = (MeasurementUnit.Gram,      453.592),
            ["pound"]        = (MeasurementUnit.Gram,      453.592),
            ["pounds"]       = (MeasurementUnit.Gram,      453.592),
            ["fl oz"]        = (MeasurementUnit.Millilitre, 29.5735),
            ["fluid oz"]     = (MeasurementUnit.Millilitre, 29.5735),
            ["fluid ounce"]  = (MeasurementUnit.Millilitre, 29.5735),
            ["pt"]           = (MeasurementUnit.Millilitre, 473.176),
            ["pint"]         = (MeasurementUnit.Millilitre, 473.176),
            ["pints"]        = (MeasurementUnit.Millilitre, 473.176),
            ["qt"]           = (MeasurementUnit.Millilitre, 946.353),
            ["quart"]        = (MeasurementUnit.Millilitre, 946.353),
            ["quarts"]       = (MeasurementUnit.Millilitre, 946.353),
            ["gal"]          = (MeasurementUnit.Litre,        3.78541),
            ["gallon"]       = (MeasurementUnit.Litre,        3.78541),
            ["gallons"]      = (MeasurementUnit.Litre,        3.78541),
        };

    /// <summary>
    /// Every customary spelling <see cref="ToCanonical"/> converts. Exposed so a caller that has to
    /// state the unit vocabulary it will accept — the seed normaliser's prompt — can derive it from
    /// this table together with <see cref="MeasurementUnit.All"/>, rather than restating a list
    /// that drifts away from the one actually enforced.
    /// </summary>
    public static IReadOnlyCollection<string> ConvertibleUnits => UnitConversions.Keys;

    // ── Stage A ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a customary measurement to metric and canonicalises the unit spelling.
    /// A unit that neither converts nor canonicalises (<c>clove</c>, <c>pinch</c>, <c>can</c>)
    /// passes through unchanged, for a human to correct before the row is persisted.
    /// </summary>
    public static (decimal Amount, string Unit) ToCanonical(double rawAmount, string unit)
    {
        var trimmed = unit?.Trim() ?? string.Empty;

        if (UnitConversions.TryGetValue(trimmed, out var conversion))
            return (Round((decimal)(rawAmount * conversion.Factor)), conversion.MetricUnit);

        if (MeasurementUnit.TryCanonicalise(trimmed, out var canonical))
            return (Round((decimal)rawAmount), canonical);

        return (Round((decimal)rawAmount), trimmed);
    }

    // ── Stage B ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an import-resolvable unit (currently <c>cup</c> only) against the matched
    /// ingredient's bulk density. With a density the conversion is real arithmetic and the row is
    /// stored in grams; without one the measurement is kept exactly as stated, because any gram
    /// figure would be invented. Spoon measures are never resolved — they are more useful to a cook
    /// as spoons, and they consolidate correctly on their own.
    /// </summary>
    /// <param name="amount">Amount after Stage A.</param>
    /// <param name="unit">Canonical unit after Stage A.</param>
    /// <param name="gramsPerMillilitre">The matched ingredient's density, or null when unknown.</param>
    public (decimal Amount, string Unit) ResolveForImport(
        decimal amount, string unit, decimal? gramsPerMillilitre)
    {
        var trimmed = unit.Trim();

        if (!_options.ResolveCupsOnImport) return (amount, trimmed);
        if (!MeasurementUnit.ImportResolvable.Contains(trimmed)) return (amount, trimmed);
        if (gramsPerMillilitre is not > 0m) return (amount, trimmed);

        var millilitres = MeasurementUnit.ToBase(amount, trimmed);
        if (millilitres is null) return (amount, trimmed);

        return (MillilitresToGrams(millilitres.Value, gramsPerMillilitre.Value), MeasurementUnit.Gram);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a volume total in millilitres to grams. Used by Stage B and, independently of the
    /// import policy, by shopping-list consolidation for any volume unit (Phase 8.5.1 §7).
    /// </summary>
    public static decimal MillilitresToGrams(decimal millilitres, decimal gramsPerMillilitre) =>
        Round(millilitres * gramsPerMillilitre);

    private static decimal Round(decimal value) => Math.Round(value, AmountDecimals);
}
