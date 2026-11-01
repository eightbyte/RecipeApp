using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>Stage 4's quantity gate, a pure predicate with no database, network or inference.</summary>
public class SeedQuantityGateTests
{
    // ── HasStatedQuantity ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("1 can (14.5 ounces) no salt added diced tomatoes")]
    [InlineData("2 medium celery stalks, chopped")]
    [InlineData("1/2 cup brown rice")]
    [InlineData("0.5 teaspoon vanilla extract")]
    public void HasStatedQuantity_AsciiDigitAnywhere_IsQuantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeTrue();

    [Theory]
    [InlineData("salt")]
    [InlineData("black pepper")]
    [InlineData("salt and pepper, to taste")]
    [InlineData("nonstick cooking spray")]
    [InlineData("raisins")]
    [InlineData("bamboo skewers")]
    [InlineData("other vegetable toppings")]
    public void HasStatedQuantity_NoDigitAndNoNumberWord_IsUnquantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeFalse();

    /// <summary>Vulgar fractions carry no ASCII digit, and storing them as unquantified would be silent data loss.</summary>
    [Theory]
    [InlineData('¼')]
    [InlineData('½')]
    [InlineData('¾')]
    public void HasStatedQuantity_Latin1VulgarFraction_IsQuantified(char fraction) =>
        SeedQuantityGate.HasStatedQuantity($"{fraction} cup sliced almonds").Should().BeTrue();

    /// <summary>Every fraction in the Number Forms block, U+2150 through U+215E.</summary>
    [Fact]
    public void HasStatedQuantity_EveryNumberFormsFraction_IsQuantified()
    {
        for (var codePoint = '⅐'; codePoint <= '⅞'; codePoint++)
            SeedQuantityGate.HasStatedQuantity($"{codePoint} cup flour")
                .Should().BeTrue($"U+{(int)codePoint:X4} is a vulgar fraction");
    }

    /// <summary>The literal corpus line that makes a naive <c>\d</c> test unsafe.</summary>
    [Fact]
    public void HasStatedQuantity_TheCorpusLineWithNoAsciiDigit_IsQuantified() =>
        SeedQuantityGate.HasStatedQuantity("¼ cup sliced almonds (optional)")
            .Should().BeTrue();

    [Theory]
    [InlineData("Two cloves garlic, minced")]
    [InlineData("three eggs")]
    [InlineData("DOZEN corn tortillas")]
    [InlineData("One-half cup milk")]
    public void HasStatedQuantity_LeadingNumberWord_IsQuantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeTrue();

    /// <summary>The leading-token restriction keeps a number word buried in a prep note from counting as a quantity.</summary>
    [Theory]
    [InlineData("green pepper, cut into one-inch pieces")]
    [InlineData("carrots, quartered")]
    [InlineData("bread, cut in half")]
    public void HasStatedQuantity_NumberWordMidLine_IsUnquantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasStatedQuantity_MissingText_IsUnquantified(string? line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeFalse();

    // ── The gate: source states a quantity ────────────────────────────────────

    [Fact]
    public void Check_QuantifiedSource_WithValidMeasurement_Accepts()
    {
        var verdict = SeedQuantityGate.Check("2 cups brown rice", 2m, "cup");

        verdict.IsAccepted.Should().BeTrue();
        verdict.Rejection.Should().BeNull();
        verdict.Amount.Should().Be(2m);
        verdict.Unit.Should().Be("cup");
    }

    /// <summary>The gate canonicalises, so the caller never has to disagree with it.</summary>
    [Theory]
    [InlineData("teaspoons", "tsp")]
    [InlineData("ML", "ml")]
    [InlineData("Cups", "cup")]
    public void Check_QuantifiedSource_LenientUnitSpelling_AcceptsCanonicalised(
        string modelUnit, string expected)
    {
        var verdict = SeedQuantityGate.Check("1 teaspoon salt", 1m, modelUnit);

        verdict.IsAccepted.Should().BeTrue();
        verdict.Unit.Should().Be(expected);
    }

    [Fact]
    public void Check_QuantifiedSource_NullAmount_RejectsAsMissing()
    {
        var verdict = SeedQuantityGate.Check("2 cups brown rice", null, null);

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.MissingQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Check_QuantifiedSource_NonPositiveAmount_Rejects(decimal amount)
    {
        var verdict = SeedQuantityGate.Check("2 cups brown rice", amount, "cup");

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.NonPositiveQuantity);
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("pinch")]
    [InlineData("can")]
    [InlineData("")]
    [InlineData(null)]
    public void Check_QuantifiedSource_UnstorableUnit_Rejects(string? unit)
    {
        var verdict = SeedQuantityGate.Check("2 cloves garlic", 2m, unit);

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.UnstorableUnit);
    }

    // ── The gate: source states no quantity ───────────────────────────────────

    [Fact]
    public void Check_UnquantifiedSource_NullMeasurement_Accepts()
    {
        var verdict = SeedQuantityGate.Check("salt and pepper, to taste", null, null);

        verdict.IsAccepted.Should().BeTrue();
        verdict.Amount.Should().BeNull();
        verdict.Unit.Should().BeNull();
    }

    /// <summary>An invented <c>1 tsp</c> for a line reading <c>salt</c> must be rejected, which the old blanket rule let through as a positive amount.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(0.25)]
    public void Check_UnquantifiedSource_InventedAmount_Rejects(decimal amount)
    {
        var verdict = SeedQuantityGate.Check("salt", amount, "tsp");

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.InventedQuantity);
    }

    /// <summary>A unit with no amount is the same fabrication in a different column and the half-set pair both validators reject, so the gate must not admit one.</summary>
    [Fact]
    public void Check_UnquantifiedSource_UnitWithoutAmount_Rejects()
    {
        var verdict = SeedQuantityGate.Check("salt", null, "tsp");

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.InventedUnit);
    }

    /// <summary>The exemption is earned from the source text, so a model cannot opt a quantified line out by returning nothing for it.</summary>
    [Fact]
    public void Check_ExemptionCannotBeSelfGranted()
    {
        SeedQuantityGate.Check("1 tablespoon olive oil", null, null)
            .IsAccepted.Should().BeFalse();

        SeedQuantityGate.Check("olive oil", null, null)
            .IsAccepted.Should().BeTrue();
    }

    // ── CheckAgainstStatedNumber ──────────────────────────────────────────────

    /// <summary>For the lines that state exactly one number, the model's amount is checkable by arithmetic rather than by judgement.</summary>
    [Theory]
    [InlineData("3/4 cup unsalted dry roasted peanuts", 0.75)]
    [InlineData("1/4 teaspoon cayenne pepper", 0.25)]
    [InlineData("1 1/2 cups seedless grapes", 1.5)]
    [InlineData("2 1/2 cups cooked chicken breast, diced", 2.5)]
    [InlineData("12 ounces lettuce mix", 12)]
    [InlineData("14.5 ounces crushed tomatoes", 14.5)]
    [InlineData("¼ cup sliced almonds (optional)", 0.25)]
    [InlineData("1 ½ cups flour", 1.5)]
    [InlineData("orange peel, dried (1 teaspoon, optional)", 1)]
    public void TryReadSoleNumber_ALineStatingOneNumber_ReadsIt(string line, double expected) =>
        SeedQuantityGate.TryReadSoleNumber(line).Should().BeApproximately((decimal)expected, 0.001m);

    /// <summary>Ambiguity is always resolved as "not checkable", because guessing which number a container line means would reject correct output.</summary>
    [Theory]
    [InlineData("1 can (14.5 ounces) no salt added diced tomatoes")]
    [InlineData("2 cans (15 ounces each) low-sodium black beans")]
    [InlineData("4 (6-inch) corn tortillas")]
    [InlineData("1/2 cup margarine, 1 stick")]
    [InlineData("aluminum foil (10x12 inches square)")]
    [InlineData("2 1/2 medium apples, sliced (14 oz. of sliced apples)")]
    public void TryReadSoleNumber_ALineStatingSeveral_IsNotCheckable(string line) =>
        SeedQuantityGate.TryReadSoleNumber(line).Should().BeNull();

    [Theory]
    [InlineData("salt")]
    [InlineData("nonstick cooking spray")]
    [InlineData("")]
    public void TryReadSoleNumber_ALineStatingNone_IsNotCheckable(string line) =>
        SeedQuantityGate.TryReadSoleNumber(line).Should().BeNull();

    /// <summary>The live misread: a fivefold error that every other rule waves through.</summary>
    [Fact]
    public void CheckAgainstStatedNumber_TheThreeQuartersMisreadAsThreePointSevenFive_IsRejected() =>
        SeedQuantityGate.CheckAgainstStatedNumber("3/4 cup unsalted dry roasted peanuts", 3.75m)
            .Should().Be(SeedQuantityRejection.ContradictedQuantity);

    [Theory]
    [InlineData("1/4 teaspoon salt", 0.25)]
    [InlineData("1/3 cup plain low-fat yogurt", 0.333)]
    [InlineData("2/3 cup rice", 0.67)]
    [InlineData("1 pound lean pork, cut into chunks", 1)]
    [InlineData("20 cups water", 20)]
    public void CheckAgainstStatedNumber_AnAmountThatAgreesWithinRounding_IsAccepted(
        string line, double amount) =>
        SeedQuantityGate.CheckAgainstStatedNumber(line, (decimal)amount).Should().BeNull();

    /// <summary>Nothing to check against, so nothing is rejected here — <see cref="SeedQuantityGate.Check"/> rules on these.</summary>
    [Theory]
    [InlineData("1 can (14.5 ounces) diced tomatoes", 14.5)]
    [InlineData("2 cans (15 ounces each) black beans", 30.0)]
    [InlineData("salt", null)]
    public void CheckAgainstStatedNumber_ALineItCannotCheck_StandsAside(string line, double? amount) =>
        SeedQuantityGate.CheckAgainstStatedNumber(line, (decimal?)amount).Should().BeNull();

    [Fact]
    public void CheckAgainstStatedNumber_NoAmountToCheck_StandsAside() =>
        SeedQuantityGate.CheckAgainstStatedNumber("1/4 teaspoon salt", null).Should().BeNull();

    // ── Cut sizes are not quantities ──────────────────────────────────────────

    /// <summary>A line whose only number is a dimension must not make the gate demand an amount the line never stated.</summary>
    [Theory]
    [InlineData("carrot, sliced into 3 inch pieces")]
    [InlineData("aluminum foil (10x12 inches square)")]
    public void HasStatedQuantity_ALineWhoseOnlyNumberIsACutSize_IsUnquantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeFalse();

    /// <summary>Stripping a dimension must not reach the real quantity on the other inch-bearing lines, which would cause silent data loss.</summary>
    [Theory]
    [InlineData("6 medium russet potatoes, peeled and sliced into 1/4 inch slices")]
    [InlineData("10 (6-inch) corn tortillas")]
    [InlineData("2 large carrots, peeled, cut into very thin 2 1/2 inch strips")]
    [InlineData("1 medium zucchini, sliced into 1/2-inch thick rounds")]
    [InlineData("4 flour tortillas (8 inch)")]
    [InlineData("1 medium red bell pepper (cut into 1 -inch pieces)")]
    public void HasStatedQuantity_ALineWithBothADimensionAndAQuantity_IsQuantified(string line) =>
        SeedQuantityGate.HasStatedQuantity(line).Should().BeTrue();

    // ── TryReadCountedQuantity ────────────────────────────────────────────────

    private static readonly HashSet<string> CountWords =
        new(StringComparer.OrdinalIgnoreCase)
        { "dash", "dashes", "pinch", "pinches", "handful", "handfuls", "spray", "sprays", "slices" };

    [Theory]
    [InlineData("1 dash black pepper", 1)]
    [InlineData("1 dash salt", 1)]
    [InlineData("2 sprays of nonstick cooking spray", 2)]
    [InlineData("3 pinches of cayenne", 3)]
    [InlineData("1/2 dash of nothing sensible", 0.5)]
    public void TryReadCountedQuantity_ANumberFollowedByAnUnsizedMeasure_IsTheCount(
        string line, double expected) =>
        SeedQuantityGate.TryReadCountedQuantity(line, CountWords).Should().Be((decimal)expected);

    /// <summary>Digits are not words, so the next word after <c>4</c> in <c>4-6</c> is the count word.</summary>
    [Fact]
    public void TryReadCountedQuantity_ARange_YieldsItsLowEnd() =>
        SeedQuantityGate.TryReadCountedQuantity("4-6 handfuls corn shucks", CountWords)
            .Should().Be(4m);

    /// <summary>A line naming a real unit is measuring, not counting, so <c>1/2 cup onion, chopped into 4 slices</c> is not read as four pieces.</summary>
    [Theory]
    [InlineData("1/2 cup onion, chopped into 4 slices")]
    [InlineData("2 cups flour")]
    [InlineData("1 pound skinless chicken breasts")]
    public void TryReadCountedQuantity_ALineNamingARealUnit_IsLeftAlone(string line) =>
        SeedQuantityGate.TryReadCountedQuantity(line, CountWords).Should().BeNull();

    [Theory]
    [InlineData("nonstick cooking spray")]
    [InlineData("salt")]
    [InlineData("7 apples")]
    [InlineData("carrot, sliced into 3 inch pieces")]
    [InlineData("")]
    public void TryReadCountedQuantity_NoNumberImmediatelyBeforeACountWord_IsNull(string line) =>
        SeedQuantityGate.TryReadCountedQuantity(line, CountWords).Should().BeNull();

    // ── TryReadSoleStatedUnit ─────────────────────────────────────────────────

    [Theory]
    [InlineData("1 tablespoon cinnamon", "tablespoon")]
    [InlineData("2 cups flour", "cups")]
    [InlineData("1/4 teaspoon salt", "teaspoon")]
    [InlineData("1 pound lean pork, cut into chunks", "pound")]
    [InlineData("¼ cup sliced almonds (optional)", "cup")]
    [InlineData("46 us fluid ounces tomato juice, low-sodium", "us fluid ounces")]
    public void TryReadSoleStatedUnit_AUnitRightAfterTheOnlyNumber_IsTheLinesUnit(
        string line, string expected) =>
        SeedQuantityGate.TryReadSoleStatedUnit(line).Should().Be(expected);

    /// <summary>The longest phrase wins, so <c>us fluid ounces</c> is not read as the <c>ounces</c> inside it, which differs by dimension.</summary>
    [Fact]
    public void TryReadSoleStatedUnit_PrefersTheLongestUnitPhrase() =>
        SeedQuantityGate.TryReadSoleStatedUnit("6 us fluid ounces orange juice")
            .Should().Be("us fluid ounces");

    /// <summary>A line stating several numbers names its unit in a parenthetical equivalence rather than for its own quantity.</summary>
    [Theory]
    [InlineData("12 large egg whites (about 1 1/2 cups)")]
    [InlineData("1 medium head of lettuce (about 10 cups)")]
    [InlineData("1 can (14.5 ounces) diced tomatoes")]
    [InlineData("2 large potatoes (about 2 pounds)")]
    [InlineData("1 clove garlic, minced (or 1/4 teaspoon garlic powder)")]
    // A percentage in the product name is a second number, so even a line whose unit sits right after the quantity stands down.
    [InlineData("12 fluid ounces 100% fruit juice concentrate")]
    public void TryReadSoleStatedUnit_ALineStatingSeveralNumbers_StandsAside(string line) =>
        SeedQuantityGate.TryReadSoleStatedUnit(line).Should().BeNull();

    /// <summary>The unit must belong to the number, so the search does not run down the line.</summary>
    [Theory]
    [InlineData("2 medium apples, pared, cored, sliced")]
    [InlineData("7 apples")]
    [InlineData("1 dash black pepper")]
    [InlineData("salt")]
    [InlineData("")]
    public void TryReadSoleStatedUnit_NoUnitImmediatelyAfterTheNumber_IsNull(string line) =>
        SeedQuantityGate.TryReadSoleStatedUnit(line).Should().BeNull();

    // ── IsAmountExplainedByLine ───────────────────────────────────────────────

    [Theory]
    [InlineData("1 pound 85% lean ground turkey", "1.85")]                         // percentage absorbed
    [InlineData("1/16 cup orange juice (1 tablespoon)", "1.062")]                  // equivalence combined
    [InlineData("3 cans (15.5 ounces each) low-sodium kidney beans", "30")]        // can count slipped
    [InlineData("1/3 cup \"lite\" vinaigrette dressing (around 15 calories per tablespoon)", "13.333")]
    [InlineData("3 tablespoons 1% low-fat milk", "1")]                             // the 1 in 1% (trial)
    [InlineData("1/4 cup buttermilk, 1% low-fat", "1")]                           // the 1 in 1% (trial)
    [InlineData("1 pound 93 % lean ground beef", "93")]                            // spaced percent sign
    public void IsAmountExplainedByLine_TheCorpusShapes_AreUnexplained(string line, string amount) =>
        SeedQuantityGate.IsAmountExplainedByLine(line, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))
            .Should().BeFalse();

    [Theory]
    [InlineData("2 cans (15.5 ounces each) low-sodium beans", "31")]     // count × pack size
    [InlineData("1 can (14.5 ounces) diced tomatoes", "14.5")]           // the pack size alone
    [InlineData("1 can (14.5 ounces) diced tomatoes", "1")]              // the count alone
    [InlineData("1/16 cup orange juice (1 tablespoon)", "0.0625")]       // the first number
    [InlineData("1/3 cup vinaigrette (15 calories per tablespoon)", "0.333")] // within 1%
    [InlineData("3 tablespoons 1% low-fat milk", "3")]                   // a percentage leaves the quantity
    [InlineData("1 pound 85% lean ground turkey", "1")]                  // explaining the amount
    public void IsAmountExplainedByLine_ANumberOnTheLineOrAProductOfTwo_IsExplained(string line, string amount) =>
        SeedQuantityGate.IsAmountExplainedByLine(line, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))
            .Should().BeTrue();

    [Theory]
    [InlineData("1 cup rice", "7")]        // one number — CheckAgainstStatedNumber's job, not this
    [InlineData("salt", "1")]              // no number — nothing to explain it by
    [InlineData("", "1")]
    public void IsAmountExplainedByLine_FewerThanTwoNumbers_IsNotJudged(string line, string amount) =>
        SeedQuantityGate.IsAmountExplainedByLine(line, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))
            .Should().BeTrue();

    [Fact]
    public void IsAmountExplainedByLine_NoAmount_IsExplained() =>
        SeedQuantityGate.IsAmountExplainedByLine("2 cans (15.5 ounces each) beans", null).Should().BeTrue();
}
