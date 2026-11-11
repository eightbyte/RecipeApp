using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>How the amounts the steps state for one ingredient are added up into a measurement for its empty row.</summary>
public class StepAmountTotallerTests
{
    private static StepAmountTotal? Total(params (double? Amount, string? Unit)[] mentions) =>
        TotalFor("salt", null, mentions);

    private static StepAmountTotal? TotalFor(
        string ingredientName, decimal? gramsPerMillilitre, params (double? Amount, string? Unit)[] mentions) =>
        StepAmountTotaller.Total(
            mentions.Select(m => new StepAmount((decimal?)m.Amount, m.Unit)).ToList(),
            ingredientName,
            gramsPerMillilitre);

    [Fact]
    public void SizedMentionsInOneUnit_AddUpInThatUnit()
    {
        Total((0.5, "tsp"), (0.5, "tsp")).Should().Be(new StepAmountTotal.InSharedUnit(1m, "tsp"));
    }

    [Fact]
    public void ASizelessMention_IsIgnoredBesideASizedOne()
    {
        Total((0.5, "tsp"), (1, "pinch"), (0.5, "tsp")).Should().Be(new StepAmountTotal.InSharedUnit(1m, "tsp"));
    }

    [Fact]
    public void ASoleSizelessMention_KeepsItsAmountAndWord()
    {
        Total((1, "pinch")).Should().Be(new StepAmountTotal.Sizeless(1m, "pinch"));
    }

    [Fact]
    public void SeveralSizelessMentions_KeepTheWordAlone()
    {
        Total((1, "pinch"), (null, "to taste")).Should().Be(new StepAmountTotal.Sizeless(null, "pinch"));
    }

    [Fact]
    public void TwoSpellingsOfOneUnit_ShareThatUnit()
    {
        Total((0.5, "teaspoon"), (0.5, "tsp")).Should().Be(new StepAmountTotal.InSharedUnit(1m, "tsp"));
    }

    [Fact]
    public void BareCounts_ShareTheMissingUnit()
    {
        TotalFor("egg", null, (1, null), (2, null)).Should().Be(new StepAmountTotal.InSharedUnit(3m, null));
    }

    [Fact]
    public void VolumesInDifferentUnits_AddUpInMillilitres()
    {
        Total((1, "tsp"), (1, "tbsp")).Should().Be(new StepAmountTotal.Converted(20m, "ml"));
    }

    [Fact]
    public void MassesInDifferentUnits_AddUpInGrams()
    {
        TotalFor("flour", null, (100, "g"), (1, "oz")).Should().Be(new StepAmountTotal.Converted(128.35m, "g"));
    }

    [Fact]
    public void CountsInDifferentWords_AddUpAsPieces()
    {
        TotalFor("garlic", null, (1, "clove"), (2, "cloves"), (1, null))
            .Should().Be(new StepAmountTotal.Converted(4m, "pcs"));
    }

    [Fact]
    public void ASizeWordInTheUnit_CountsAsABareCount()
    {
        TotalFor("egg", null, (1, "large"), (1, null)).Should().Be(new StepAmountTotal.Converted(2m, "pcs"));
    }

    [Fact]
    public void MassAndVolume_AddUpInGramsThroughTheDensity()
    {
        TotalFor("flour", 0.5m, (100, "g"), (1, "cup")).Should().Be(new StepAmountTotal.Converted(220m, "g"));
    }

    [Fact]
    public void MassAndVolumeWithNoDensity_CannotBeAddedUp()
    {
        TotalFor("milk", null, (100, "g"), (1, "cup")).Should().Be(new StepAmountTotal.NotAddedUp());
    }

    [Fact]
    public void ACountBesideAMass_CannotBeAddedUp()
    {
        TotalFor("onion", 0.5m, (1, null), (100, "g")).Should().Be(new StepAmountTotal.NotAddedUp());
    }

    [Fact]
    public void AUnitWithNoKnownSizeBesideAnother_CannotBeAddedUp()
    {
        TotalFor("chickpeas", null, (1, "can"), (100, "g")).Should().Be(new StepAmountTotal.NotAddedUp());
    }

    [Fact]
    public void AUnitWithNoKnownSizeOnEveryMention_AddsUpInThatUnit()
    {
        TotalFor("chickpeas", null, (1, "can"), (1, "can")).Should().Be(new StepAmountTotal.InSharedUnit(2m, "can"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "tsp")]
    [InlineData(0d, "tsp")]
    [InlineData(-1d, "tsp")]
    public void AMentionStatingNoAmount_StatesNothing(double? amount, string? unit)
    {
        Total((amount, unit)).Should().BeNull();
    }
}
