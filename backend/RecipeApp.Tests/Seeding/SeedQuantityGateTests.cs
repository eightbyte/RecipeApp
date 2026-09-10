using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 4's quantity gate (Phase 9.1 §3.2), which replaces Phase 9 §11.3's blanket
/// <c>Any ingredient has Amount &lt;= 0</c>. Pure predicate — no database, no network, no inference.
/// </summary>
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

    /// <summary>
    /// U+00BC–U+00BE. Storing these as unquantified would be silent data loss, which this project
    /// treats as worse than a loud failure (Phase 9.1 §2).
    /// </summary>
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

    /// <summary>
    /// The leading-token restriction is what makes the number-word clause safe: a number word
    /// buried in a prep note describes the cut, not the quantity.
    /// </summary>
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

    /// <summary>
    /// The new tooth. The rule this replaces had nothing to say about a model inventing
    /// <c>1 tsp</c> for a line reading <c>salt</c> — it sailed through as a positive amount.
    /// </summary>
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

    /// <summary>
    /// A unit with no amount is the same fabrication in a different column, and it is also the
    /// half-set pair both request validators reject — so the gate must not admit one either.
    /// </summary>
    [Fact]
    public void Check_UnquantifiedSource_UnitWithoutAmount_Rejects()
    {
        var verdict = SeedQuantityGate.Check("salt", null, "tsp");

        verdict.IsAccepted.Should().BeFalse();
        verdict.Rejection.Should().Be(SeedQuantityRejection.InventedUnit);
    }

    /// <summary>
    /// The exemption is earned from the source text, never granted by the model's own output: a
    /// model cannot opt a quantified line out of the gate by returning nothing for it.
    /// </summary>
    [Fact]
    public void Check_ExemptionCannotBeSelfGranted()
    {
        SeedQuantityGate.Check("1 tablespoon olive oil", null, null)
            .IsAccepted.Should().BeFalse();

        SeedQuantityGate.Check("olive oil", null, null)
            .IsAccepted.Should().BeTrue();
    }

    // ── CheckAgainstStatedNumber ──────────────────────────────────────────────

    /// <summary>
    /// 7,136 of 8,601 corpus lines (83.0%) state exactly one number, and for those the model's
    /// amount is checkable by arithmetic rather than by judgement.
    /// </summary>
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

    /// <summary>
    /// Ambiguity is always resolved as "not checkable". A container line's right answer is a choice
    /// between its numbers or a product of them, and guessing which would reject correct output.
    /// </summary>
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
}
