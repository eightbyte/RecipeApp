using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>
/// Stage A (customary → metric + alias canonicalisation) and Stage B (ingredient-aware cup
/// resolution). Both are pure — no database, no HTTP, no inference.
/// </summary>
public class MeasurementConverterTests
{
    private static MeasurementConverter Build(bool resolveCupsOnImport = true) =>
        new(Options.Create(new MeasurementOptions { ResolveCupsOnImport = resolveCupsOnImport }));

    // ── Stage A: customary → metric ───────────────────────────────────────────

    [Theory]
    [InlineData("oz",           1.0,  28.350, "g")]
    [InlineData("ounce",        2.0,  56.699, "g")]
    [InlineData("ounces",       3.0,  85.049, "g")]
    [InlineData("lb",           1.0, 453.592, "g")]
    [InlineData("lbs",          2.0, 907.184, "g")]
    [InlineData("pound",        1.0, 453.592, "g")]
    [InlineData("pounds",       1.0, 453.592, "g")]
    [InlineData("fl oz",        1.0,  29.574, "ml")]
    [InlineData("fl ozs",       1.0,  29.574, "ml")]
    [InlineData("fluid oz",     1.0,  29.574, "ml")]
    [InlineData("fluid ounce",  1.0,  29.574, "ml")]
    // The plural was the one missing spelling while every other unit here had both, and it cost a
    // recipe: the model read "10 3/4 us fluid ounces" correctly and the gate rejected the unit.
    [InlineData("fluid ounces",    10.75, 317.915, "ml")]
    [InlineData("us fluid ounce",   1.0,   29.574, "ml")]
    [InlineData("us fluid ounces",  6.0,  177.441, "ml")]
    [InlineData("pt",           1.0, 473.176, "ml")]
    [InlineData("pint",         1.0, 473.176, "ml")]
    [InlineData("pints",        1.0, 473.176, "ml")]
    [InlineData("qt",           1.0, 946.353, "ml")]
    [InlineData("quart",        1.0, 946.353, "ml")]
    [InlineData("quarts",       1.0, 946.353, "ml")]
    [InlineData("gal",          1.0,   3.785, "L")]
    [InlineData("gallon",       1.0,   3.785, "L")]
    [InlineData("gallons",      2.0,   7.571, "L")]
    public void ToCanonical_ConvertsCustomaryToMetric(
        string unit, double input, double expectedApprox, string expectedUnit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(input, unit);
        outUnit.Should().Be(expectedUnit);
        ((double)amount).Should().BeApproximately(expectedApprox, 0.01);
    }

    [Theory]
    [InlineData("g")]
    [InlineData("kg")]
    [InlineData("ml")]
    [InlineData("L")]
    [InlineData("tsp")]
    [InlineData("tbsp")]
    [InlineData("pcs")]
    [InlineData("cup")]
    public void ToCanonical_CanonicalUnitsPassThroughUnchanged(string unit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(100.0, unit);
        outUnit.Should().Be(unit);
        amount.Should().Be(100m);
    }

    [Theory]
    [InlineData("cups",        2.0, 2.0,   "cup")]
    [InlineData("cup",         2.0, 2.0,   "cup")]
    [InlineData("teaspoon",    1.0, 1.0,   "tsp")]
    [InlineData("tablespoons", 3.0, 3.0,   "tbsp")]
    [InlineData("pieces",      2.0, 2.0,   "pcs")]
    [InlineData("grams",     150.0, 150.0, "g")]
    [InlineData("ML",        250.0, 250.0, "ml")]
    public void ToCanonical_CanonicalisesAliasesWithoutConverting(
        string unit, double input, double expectedAmount, string expectedUnit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(input, unit);
        outUnit.Should().Be(expectedUnit);
        amount.Should().Be((decimal)expectedAmount);
    }

    [Fact]
    public void ToCanonical_DoesNotConvertCupsToMillilitres()
    {
        // Phase 8.5.1 §5.5 — a cup is only resolved once the ingredient is known (Stage B).
        var (amount, unit) = MeasurementConverter.ToCanonical(2.0, "cups");
        amount.Should().Be(2m);
        unit.Should().Be("cup");
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("pinch")]
    [InlineData("can")]
    [InlineData("bunch")]
    public void ToCanonical_UnknownUnitsPassThroughForAHumanToCorrect(string unit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(3.0, unit);
        outUnit.Should().Be(unit);
        amount.Should().Be(3m);
        MeasurementUnit.IsValid(outUnit).Should().BeFalse();
    }

    [Fact]
    public void ToCanonical_RoundsToThreeDecimalPlaces()
    {
        var (amount, _) = MeasurementConverter.ToCanonical(1.0, "oz");
        decimal.Round(amount, 3).Should().Be(amount);
    }

    // ── Stage B: cup resolution ───────────────────────────────────────────────

    [Fact]
    public void ResolveForImport_CupWithDensity_ConvertsToGrams()
    {
        // 2 cup × 240 ml × 0.5 g/ml = 240 g
        var (amount, unit) = Build().ResolveForImport(2m, MeasurementUnit.Cup, 0.5m);
        amount.Should().Be(240m);
        unit.Should().Be(MeasurementUnit.Gram);
    }

    [Fact]
    public void ResolveForImport_CupWithoutDensity_KeepsTheCup()
    {
        // Packing-dominated or simply unknown: any gram figure would be invented (§5.3).
        var (amount, unit) = Build().ResolveForImport(2m, MeasurementUnit.Cup, null);
        amount.Should().Be(2m);
        unit.Should().Be(MeasurementUnit.Cup);
    }

    [Theory]
    [InlineData("tsp")]
    [InlineData("tbsp")]
    [InlineData("ml")]
    [InlineData("L")]
    [InlineData("g")]
    [InlineData("kg")]
    [InlineData("pcs")]
    public void ResolveForImport_LeavesEveryOtherUnitAlone(string unit)
    {
        // Only cup is import-resolvable. Spoons stay spoons even when a density exists —
        // "1 tsp salt" is better guidance to a cook than "6 g salt".
        var (amount, outUnit) = Build().ResolveForImport(1m, unit, 1.2167m);
        amount.Should().Be(1m);
        outUnit.Should().Be(unit);
    }

    [Fact]
    public void ResolveForImport_DisabledByOption_KeepsTheCupDespiteDensity()
    {
        var (amount, unit) = Build(resolveCupsOnImport: false)
            .ResolveForImport(2m, MeasurementUnit.Cup, 0.5m);
        amount.Should().Be(2m);
        unit.Should().Be(MeasurementUnit.Cup);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResolveForImport_IgnoresNonPositiveDensity(decimal density)
    {
        var (amount, unit) = Build().ResolveForImport(1m, MeasurementUnit.Cup, density);
        amount.Should().Be(1m);
        unit.Should().Be(MeasurementUnit.Cup);
    }

    [Fact]
    public void ResolveForImport_RoundTripsAPublishedGramsPerCupFigure()
    {
        // Densities are derived as gramsPerCup / MillilitresPerCup, so one cup must give the
        // published figure back exactly (§5.2).
        const decimal gramsPerCup = 113m;   // whole wheat flour
        var density = Math.Round(gramsPerCup / MeasurementUnit.MillilitresPerCup, 4);

        var (amount, unit) = Build().ResolveForImport(1m, MeasurementUnit.Cup, density);

        unit.Should().Be(MeasurementUnit.Gram);
        amount.Should().BeApproximately(gramsPerCup, 0.05m);
    }

    // ── Shared helper ─────────────────────────────────────────────────────────

    [Fact]
    public void MillilitresToGrams_MultipliesByDensity()
    {
        MeasurementConverter.MillilitresToGrams(240m, 0.5m).Should().Be(120m);
    }
}
