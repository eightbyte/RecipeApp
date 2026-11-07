using RecipeApp.API.Enums;

namespace RecipeApp.Tests.Enums;

/// <summary>The sizeless table decides which scraped lines are stored unquantified, so its spellings and its exclusions are both pinned.</summary>
public class SizelessUnitTests
{
    [Theory]
    [InlineData("pinch")]
    [InlineData("pinches")]
    [InlineData("dash")]
    [InlineData("dashes")]
    [InlineData("smidgen")]
    [InlineData("smidgens")]
    [InlineData("splash")]
    [InlineData("splashes")]
    [InlineData("drizzle")]
    [InlineData("drizzles")]
    [InlineData("sprinkle")]
    [InlineData("sprinkles")]
    [InlineData("handful")]
    [InlineData("handfuls")]
    [InlineData("to taste")]
    [InlineData("Pinch")]       // case does not matter
    [InlineData("  dash  ")]    // nor surrounding whitespace
    public void IsSizeless_MatchesUnitsWithNoRealSize(string unit)
    {
        SizelessUnit.IsSizeless(unit).Should().BeTrue();
    }

    [Theory]
    [InlineData("clove")]       // count words await their own decision
    [InlineData("cloves")]
    [InlineData("can")]
    [InlineData("sprig")]
    [InlineData("slice")]
    [InlineData("bunch")]
    [InlineData("head")]
    [InlineData("stick")]
    [InlineData("tsp")]         // storable units have a size
    [InlineData("cup")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSizeless_RejectsEverythingElse(string? unit)
    {
        SizelessUnit.IsSizeless(unit).Should().BeFalse();
    }
}
