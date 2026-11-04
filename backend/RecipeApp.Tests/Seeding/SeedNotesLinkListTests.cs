using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>Stage 5's removal of MyPlate's related-foods link list from page notes, covering the list alone, trailing a sentence, and followed by real content that must survive.</summary>
public class SeedNotesLinkListTests
{
    private static readonly string Marker = new RecipeSeedingOptions().NotesLinkListMarker;

    [Fact]
    public void TheListAlone_LeavesNoNotes() =>
        SeedRecipePersister.StripLinkList("Learn more about: Grapes Onions Herbs Garlic", Marker)
            .Should().BeNull();

    [Fact]
    public void TheListAfterASentence_KeepsTheSentence() =>
        SeedRecipePersister.StripLinkList(
                "Try adding chopped cucumber for more crunch. Learn more about: Grapes Onions", Marker)
            .Should().Be("Try adding chopped cucumber for more crunch.");

    [Fact]
    public void TheListWithoutItsColon_IsStrippedToo() =>
        SeedRecipePersister.StripLinkList("Serve with plain or vanilla yogurt. Learn more about Cherries", Marker)
            .Should().Be("Serve with plain or vanilla yogurt.");

    [Fact]
    public void ContentOnALaterLine_IsKept() =>
        SeedRecipePersister.StripLinkList(
                "Learn more about: Tomatoes Bell Peppers\n\n* Store bought chili sauce can be high in sodium.", Marker)
            .Should().Be("* Store bought chili sauce can be high in sodium.");

    [Fact]
    public void ContentBeforeAndAfter_IsKeptAsTwoParagraphs() =>
        SeedRecipePersister.StripLinkList("Serve warm. Learn more about: Rice\n\n*Option: Serve with rice.", Marker)
            .Should().Be("Serve warm.\n\n*Option: Serve with rice.");

    [Theory]
    [InlineData("Store leftovers covered.")]
    [InlineData(null)]
    [InlineData("")]
    public void NotesWithoutTheMarker_AreUnchanged(string? notes) =>
        SeedRecipePersister.StripLinkList(notes, Marker).Should().Be(notes);

    [Fact]
    public void AnEmptyMarker_DisablesTheStrip() =>
        SeedRecipePersister.StripLinkList("Learn more about: Rice", string.Empty)
            .Should().Be("Learn more about: Rice");
}
