using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Why a Stage 4 ingredient failed the quantity gate. Reported per line so "recipe rejected" is
/// always accompanied by which line and what went wrong.
/// </summary>
public enum SeedQuantityRejection
{
    /// <summary>The source line stated a quantity and the model returned none.</summary>
    MissingQuantity,

    /// <summary>The source line stated a quantity and the model returned zero or a negative one.</summary>
    NonPositiveQuantity,

    /// <summary>The source line stated a quantity and the model's unit is not storable.</summary>
    UnstorableUnit,

    /// <summary>
    /// The source line stated no quantity and the model supplied one anyway — the fabrication this
    /// gate exists to catch, which the rule it replaces waved through as a positive amount.
    /// </summary>
    InventedQuantity,

    /// <summary>
    /// The source line stated no quantity and the model correctly returned none, but attached a
    /// unit to it. A unit measuring nothing is the same fabrication in a different column.
    /// </summary>
    InventedUnit,
}

/// <summary>
/// The outcome of one ingredient line's quantity check. On acceptance it also carries the
/// measurement as it should be stored — the unit already canonicalised — so the caller does not
/// canonicalise a second time and risk disagreeing with the gate that admitted the row.
/// </summary>
public record SeedQuantityVerdict(
    bool IsAccepted,
    SeedQuantityRejection? Rejection,
    decimal? Amount,
    string? Unit)
{
    internal static SeedQuantityVerdict Accept(decimal? amount, string? unit) =>
        new(true, null, amount, unit);

    internal static SeedQuantityVerdict Reject(SeedQuantityRejection rejection) =>
        new(false, rejection, null, null);
}

/// <summary>
/// Stage 4's quantity gate, replacing Phase 9 §11.3's blanket <c>Any ingredient has Amount &lt;= 0</c>.
///
/// <para>The blanket rule fails 313 of the 1,089 parsed recipes (28.7%) before the model has made a
/// single mistake, because 432 ingredient lines state no quantity at all — <c>salt</c>,
/// <c>raisins</c>, <c>nonstick cooking spray</c>. That is a property of home cooking, not a parse
/// defect (Phase 9.1 §2).</para>
///
/// <para>What replaces it is <b>stricter</b>, not laxer. The exemption is earned from the source
/// text, never granted by the model's own output, so a model that invents <c>1 tsp</c> for a line
/// reading <c>salt</c> is now rejected where the old rule admitted it. The model cannot opt itself
/// out by emitting a null (Phase 9.1 §3.2).</para>
/// </summary>
public static class SeedQuantityGate
{
    /// <summary>
    /// Number words accepted as a stated quantity, matched only as a line's <i>leading</i> token.
    /// The restriction is what separates <c>Two cloves garlic</c> from <c>cut into one-inch
    /// pieces</c>. Measured, no line in the harvested corpus reaches this clause — it is here so
    /// the rule is correct rather than merely sufficient for one corpus.
    /// </summary>
    private static readonly HashSet<string> LeadingNumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven",
        "twelve", "half", "quarter", "dozen",
    };

    /// <summary>
    /// Whether the source line states a quantity, decided from the parsed text Stage 3 already
    /// carries verbatim. No re-parse and no new cache field.
    ///
    /// <para>A vulgar fraction counts. The corpus contains <c>¼ cup sliced almonds (optional)</c>,
    /// which carries no ASCII digit at all: a naive <c>\d</c> test would classify a real
    /// quarter-cup as unquantified, and the model would then be <i>instructed</i> to discard it.
    /// Silent data loss is worse than a loud failure (Phase 9.1 §2).</para>
    /// </summary>
    public static bool HasStatedQuantity(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return false;

        foreach (var character in sourceText)
        {
            if (char.IsAsciiDigit(character)) return true;
            if (IsVulgarFraction(character)) return true;
        }

        return LeadingNumberWords.Contains(LeadingWord(sourceText));
    }

    /// <summary>
    /// Checks one ingredient line's extracted measurement against what its source text actually
    /// stated. See the table in Phase 9.1 §3.2.
    /// </summary>
    /// <param name="sourceText">The verbatim ingredient line from <c>ParsedIngredientLine.Text</c>.</param>
    /// <param name="amount">The amount the model returned, or null if it reported none.</param>
    /// <param name="unit">The unit the model returned, in whatever spelling it used.</param>
    public static SeedQuantityVerdict Check(string? sourceText, decimal? amount, string? unit) =>
        HasStatedQuantity(sourceText)
            ? CheckQuantified(amount, unit)
            : CheckUnquantified(amount, unit);

    private static SeedQuantityVerdict CheckQuantified(decimal? amount, string? unit)
    {
        if (amount is not { } statedAmount)
            return SeedQuantityVerdict.Reject(SeedQuantityRejection.MissingQuantity);

        if (statedAmount <= 0m)
            return SeedQuantityVerdict.Reject(SeedQuantityRejection.NonPositiveQuantity);

        // Canonicalise before validating, as every path receiving a unit from outside does:
        // "teaspoons" and "ML" are the model's spelling of a storable unit, not a different one.
        if (!MeasurementUnit.TryCanonicalise(unit, out var canonicalUnit))
            return SeedQuantityVerdict.Reject(SeedQuantityRejection.UnstorableUnit);

        return SeedQuantityVerdict.Accept(statedAmount, canonicalUnit);
    }

    private static SeedQuantityVerdict CheckUnquantified(decimal? amount, string? unit)
    {
        if (amount.HasValue)
            return SeedQuantityVerdict.Reject(SeedQuantityRejection.InventedQuantity);

        if (!string.IsNullOrWhiteSpace(unit))
            return SeedQuantityVerdict.Reject(SeedQuantityRejection.InventedUnit);

        return SeedQuantityVerdict.Accept(null, null);
    }

    /// <summary>
    /// Vulgar fractions: <c>¼ ½ ¾</c> (U+00BC–U+00BE) and the Number Forms block's fraction range
    /// (U+2150–U+215E, <c>⅐</c> through <c>⅞</c>).
    /// </summary>
    private static bool IsVulgarFraction(char character) =>
        character is >= '¼' and <= '¾' or >= '⅐' and <= '⅞';

    /// <summary>
    /// The line's first run of letters. Split on anything non-alphabetic so a hyphenated
    /// <c>One-half cup</c> still leads with <c>one</c>.
    /// </summary>
    private static string LeadingWord(string sourceText)
    {
        var start = 0;
        while (start < sourceText.Length && !char.IsLetter(sourceText[start])) start++;

        var end = start;
        while (end < sourceText.Length && char.IsLetter(sourceText[end])) end++;

        return sourceText[start..end];
    }
}
