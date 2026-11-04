using System.Text.RegularExpressions;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Why a Stage 4 ingredient failed the quantity gate, reported per line.</summary>
public enum SeedQuantityRejection
{
    /// <summary>The source line stated a quantity and the model returned none.</summary>
    MissingQuantity,

    /// <summary>The source line stated a quantity and the model returned zero or a negative one.</summary>
    NonPositiveQuantity,

    /// <summary>The source line stated a quantity and the model's unit is not storable.</summary>
    UnstorableUnit,

    /// <summary>The source line stated no quantity and the model supplied one anyway.</summary>
    InventedQuantity,

    /// <summary>The source line stated no quantity and the model returned none but attached a unit to it.</summary>
    InventedUnit,

    /// <summary>The source line states exactly one number and the model returned a different one, such as <c>3/4 cup</c> read as <c>3.75 cup</c>.</summary>
    ContradictedQuantity,
}

/// <summary>The outcome of one line's quantity check, carrying the canonicalised measurement on acceptance so callers do not canonicalise twice.</summary>
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

/// <summary>Stage 4's quantity gate, where the exemption for an unquantified line is earned from the source text and never granted by the model's output.</summary>
public static class SeedQuantityGate
{
    /// <summary>Number words accepted as a stated quantity, matched only as a line's leading token so <c>cut into one-inch pieces</c> does not count.</summary>
    private static readonly HashSet<string> LeadingNumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven",
        "twelve", "half", "quarter", "dozen",
    };

    /// <summary>Whether the source line states a quantity, counting vulgar fractions such as <c>¼</c> that carry no ASCII digit.</summary>
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

    /// <summary>The line with its cut-size phrases removed, so a dimension is not mistaken for a quantity.</summary>
    private static string WithoutDimensions(string sourceText) =>
        DimensionPhrase.Replace(sourceText, " ");

    /// <summary>A measurement in inches, such as <c>3 inch</c>, <c>1/2-inch</c> or <c>10x12 inches</c>.</summary>
    private static readonly Regex DimensionPhrase = new(
        @"\d+(?:\s*[xX]\s*\d+)?(?:\s+\d+/\d+|/\d+|\.\d+)?\s*-?\s*inch(?:es)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Checks one ingredient line's extracted measurement against what its source text actually stated.</summary>
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

        // Canonicalise before validating, as every path receiving a unit from outside does.
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

    /// <summary>Whether the line names any unit of measurement, storable or customary, which separates a measured line from a counted one.</summary>
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

    /// <summary>The count a line states for something it counts rather than measures, such as <c>1 dash black pepper</c>, or null when the line is not that shape.</summary>
    /// <param name="countWords">The closed vocabulary owned by <c>SeedRecipeNormaliser.CountWords</c>, passed in so it is not restated.</param>
    public static decimal? TryReadCountedQuantity(string? sourceText, IReadOnlySet<string> countWords)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        // A line that names a real unit is measuring, not counting.
        if (StatesAUnitOfMeasurement(sourceText)) return null;

        foreach (Match match in NumericToken.Matches(sourceText))
        {
            var following = FirstWordAfter(sourceText, match.Index + match.Length);
            if (following.Length > 0 && countWords.Contains(following))
                return ParseNumber(match.Value);
        }

        return null;
    }

    /// <summary>The unit a line names immediately after the single number it states, or null when the line does not settle the question.</summary>
    public static string? TryReadSoleStatedUnit(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        var numbers = NumericToken.Matches(sourceText);
        if (numbers.Count != 1) return null;

        var tail  = sourceText[(numbers[0].Index + numbers[0].Length)..];
        var words = WordRun.Matches(tail).Take(LongestUnitWordCount)
            .Select(match => match.Value).ToList();

        // Longest phrase first, anchored at the word after the number.
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

    /// <summary>Words in the longest accepted unit spelling, derived from the table so the phrase search looks exactly as far as it must.</summary>
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

    /// <summary>Single words from the converter's customary table, so <c>1 pound pork</c> counts as naming a unit.</summary>
    private static readonly HashSet<string> ConvertibleUnitWords =
        new(MeasurementConverter.ConvertibleUnits.SelectMany(unit => unit.Split(' ')),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Rejects an amount that contradicts a source line stating exactly one number.</summary>
    /// <returns>The rejection, or null when the line is not checkable or the amount agrees.</returns>
    public static SeedQuantityRejection? CheckAgainstStatedNumber(
        string? sourceText, decimal? statedAmount)
    {
        if (statedAmount is not { } amount) return null;
        if (TryReadSoleNumber(sourceText) is not { } sole) return null;

        // Allow a one percent tolerance, because the parser and the model may disagree on the fourth decimal of a fraction.
        return Agrees(amount, sole) ? null : SeedQuantityRejection.ContradictedQuantity;
    }

    /// <summary>Relative agreement required between the model's amount and the line's own number.</summary>
    private const decimal ToleranceFraction = 0.01m;

    /// <summary>Floor for the tolerance, so a small fraction is not held to an impossible precision.</summary>
    private const decimal MinimumTolerance = 0.005m;

    /// <summary>Numeric tokens, longest form first so <c>1 1/2</c> reads as one number rather than two.</summary>
    private static readonly Regex NumericToken = new(
        @"\d+\s+\d+/\d+|\d+\s*[¼-¾⅐-⅞]|\d+/\d+|\d+(?:\.\d+)?|[¼-¾⅐-⅞]",
        RegexOptions.Compiled);

    /// <summary>The single number a line states, or null when it states none or more than one, because ambiguity is resolved as "not checkable".</summary>
    public static decimal? TryReadSoleNumber(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        var matches = NumericToken.Matches(sourceText);
        return matches.Count == 1 ? ParseNumber(matches[0].Value) : null;
    }

    /// <summary>
    /// Whether an amount read off a line stating two or more numbers equals one of them or the product of two, where a percentage
    /// counts towards "two or more" but never explains an amount.
    /// </summary>
    public static bool IsAmountExplainedByLine(string? sourceText, decimal? statedAmount)
    {
        if (statedAmount is not { } amount || string.IsNullOrWhiteSpace(sourceText)) return true;

        var stated = NumericToken.Matches(sourceText)
            .Select(match => (Value: ParseNumber(match.Value), IsPercentage: IsFollowedByPercentSign(sourceText, match)))
            .Where(number => number.Value is not null)
            .ToList();

        if (stated.Count < 2) return true;

        var quantities = stated
            .Where(number => !number.IsPercentage)
            .Select(number => number.Value!.Value)
            .ToList();

        if (quantities.Any(number => Agrees(amount, number))) return true;

        for (var first = 0; first < quantities.Count; first++)
            for (var second = first + 1; second < quantities.Count; second++)
                if (Agrees(amount, quantities[first] * quantities[second])) return true;

        return false;
    }

    /// <summary>Whether a matched number is a percentage: <c>85%</c>, <c>1 %</c>.</summary>
    private static bool IsFollowedByPercentSign(string text, Match number)
    {
        var next = number.Index + number.Length;
        while (next < text.Length && text[next] == ' ') next++;
        return next < text.Length && text[next] == '%';
    }

    /// <summary>The same agreement <see cref="CheckAgainstStatedNumber"/> requires.</summary>
    private static bool Agrees(decimal amount, decimal expected) =>
        Math.Abs(amount - expected) <= Math.Max(expected * ToleranceFraction, MinimumTolerance);

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

    /// <summary>Vulgar fractions: <c>¼ ½ ¾</c> and the Number Forms block's fraction range.</summary>
    private static bool IsVulgarFraction(char character) =>
        character is >= '¼' and <= '¾' or >= '⅐' and <= '⅞';

    /// <summary>The line's first run of letters, split on anything non-alphabetic so a hyphenated <c>One-half cup</c> still leads with <c>one</c>.</summary>
    private static string LeadingWord(string sourceText)
    {
        var start = 0;
        while (start < sourceText.Length && !char.IsLetter(sourceText[start])) start++;

        var end = start;
        while (end < sourceText.Length && char.IsLetter(sourceText[end])) end++;

        return sourceText[start..end];
    }
}
