using RecipeApp.API.Enums;

namespace RecipeApp.Tests.Enums;

/// <summary>The count-word table decides which scraped units are stored as <c>pcs</c>, so its spellings and its exclusions are both pinned.</summary>
public class CountWordsTests
{
    [Theory]
    [InlineData("clove")]
    [InlineData("cloves")]
    [InlineData("sprig")]
    [InlineData("sprigs")]
    [InlineData("slice")]
    [InlineData("slices")]
    [InlineData("head")]
    [InlineData("heads")]
    [InlineData("piece")]
    [InlineData("pieces")]
    [InlineData("whole")]
    [InlineData("ear")]
    [InlineData("ears")]
    [InlineData("leaf")]
    [InlineData("leaves")]
    [InlineData("stalk")]
    [InlineData("stalks")]
    [InlineData("rib")]
    [InlineData("ribs")]
    [InlineData("fillet")]
    [InlineData("fillets")]
    [InlineData("Cloves")]      // case does not matter
    [InlineData("  sprig  ")]   // nor surrounding whitespace
    public void IsCountWord_MatchesWordsThatOnlyCount(string unit)
    {
        CountWords.IsCountWord(unit).Should().BeTrue();
    }

    [Theory]
    [InlineData("can")]         // a real size that pcs would lose
    [InlineData("cans")]
    [InlineData("stick")]
    [InlineData("jar")]
    [InlineData("package")]
    [InlineData("bunch")]
    [InlineData("bag")]
    [InlineData("bottle")]
    [InlineData("box")]
    [InlineData("large")]       // a size word is not a count word
    [InlineData("pinch")]       // a Sizeless Unit
    [InlineData("pcs")]         // already storable
    [InlineData("")]
    [InlineData(null)]
    public void IsCountWord_RejectsEverythingElse(string? unit)
    {
        CountWords.IsCountWord(unit).Should().BeFalse();
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("extra-large")]
    [InlineData("Large")]
    [InlineData(" medium ")]
    public void IsSizeWord_MatchesSizeWords(string unit)
    {
        CountWords.IsSizeWord(unit).Should().BeTrue();
    }

    [Theory]
    [InlineData("clove")]
    [InlineData("can")]
    [InlineData("g")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSizeWord_RejectsEverythingElse(string? unit)
    {
        CountWords.IsSizeWord(unit).Should().BeFalse();
    }
}
