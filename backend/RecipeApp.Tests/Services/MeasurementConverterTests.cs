using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>Stage A (customary to metric and alias canonicalisation) and Stage B (ingredient-aware cup resolution), both pure.</summary>
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
    // The plural was the one missing spelling, and it cost a recipe when the gate rejected "us fluid ounces".
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

    [Theory]
    [InlineData("oz.",     8.0, 226.796, "g")]
    [InlineData("Oz.",     1.0,  28.35,  "g")]
    [InlineData("lb.",     1.0, 453.592, "g")]
    [InlineData("lbs.",    2.0, 907.184, "g")]
    [InlineData("fl oz.",  1.0,  29.574, "ml")]
    [InlineData("pt.",     1.0, 473.176, "ml")]
    [InlineData("qt.",     1.0, 946.353, "ml")]
    [InlineData("gal.",    1.0,   3.785, "L")]
    public void ToCanonical_ConvertsACustomaryUnitWrittenWithATrailingPeriod(
        string unit, double input, double expectedApprox, string expectedUnit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(input, unit);
        outUnit.Should().Be(expectedUnit);
        ((double)amount).Should().BeApproximately(expectedApprox, 0.01);
    }

    [Theory]
    [InlineData("tsp.",  "tsp")]
    [InlineData("Tbsp.", "tbsp")]
    [InlineData("g.",    "g")]
    public void ToCanonical_CanonicalisesAUnitWrittenWithATrailingPeriod(string unit, string expectedUnit)
    {
        var (amount, outUnit) = MeasurementConverter.ToCanonical(2.0, unit);
        outUnit.Should().Be(expectedUnit);
        amount.Should().Be(2m);
    }

    [Fact]
    public void ToCanonical_DoesNotConvertCupsToMillilitres()
    {
        // A cup is only resolved once the ingredient is known (Stage B).
        var (amount, unit) = MeasurementConverter.ToCanonical(2.0, "cups");
        amount.Should().Be(2m);
        unit.Should().Be("cup");
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("sprig")]
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
        // Packing-dominated or simply unknown, so any gram figure would be invented.
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
        // Only cup is import-resolvable, so spoons stay spoons even when a density exists.
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
        // Densities are gramsPerCup divided by MillilitresPerCup, so one cup must give the published figure back exactly.
        const decimal gramsPerCup = 113m;   // whole wheat flour
        var density = Math.Round(gramsPerCup / MeasurementUnit.MillilitresPerCup, 4);

        var (amount, unit) = Build().ResolveForImport(1m, MeasurementUnit.Cup, density);

        unit.Should().Be(MeasurementUnit.Gram);
        amount.Should().BeApproximately(gramsPerCup, 0.05m);
    }

    // ── Entry-only units ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(4,  "oz",    113.398, "g")]
    [InlineData(1,  "lb",    453.592, "g")]
    [InlineData(8,  "fl oz", 236.588, "ml")]
    [InlineData(1,  "pint",  473.176, "ml")]
    [InlineData(1,  "quart", 946.353, "ml")]
    public void ToStored_EntryOnlyUnit_ConvertsAndKeepsTheTypedMeasurementAsTheSource(
        double typedAmount, string typedUnit, double storedAmount, string storedUnit)
    {
        MeasurementConverter.ToStored((decimal)typedAmount, typedUnit, null, null).Should().Be(
            new StoredMeasurement((decimal)storedAmount, storedUnit, (decimal)typedAmount, typedUnit));
    }

    [Theory]
    [InlineData("ounces")]
    [InlineData("Ounce")]
    [InlineData("oz")]
    public void ToStored_SourceStatingTheSameQuantity_KeepsItsSpelling(string sourceUnit)
    {
        MeasurementConverter.ToStored(4m, "oz", 4m, sourceUnit).Should().Be(
            new StoredMeasurement(113.398m, "g", 4m, sourceUnit));
    }

    [Theory]
    [InlineData(4,  "pounds")]
    [InlineData(5,  "ounces")]
    [InlineData(4,  "cups")]
    public void ToStored_SourceStatingAnotherQuantity_IsReplacedByTheTypedMeasurement(
        double sourceAmount, string sourceUnit)
    {
        MeasurementConverter.ToStored(4m, "oz", (decimal)sourceAmount, sourceUnit).Should().Be(
            new StoredMeasurement(113.398m, "g", 4m, "oz"));
    }

    [Theory]
    [InlineData("oz")]
    [InlineData("ounces")]
    public void ToStored_RetypedAsTheSameQuantityInAnotherUnit_TakesTheTypedMeasurementAsTheSource(string sourceUnit)
    {
        // 16 oz and 1 lb are both 453.592 g, but the user's edit must still show.
        MeasurementConverter.ToStored(1m, "lb", 16m, sourceUnit).Should().Be(
            new StoredMeasurement(453.592m, "g", 1m, "lb"));
    }

    [Fact]
    public void ToStored_StorableUnit_CarriesTheSourceThrough()
    {
        MeasurementConverter.ToStored(120m, "g", 1m, "cup").Should().Be(
            new StoredMeasurement(120m, "g", 1m, "cup"));
    }

    [Theory]
    [InlineData(113.398, "g",  4,  "oz",              "oz")]
    [InlineData(113.398, "g",  4,  "ounces",          "oz")]
    [InlineData(453.592, "g",  1,  "pound",           "lb")]
    [InlineData(177.441, "ml", 6,  "us fluid ounces", "fl oz")]
    [InlineData(473.176, "ml", 1,  "pints",           "pint")]
    [InlineData(946.353, "ml", 1,  "qt",              "quart")]
    [InlineData(226.796, "g",  8,  "oz.",             "oz")]
    [InlineData(453.592, "g",  1,  "lb.",             "lb")]
    public void EntryUnitOf_SourceThatStillConvertsToTheStoredMeasurement_IsItsEntryOnlyUnit(
        double amount, string unit, double sourceAmount, string sourceUnit, string expected)
    {
        MeasurementConverter.EntryUnitOf((decimal)amount, unit, (decimal)sourceAmount, sourceUnit)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(150.0,   "g",   4.0,  "oz")]       // edited by hand after import
    [InlineData(113.398, "g",   null, "oz")]
    [InlineData(113.398, "g",   4.0,  null)]
    [InlineData(120.0,   "g",   1.0,  "cup")]      // a storable source
    [InlineData(3.785,   "L",   1.0,  "gallon")]   // converts, but is not offered by the picker
    [InlineData(2.0,     "pcs", 2.0,  "cloves")]
    public void EntryUnitOf_AnyOtherRow_IsNull(
        double amount, string unit, double? sourceAmount, string? sourceUnit)
    {
        MeasurementConverter.EntryUnitOf((decimal)amount, unit, (decimal?)sourceAmount, sourceUnit)
            .Should().BeNull();
    }

    [Fact]
    public void ToStored_AmountWithNoUnit_IsACountWithNoSource()
    {
        MeasurementConverter.ToStored(2m, null, null, null).Should().Be(
            new StoredMeasurement(2m, MeasurementUnit.Piece, null, null));
    }

    // ── Sized packages ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, "(6 oz) pkg",          6.0,  "oz",    "pkg")]
    [InlineData(0.5, "(16 oz) pkg",         8.0,  "oz",    "pkg")]
    [InlineData(2.0, "(15-ounce) can",     30.0,  "ounce", "can")]
    [InlineData(1.0, "( 1.5 lb ) bag",      1.5,  "lb",    "bag")]
    [InlineData(1.0, "(6oz) package",       6.0,  "oz",    "package")]
    [InlineData(1.0, "(400 g) can",       400.0,  "g",     "can")]
    [InlineData(3.0, "(12 fl oz) bottles", 36.0,  "fl oz", "bottles")]
    [InlineData(1.0, "(6 oz)",              6.0,  "oz",    null)]
    public void ReadSizedPackage_ReadsTheTotalInTheStatedSize(
        double count, string unit, double total, string sizeUnit, string? container)
    {
        MeasurementConverter.ReadSizedPackage(count, unit).Should().Be(
            new SizedPackage((decimal)total, sizeUnit, container));
    }

    [Theory]
    [InlineData("pkg")]
    [InlineData("(6) pkg")]          // no unit inside the brackets
    [InlineData("(6 cloves) pkg")]   // a count, not a size
    [InlineData("(2 pcs) pkg")]
    [InlineData("(6 oz")]
    [InlineData("6 oz pkg")]
    [InlineData("")]
    public void ReadSizedPackage_AnyOtherUnit_IsNull(string unit)
    {
        MeasurementConverter.ReadSizedPackage(1.0, unit).Should().BeNull();
    }

    // ── US Unit System ────────────────────────────────────────────────────────

    private static UsMeasurement? ToUs(
        decimal? amount, string? unit, decimal multiplier = 1m,
        decimal? sourceAmount = null, string? sourceUnit = null, decimal? gramsPerMillilitre = null) =>
        MeasurementConverter.ToUs(new StoredMeasurement(amount, unit, sourceAmount, sourceUnit), gramsPerMillilitre, multiplier);

    [Theory]
    [InlineData(113.398, "g",   4.0,   "oz")]
    [InlineData(200.0,   "g",   7.055, "oz")]
    [InlineData(453.592, "g",   1.0,   "lb")]
    [InlineData(680.388, "g",   1.5,   "lb")]
    [InlineData(1.0,     "kg",  2.205, "lb")]
    public void ToUs_Mass_ReadsOuncesBelowAPoundAndPoundsFromThere(
        double amount, string unit, double expected, string expectedUnit)
    {
        ToUs((decimal)amount, unit).Should().Be(new UsMeasurement((decimal)expected, expectedUnit));
    }

    [Theory]
    [InlineData(5.0,   "ml",   1.0,   "tsp")]
    [InlineData(7.5,   "ml",   1.5,   "tsp")]
    [InlineData(15.0,  "ml",   1.0,   "tbsp")]
    [InlineData(45.0,  "ml",   3.0,   "tbsp")]
    [InlineData(60.0,  "ml",   0.25,  "cup")]
    [InlineData(1.0,   "L",    4.167, "cup")]
    [InlineData(3.0,   "tsp",  1.0,   "tbsp")]
    [InlineData(4.0,   "tbsp", 0.25,  "cup")]
    [InlineData(0.125, "cup",  2.0,   "tbsp")]
    [InlineData(2.0,   "cup",  2.0,   "cup")]
    public void ToUs_Volume_ClimbsTheKitchenLadder(double amount, string unit, double expected, string expectedUnit)
    {
        ToUs((decimal)amount, unit).Should().Be(new UsMeasurement((decimal)expected, expectedUnit));
    }

    [Theory]
    [InlineData(59.147, "ml", 0.246, "cup")]    // 2 fl oz reads ¼ cup, not 4 tbsp
    [InlineData(14.8,   "ml", 0.987, "tbsp")]   // reads 1 tbsp, not 3 tsp
    [InlineData(452.0,  "g",  0.996, "lb")]     // reads 1 lb, not 16 oz
    public void ToUs_ChoosesTheUnitByTheAmountAsItWillRead(double amount, string unit, double expected, string expectedUnit)
    {
        ToUs((decimal)amount, unit).Should().Be(new UsMeasurement((decimal)expected, expectedUnit));
    }

    [Fact]
    public void ToUs_ScalesBeforeChoosingTheUnit()
    {
        ToUs(1m, MeasurementUnit.Teaspoon, multiplier: 3m).Should().Be(new UsMeasurement(1m, MeasurementUnit.Tablespoon));
        ToUs(340.194m, MeasurementUnit.Gram, multiplier: 2m).Should().Be(new UsMeasurement(1.5m, MeasurementUnit.Pound));
    }

    [Theory]
    [InlineData("cup",  1.0, 1.0)]
    [InlineData("cups", 1.0, 1.0)]
    [InlineData("cup",  0.5, 0.5)]
    public void ToUs_CupSourceStoredAsGramsByDensity_ReadsTheSourceVolume(string sourceUnit, double multiplier, double cups)
    {
        ToUs(120m, MeasurementUnit.Gram, (decimal)multiplier, sourceAmount: 1m, sourceUnit: sourceUnit, gramsPerMillilitre: 0.5m)
            .Should().Be(new UsMeasurement((decimal)cups, MeasurementUnit.Cup, FromSource: true));
    }

    [Theory]
    [InlineData(200.0, 0.5,  7.055)]   // edited by hand after import
    [InlineData(120.0, 0.6,  4.233)]   // density edited since import
    [InlineData(120.0, null, 4.233)]
    public void ToUs_CupSourceNoLongerMatchingTheStoredGrams_ConvertsTheStoredMass(
        double grams, double? density, double ounces)
    {
        ToUs((decimal)grams, MeasurementUnit.Gram, sourceAmount: 1m, sourceUnit: MeasurementUnit.Cup,
                gramsPerMillilitre: (decimal?)density)
            .Should().Be(new UsMeasurement((decimal)ounces, MeasurementUnit.Ounce));
    }

    [Fact]
    public void ToUs_OunceSource_ClimbsTheLadderWhenScaled()
    {
        ToUs(113.398m, MeasurementUnit.Gram, multiplier: 8m, sourceAmount: 4m, sourceUnit: "oz")
            .Should().Be(new UsMeasurement(2m, MeasurementUnit.Pound, FromSource: true));
    }

    [Theory]
    [InlineData(113.398, "g",   4.0, "ounces",    true)]
    [InlineData(473.176, "ml",  1.0, "pint",      true)]
    [InlineData(3.0,     "tsp", 3.0, "teaspoons", true)]
    [InlineData(200.0,   "g",   4.0, "oz",        false)]   // edited by hand after import
    [InlineData(500.0,   "g",   500, "grams",     false)]   // a metric source is not a US one
    public void ToUs_FromSource_OnlyForAUsSourceStillConvertingToTheStoredMeasurement(
        double amount, string unit, double sourceAmount, string sourceUnit, bool fromSource)
    {
        ToUs((decimal)amount, unit, sourceAmount: (decimal)sourceAmount, sourceUnit: sourceUnit)!.Value
            .FromSource.Should().Be(fromSource);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(2.0,  "pcs")]
    public void ToUs_UnquantifiedOrCountRow_IsNull(double? amount, string? unit)
    {
        ToUs((decimal?)amount, unit, sourceAmount: 2m, sourceUnit: "cloves").Should().BeNull();
    }

    [Fact]
    public void MillilitresToGrams_MultipliesByDensity()
    {
        MeasurementConverter.MillilitresToGrams(240m, 0.5m).Should().Be(120m);
    }
}
