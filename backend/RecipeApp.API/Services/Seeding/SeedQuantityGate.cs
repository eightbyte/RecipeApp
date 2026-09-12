using System.Text.RegularExpressions;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;

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

    /// <summary>
    /// The source line states exactly one number and the model returned a different one —
    /// <c>3/4 cup peanuts</c> read as <c>3.75 cup</c>.
    ///
    /// <para>The only rejection here that catches a <i>plausible</i> answer. Every other one fails
    /// on shape: a missing amount, a unit that will not store. A fivefold misread of a fraction is
    /// a positive number in a valid unit, so nothing about it looks wrong — it just quietly
    /// multiplies every shopping list the ingredient lands in. Phase 9 §11.3's own justification
    /// for the gate.</para>
    /// </summary>
    ContradictedQuantity,
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

        var measurable = WithoutDimensions(sourceText);

        foreach (var character in measurable)
        {
            if (char.IsAsciiDigit(character)) return true;
            if (IsVulgarFraction(character)) return true;
        }

        return LeadingNumberWords.Contains(LeadingWord(measurable));
    }

    /// <summary>
    /// The line with its cut-size phrases removed, so a dimension is not mistaken for a quantity.
    ///
    /// <para>119 lines name a size in inches, and on all but two of them a real quantity appears as
    /// well — <c>6 medium russet potatoes, peeled and sliced into 1/4 inch slices</c> is quantified
    /// by its leading <c>6</c>, whichever way the inches are read. The two exceptions are the whole
    /// reason this exists: <c>carrot, sliced into 3 inch pieces</c> and
    /// <c>aluminum foil (10x12 inches square)</c> state no quantity at all, and counting their
    /// dimensions as one makes the gate demand an amount the line never gave. The model correctly
    /// returned none for both and lost its recipe for it.</para>
    ///
    /// <para>Deliberately <b>not</b> applied to <see cref="TryReadSoleNumber"/>. Stripping there
    /// would make more lines arithmetically checkable, which sounds like an improvement and is an
    /// untested widening of the one check that can reject a plausible answer. Two lines of evidence
    /// does not justify it.</para>
    /// </summary>
    private static string WithoutDimensions(string sourceText) =>
        DimensionPhrase.Replace(sourceText, " ");

    /// <summary>
    /// A measurement in inches: <c>3 inch</c>, <c>1/4 inch</c>, <c>2 1/2 inch</c>,
    /// <c>1/2-inch</c>, <c>(6-inch)</c>, <c>10x12 inches</c>.
    /// </summary>
    private static readonly Regex DimensionPhrase = new(
        @"\d+(?:\s*[xX]\s*\d+)?(?:\s+\d+/\d+|/\d+|\.\d+)?\s*-?\s*inch(?:es)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
    /// Whether the line names any unit of measurement at all — a storable one, or a customary one
    /// Stage A converts.
    ///
    /// <para>The evidence behind reading a bare count as pieces. <c>1 granny smith apple</c> and
    /// <c>16 lettuce leaves</c> name no unit because they are counting whole things, so a model
    /// that returns the number and no unit has read them correctly. <c>2 cups flour</c> names one,
    /// so the same answer there is a dropped unit and must be rejected. Deciding that from the
    /// source rather than from the model's output is the Phase 9.1 pattern: the exemption is
    /// earned by the line, never granted by the answer.</para>
    /// </summary>
    public static bool StatesAUnitOfMeasurement(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return false;

        foreach (var word in WordSeparators.Split(sourceText))
        {
            if (word.Length == 0) continue;
            if (MeasurementUnit.TryCanonicalise(word, out _)) return true;
            if (ConvertibleUnitWords.Contains(word)) return true;
        }

        return false;
    }

    /// <summary>
    /// The count a line states for something it counts rather than measures, or null when the line
    /// is not that shape. <c>1 dash black pepper</c> is 1, <c>2 sprays of cooking spray</c> is 2,
    /// <c>4-6 handfuls corn shucks</c> is 4.
    ///
    /// <para><b>Why this is needed at all.</b> Stage 4 already resolves a count word the model put
    /// in the unit column to <c>pcs</c>, but that only runs on an answer that <i>has</i> an amount.
    /// Measured: on 44 lines across 43 recipes the model returns no amount for these, reading
    /// <c>1 dash salt</c> as the unquantified seasoning it resembles — and the gate then rejects it
    /// for a quantity the line does state. The count is recovered from the line here instead, which
    /// is the Phase 9.1 pattern: read the answer off the source, never off the model.</para>
    ///
    /// <para>Two guards keep this from inventing anything. The line must name <b>no</b> real unit of
    /// measurement, so <c>1/2 cup onion, chopped into 4 slices</c> is left alone rather than read as
    /// 4 pieces. And the count word must be the <i>next word</i> after the number, so an unrelated
    /// digit elsewhere on the line cannot supply it. A range yields its low end, since digits are
    /// not words and <c>4-6 handfuls</c> therefore reads as 4: under-buying corn shucks is the
    /// recoverable direction.</para>
    /// </summary>
    /// <param name="sourceText">The verbatim line, both spans — <c>ParsedIngredientLine.FullText</c>.</param>
    /// <param name="countWords">
    /// The closed set of words the corpus writes where a unit would go. Passed in rather than held
    /// here so the vocabulary stays owned by the one place that documents its corpus counts,
    /// <c>SeedRecipeNormaliser.CountWords</c>, instead of being restated and drifting.
    /// </param>
    public static decimal? TryReadCountedQuantity(string? sourceText, IReadOnlySet<string> countWords)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        // A line that names a real unit is measuring, not counting, and its amount belongs to that
        // unit. Decided from the source exactly as StatesAUnitOfMeasurement is used elsewhere.
        if (StatesAUnitOfMeasurement(sourceText)) return null;

        foreach (Match match in NumericToken.Matches(sourceText))
        {
            var following = FirstWordAfter(sourceText, match.Index + match.Length);
            if (following.Length > 0 && countWords.Contains(following))
                return ParseNumber(match.Value);
        }

        return null;
    }

    /// <summary>
    /// The unit a line names immediately after the single number it states, or null when the line
    /// does not settle the question. <c>1 tablespoon cinnamon</c> is <c>tablespoon</c>.
    ///
    /// <para><b>Measured before being trusted.</b> Across the normalised corpus this rule applies to
    /// 1,703 rows, and the model's own unit agrees with it on 1,702. The single disagreement is a
    /// real error it catches: the model read <c>1 tablespoon cinnamon</c> as <c>1 cup</c>, a
    /// sixteenfold overstatement that is positive, storable, and arithmetically consistent with the
    /// line's only number — so every other rule in this gate admits it.</para>
    ///
    /// <para>Both guards are load-bearing. <b>Exactly one number</b>, because a line with several
    /// names a unit inside a parenthetical equivalence rather than for its own quantity —
    /// <c>12 large egg whites (about 1 1/2 cups)</c> is 12 pieces, not cups, and 37 corpus rows are
    /// that shape. <b>Immediately after</b>, because the unit has to belong to the number: the word
    /// following the count in <c>2 medium apples</c> is a size, and searching further along the line
    /// would find a unit that measures something else.</para>
    /// </summary>
    public static string? TryReadSoleStatedUnit(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        var numbers = NumericToken.Matches(sourceText);
        if (numbers.Count != 1) return null;

        var tail  = sourceText[(numbers[0].Index + numbers[0].Length)..];
        var words = WordRun.Matches(tail).Take(LongestUnitWordCount)
            .Select(match => match.Value).ToList();

        // Longest phrase first, anchored at the word after the number: 'us fluid ounces' before
        // 'fluid ounces' before 'fluid'.
        for (var take = words.Count; take > 0; take--)
        {
            var phrase = string.Join(' ', words.Take(take));

            if (MeasurementUnit.TryCanonicalise(phrase, out _)) return phrase;
            if (MeasurementConverter.ConvertibleUnits.Contains(phrase, StringComparer.OrdinalIgnoreCase))
                return phrase;
        }

        return null;
    }

    /// <summary>One run of letters.</summary>
    private static readonly Regex WordRun = new(@"[A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Words in the longest unit spelling the pipeline accepts, so the phrase search looks exactly
    /// as far as it must. Derived from the table rather than fixed, so adding <c>us fluid ounces</c>
    /// cannot leave the search one word short.
    /// </summary>
    private static readonly int LongestUnitWordCount =
        MeasurementConverter.ConvertibleUnits
            .Concat(MeasurementUnit.AcceptedSpellings)
            .Max(unit => unit.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

    /// <summary>The next run of letters at or after <paramref name="start"/>, or empty if there is none.</summary>
    private static string FirstWordAfter(string sourceText, int start)
    {
        var index = start;
        while (index < sourceText.Length && !char.IsLetter(sourceText[index])) index++;

        var end = index;
        while (end < sourceText.Length && char.IsLetter(sourceText[end])) end++;

        return sourceText[index..end];
    }

    /// <summary>Anything that is not part of a word — the corpus punctuates units freely.</summary>
    private static readonly Regex WordSeparators = new(@"[^A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Single words drawn from the converter's customary table, so <c>1 pound pork</c> counts as
    /// naming a unit. Multi-word entries such as <c>fluid ounce</c> contribute their own words.
    /// </summary>
    private static readonly HashSet<string> ConvertibleUnitWords =
        new(MeasurementConverter.ConvertibleUnits.SelectMany(unit => unit.Split(' ')),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects an amount that contradicts a source line stating exactly one number.
    ///
    /// <para>Measured on the parsed corpus, <b>7,136 of 8,601 ingredient lines (83.0%) state
    /// exactly one number</b>, and for every one of them the model's amount is checkable by
    /// arithmetic rather than by judgement. The remaining 13.3% state several — container sizes
    /// (<c>1 can (14.5 ounces)</c>), pack counts, oven temperatures — where the right answer is a
    /// choice between them or a product of them, so the check stands aside rather than guessing.
    /// The rest state none, which <see cref="Check"/> already covers.</para>
    ///
    /// <para>This is the only part of the gate that can catch a plausible-looking wrong answer, and
    /// it earned its place: the model read <c>3/4 cup unsalted dry roasted peanuts</c> as
    /// <c>3.75 cup</c> — a positive amount in a storable unit that every other rule waves through
    /// and that would have quintupled the peanuts in every shopping list built from that recipe.
    /// </para>
    /// </summary>
    /// <param name="sourceText">The verbatim line, both spans — <c>ParsedIngredientLine.FullText</c>.</param>
    /// <param name="statedAmount">The amount the model read off the line, <b>before</b> unit conversion.</param>
    /// <returns>The rejection, or null when the line is not checkable or the amount agrees.</returns>
    public static SeedQuantityRejection? CheckAgainstStatedNumber(
        string? sourceText, decimal? statedAmount)
    {
        if (statedAmount is not { } amount) return null;
        if (TryReadSoleNumber(sourceText) is not { } sole) return null;

        // A tolerance, because a third of a cup is 0.333 to the parser and may be 0.33 or 0.667
        // to the model, and disagreeing about the fourth decimal place is not a misread. One
        // percent is far tighter than any real error: the failure this catches was 5x.
        var tolerance = Math.Max(sole * ToleranceFraction, MinimumTolerance);

        return Math.Abs(amount - sole) <= tolerance ? null : SeedQuantityRejection.ContradictedQuantity;
    }

    /// <summary>Relative agreement required between the model's amount and the line's own number.</summary>
    private const decimal ToleranceFraction = 0.01m;

    /// <summary>Floor for the tolerance, so a small fraction is not held to an impossible precision.</summary>
    private const decimal MinimumTolerance = 0.005m;

    /// <summary>
    /// Numeric tokens, longest form first so <c>1 1/2</c> reads as one number rather than two.
    /// Covers mixed numbers, common fractions, decimals, integers and vulgar fractions.
    /// </summary>
    private static readonly Regex NumericToken = new(
        @"\d+\s+\d+/\d+|\d+\s*[¼-¾⅐-⅞]|\d+/\d+|\d+(?:\.\d+)?|[¼-¾⅐-⅞]",
        RegexOptions.Compiled);

    /// <summary>
    /// The single number a line states, or null when it states none or more than one. Ambiguity is
    /// always resolved as "not checkable": a false rejection costs a real recipe, and the check is
    /// worth having only while it never fires on a correct answer.
    /// </summary>
    public static decimal? TryReadSoleNumber(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        var matches = NumericToken.Matches(sourceText);
        return matches.Count == 1 ? ParseNumber(matches[0].Value) : null;
    }

    /// <summary>Reads one matched token: <c>2 1/2</c>, <c>3/4</c>, <c>14.5</c>, <c>4</c>, <c>¼</c>, <c>1 ½</c>.</summary>
    private static decimal? ParseNumber(string token)
    {
        var trimmed = token.Trim();

        // A trailing vulgar fraction, with or without a whole part.
        if (IsVulgarFraction(trimmed[^1]))
        {
            var fraction = VulgarValue(trimmed[^1]);
            var whole    = trimmed[..^1].Trim();

            if (whole.Length == 0) return fraction;
            return decimal.TryParse(whole, out var wholeValue) ? wholeValue + fraction : null;
        }

        var parts = trimmed.Split('/');
        if (parts.Length == 2)
        {
            // "3/4", or the fractional half of a mixed number written as "1 1/2".
            var leading = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (!decimal.TryParse(leading[^1], out var numerator)) return null;
            if (!decimal.TryParse(parts[1], out var denominator) || denominator == 0m) return null;

            var value = numerator / denominator;

            if (leading.Length == 1) return value;
            return decimal.TryParse(leading[0], out var wholeValue) ? wholeValue + value : null;
        }

        return decimal.TryParse(trimmed, out var plain) ? plain : null;
    }

    /// <summary>The value of one vulgar fraction character.</summary>
    private static decimal VulgarValue(char character) => character switch
    {
        '¼' => 0.25m,  '½' => 0.5m,   '¾' => 0.75m,
        '⅐' => 1m / 7,  '⅑' => 1m / 9,  '⅒' => 0.1m,
        '⅓' => 1m / 3,  '⅔' => 2m / 3,  '⅕' => 0.2m,
        '⅖' => 0.4m,   '⅗' => 0.6m,   '⅘' => 0.8m,
        '⅙' => 1m / 6,  '⅚' => 5m / 6,  '⅛' => 0.125m,
        '⅜' => 0.375m, '⅝' => 0.625m, '⅞' => 0.875m,
        _         => 0m,
    };

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
