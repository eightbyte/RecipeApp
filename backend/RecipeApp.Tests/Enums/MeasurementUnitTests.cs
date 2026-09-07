using RecipeApp.API.Enums;

namespace RecipeApp.Tests.Enums;

/// <summary>
/// The unit table is the single authority every other unit check defers to, so it is pinned
/// exhaustively: every canonical spelling, every alias, and the strict/lenient split that lets
/// consolidation group by plain string equality.
/// </summary>
public class MeasurementUnitTests
{
    // ── The storable set ──────────────────────────────────────────────────────

    [Fact]
    public void All_ContainsExactlyTheStorableUnits()
    {
        MeasurementUnit.All.Should().Equal("g", "kg", "ml", "L", "pcs", "tsp", "tbsp", "cup");
    }

    [Fact]
    public void Table_CoversEveryUnitInAll()
    {
        MeasurementUnit.All.Should().OnlyContain(u => MeasurementUnit.Table.ContainsKey(u));
        MeasurementUnit.Table.Should().HaveCount(MeasurementUnit.All.Count);
    }

    // ── IsValid is strict ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("g")]
    [InlineData("kg")]
    [InlineData("ml")]
    [InlineData("L")]
    [InlineData("pcs")]
    [InlineData("tsp")]
    [InlineData("tbsp")]
    [InlineData("cup")]
    public void IsValid_AcceptsCanonicalSpellings(string unit)
    {
        MeasurementUnit.IsValid(unit).Should().BeTrue();
    }

    [Theory]
    [InlineData("Cup")]      // case variants are not canonical
    [InlineData("CUP")]
    [InlineData("l")]        // litre is capital L
    [InlineData("G")]
    [InlineData("Tbsp")]
    [InlineData("cups")]     // aliases are not canonical
    [InlineData("teaspoon")]
    [InlineData("clove")]    // no fixed size
    [InlineData("pinch")]
    [InlineData("oz")]       // customary, converted before storage
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_RejectsEverythingElse(string? unit)
    {
        MeasurementUnit.IsValid(unit).Should().BeFalse();
    }

    [Fact]
    public void IsValid_IgnoresSurroundingWhitespace()
    {
        MeasurementUnit.IsValid("  tbsp  ").Should().BeTrue();
    }

    // ── TryCanonicalise is lenient ────────────────────────────────────────────

    [Theory]
    [InlineData("gram", "g")]
    [InlineData("grams", "g")]
    [InlineData("kilogram", "kg")]
    [InlineData("kilograms", "kg")]
    [InlineData("kilo", "kg")]
    [InlineData("millilitre", "ml")]
    [InlineData("millilitres", "ml")]
    [InlineData("milliliter", "ml")]
    [InlineData("milliliters", "ml")]
    [InlineData("litre", "L")]
    [InlineData("litres", "L")]
    [InlineData("liter", "L")]
    [InlineData("liters", "L")]
    [InlineData("piece", "pcs")]
    [InlineData("pieces", "pcs")]
    [InlineData("pc", "pcs")]
    [InlineData("each", "pcs")]
    [InlineData("teaspoon", "tsp")]
    [InlineData("teaspoons", "tsp")]
    [InlineData("tablespoon", "tbsp")]
    [InlineData("tablespoons", "tbsp")]
    [InlineData("tbs", "tbsp")]
    [InlineData("cups", "cup")]
    public void TryCanonicalise_ResolvesEveryAlias(string input, string expected)
    {
        MeasurementUnit.TryCanonicalise(input, out var canonical).Should().BeTrue();
        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("G", "g")]
    [InlineData("ML", "ml")]
    [InlineData("l", "L")]
    [InlineData("Tbsp", "tbsp")]
    [InlineData("CUP", "cup")]
    [InlineData("  Cups  ", "cup")]
    [InlineData("TEASPOONS", "tsp")]
    public void TryCanonicalise_IsCaseInsensitiveAndTrims(string input, string expected)
    {
        MeasurementUnit.TryCanonicalise(input, out var canonical).Should().BeTrue();
        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("pinch")]
    [InlineData("can")]
    [InlineData("slice")]
    [InlineData("bunch")]
    [InlineData("handful")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryCanonicalise_RejectsUnitsWithNoFixedSize(string? input)
    {
        MeasurementUnit.TryCanonicalise(input, out var canonical).Should().BeFalse();
        canonical.Should().BeNull();
    }

    [Fact]
    public void TryCanonicalise_OutputAlwaysPassesIsValid()
    {
        string[] everySpelling =
        [
            "gram", "grams", "kilogram", "kilograms", "kilo", "millilitre", "millilitres",
            "milliliter", "milliliters", "litre", "litres", "liter", "liters", "piece", "pieces",
            "pc", "each", "teaspoon", "teaspoons", "tablespoon", "tablespoons", "tbs", "cups",
            "g", "kg", "ml", "L", "pcs", "tsp", "tbsp", "cup", "CUP", "Tbsp",
        ];

        foreach (var spelling in everySpelling)
        {
            MeasurementUnit.TryCanonicalise(spelling, out var canonical).Should().BeTrue(
                "'{0}' should canonicalise", spelling);
            MeasurementUnit.IsValid(canonical).Should().BeTrue(
                "canonicalising '{0}' must yield a storable unit", spelling);
        }
    }

    // ── Dimensions and base conversion ────────────────────────────────────────

    [Theory]
    [InlineData("g", UnitDimension.Mass)]
    [InlineData("kg", UnitDimension.Mass)]
    [InlineData("ml", UnitDimension.Volume)]
    [InlineData("L", UnitDimension.Volume)]
    [InlineData("tsp", UnitDimension.Volume)]
    [InlineData("tbsp", UnitDimension.Volume)]
    [InlineData("cup", UnitDimension.Volume)]
    [InlineData("pcs", UnitDimension.Count)]
    public void DimensionOf_ReturnsTheDimensionForEveryStorableUnit(string unit, UnitDimension expected)
    {
        MeasurementUnit.DimensionOf(unit).Should().Be(expected);
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("Cup")]
    [InlineData(null)]
    public void DimensionOf_ReturnsNullForNonCanonicalUnits(string? unit)
    {
        MeasurementUnit.DimensionOf(unit).Should().BeNull();
    }

    [Theory]
    [InlineData(1, "g", 1)]
    [InlineData(2, "kg", 2000)]
    [InlineData(250, "ml", 250)]
    [InlineData(1.5, "L", 1500)]
    [InlineData(3, "tsp", 15)]
    [InlineData(2, "tbsp", 30)]
    [InlineData(2, "cup", 480)]
    [InlineData(4, "pcs", 4)]
    public void ToBase_ConvertsToTheDimensionBaseUnit(double amount, string unit, double expected)
    {
        MeasurementUnit.ToBase((decimal)amount, unit).Should().Be((decimal)expected);
    }

    [Fact]
    public void ToBase_ReturnsNullForUnknownUnits()
    {
        MeasurementUnit.ToBase(1m, "clove").Should().BeNull();
    }

    [Fact]
    public void CupBaseFactorMatchesMillilitresPerCup()
    {
        MeasurementUnit.Table[MeasurementUnit.Cup].BaseFactor
            .Should().Be(MeasurementUnit.MillilitresPerCup);
    }

    [Fact]
    public void ImportResolvable_IsCupOnly()
    {
        // Widening this is a deliberate policy change, not an accident — see Phase 8.5.1 §5.4.
        MeasurementUnit.ImportResolvable.Should().Equal(MeasurementUnit.Cup);
    }
}
