using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services;

/// <summary>A recipe ingredient row's Measurement and Source Measurement, as stored.</summary>
public readonly record struct StoredMeasurement(
    decimal? Amount, string? Unit, decimal? SourceAmount, string? SourceUnit);

/// <summary>
/// All unit arithmetic for imports and hand entry: Stage A converts customary units to metric without ingredient context, and
/// Stage B resolves <c>cup</c> to grams once a catalogue match supplies a density.
/// </summary>
public class MeasurementConverter(IOptions<MeasurementOptions> options)
{
    private readonly MeasurementOptions _options = options.Value;

    /// <summary>Decimal places every converted amount is rounded to, matching RecipeIngredient.Amount's scale.</summary>
    private const int AmountDecimals = 3;

    /// <summary>Customary units with a fixed metric equivalent, excluding <c>cup</c> because it carries the dry/wet ambiguity.</summary>
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
            // Every spelling the corpus and the model produce, plurals included.
            ["fl oz"]             = (MeasurementUnit.Millilitre, 29.5735),
            ["fl ozs"]            = (MeasurementUnit.Millilitre, 29.5735),
            ["fluid oz"]          = (MeasurementUnit.Millilitre, 29.5735),
            ["fluid ounce"]       = (MeasurementUnit.Millilitre, 29.5735),
            ["fluid ounces"]      = (MeasurementUnit.Millilitre, 29.5735),
            ["us fluid ounce"]    = (MeasurementUnit.Millilitre, 29.5735),
            ["us fluid ounces"]   = (MeasurementUnit.Millilitre, 29.5735),
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

    /// <summary>Every customary spelling <see cref="ToCanonical"/> converts, exposed so a prompt can derive its unit vocabulary from the enforced table instead of restating it.</summary>
    public static IReadOnlyCollection<string> ConvertibleUnits => UnitConversions.Keys;

    // ── Stage A ───────────────────────────────────────────────────────────────

    /// <summary>Converts a customary measurement to metric and canonicalises the unit spelling, passing through unchanged any unit that does neither, such as <c>clove</c> or <c>sprig</c>.</summary>
    public static (decimal Amount, string Unit) ToCanonical(double rawAmount, string unit)
    {
        var trimmed = unit?.Trim() ?? string.Empty;

        if (UnitConversions.TryGetValue(trimmed, out var conversion))
            return (Round((decimal)(rawAmount * conversion.Factor)), conversion.MetricUnit);

        if (MeasurementUnit.TryCanonicalise(trimmed, out var canonical))
            return (Round((decimal)rawAmount), canonical);

        return (Round((decimal)rawAmount), trimmed);
    }

    // ── Entry-only units ──────────────────────────────────────────────────────

    /// <summary>
    /// The measurement and Source Measurement a recipe ingredient row is stored with, converting an entry-only unit by Stage A
    /// and keeping the typed measurement as the source, unless the given source is that same measurement in another spelling.
    /// </summary>
    public static StoredMeasurement ToStored(decimal? amount, string? unit, decimal? sourceAmount, string? sourceUnit)
    {
        if (amount is { } typedAmount && MeasurementUnit.IsEntryOnly(unit))
        {
            var typedUnit = unit.Trim();
            var (storedAmount, storedUnit) = ToCanonical((double)typedAmount, typedUnit);

            // An imported "4 ounces" reopened as "4 oz" and saved unchanged keeps its wording.
            var sourceIsTheTypedMeasurement = sourceAmount == typedAmount
                && EntryUnitOf(storedAmount, storedUnit, sourceAmount, sourceUnit) == typedUnit;

            return sourceIsTheTypedMeasurement
                ? new StoredMeasurement(storedAmount, storedUnit, sourceAmount, sourceUnit!.Trim())
                : new StoredMeasurement(storedAmount, storedUnit, typedAmount, typedUnit);
        }

        var (countedAmount, countedUnit) = Models.RecipeIngredient.ToStoredMeasurement(amount, unit);
        return new StoredMeasurement(countedAmount, countedUnit, sourceAmount, sourceUnit?.Trim());
    }

    /// <summary>
    /// The entry-only unit a row's Source Measurement is stated in, in any Stage A spelling, provided it still converts to the
    /// stored measurement; null otherwise, so a row edited by hand after import reopens as stored rather than reverting.
    /// </summary>
    public static string? EntryUnitOf(decimal? amount, string? unit, decimal? sourceAmount, string? sourceUnit)
    {
        if (amount is not { } storedAmount || unit is null) return null;
        if (sourceAmount is not { } givenAmount || sourceUnit is null) return null;
        if (!UnitConversions.TryGetValue(sourceUnit.Trim(), out var conversion)) return null;

        var entryUnit = MeasurementUnit.EntryOnly.FirstOrDefault(u => UnitConversions[u] == conversion);
        if (entryUnit is null) return null;

        return ToCanonical((double)givenAmount, sourceUnit) == (storedAmount, unit.Trim()) ? entryUnit : null;
    }

    // ── Stage B ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an import-resolvable unit (currently <c>cup</c>) against the matched ingredient's density, keeping the measurement
    /// exactly as stated when there is none because any gram figure would be invented.
    /// </summary>
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

    /// <summary>Converts a volume total in millilitres to grams, used by import Stage B and independently by shopping-list consolidation.</summary>
    public static decimal MillilitresToGrams(decimal millilitres, decimal gramsPerMillilitre) =>
        Round(millilitres * gramsPerMillilitre);

    private static decimal Round(decimal value) => Math.Round(value, AmountDecimals);
}
