using FluentAssertions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>The merge vocabulary as a closed list, where every case is a corpus pair the grouping model actually proposed.</summary>
public class CatalogueMergeVocabularyTests
{
    [Theory]
    // Singular and plural, including the forms a naive "strip the s" misses.
    [InlineData("onion", "onions")]
    [InlineData("tomato", "tomatoes")]
    [InlineData("blueberry", "blueberries")]
    [InlineData("bay leaf", "bay leaves")]
    [InlineData("pear half", "pear halves")]
    [InlineData("peach", "peaches")]
    // Fat, sodium and sugar variants merge.
    [InlineData("chicken broth", "low-sodium chicken broth")]
    [InlineData("milk", "fat-free (skim) milk")]
    [InlineData("milk", "whole milk")]
    [InlineData("canned plums", "low-sugar canned plums")]
    [InlineData("cream style corn", "no salt added cream style corn")]
    // Cut, trim and size.
    [InlineData("chicken breast", "boneless, skinless chicken breasts")]
    [InlineData("almonds", "sliced almonds")]
    [InlineData("onion", "medium onion")]
    // Redundant qualifiers, units of sale and asides.
    [InlineData("cilantro", "fresh cilantro")]
    [InlineData("cabbage", "head of cabbage")]
    [InlineData("paprika", "paprika (optional)")]
    // Spellings, and a choice stated in either order.
    [InlineData("tilapia fillet", "tilapia filets")]
    [InlineData("green chiles", "green chilies")]
    [InlineData("pinto or kidney beans", "kidney or pinto beans")]
    public void DifferOnlyByMergeVocabulary_AllowsWhatSection5Merges(string first, string second)
    {
        CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary(first, second).Should().BeTrue();
        CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary(second, first).Should().BeTrue("the relation is symmetric");
    }

    [Theory]
    // A particular kind of a general name — each merged by the model on the benchmark batches.
    [InlineData("egg noodles", "lasagna noodles")]
    [InlineData("mustard", "dijon mustard")]
    [InlineData("cereal", "crispy rice cereal")]
    [InlineData("flour", "whole wheat flour")]
    [InlineData("lettuce", "romaine lettuce")]
    [InlineData("bacon", "turkey bacon")]
    // Preservation state and cooked/raw.
    [InlineData("green beans", "frozen green beans")]
    [InlineData("chicken", "canned chicken")]
    [InlineData("ginger", "ground ginger")]
    [InlineData("cranberries", "dried cranberries")]
    [InlineData("chicken breast", "cooked chicken breast")]
    // A choice is not either of its options.
    [InlineData("butter", "margarine or butter")]
    [InlineData("honey", "maple syrup or honey")]
    public void DifferOnlyByMergeVocabulary_RefusesWhatSection5KeepsApart(string first, string second) =>
        CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary(first, second).Should().BeFalse();

    [Fact]
    public void DifferOnlyByMergeVocabulary_TwoNamesMadeOnlyOfMergeWordsDoNotShareAnEmptyCore()
    {
        // Stripped of merge words, 'chopped' and 'sliced' would both be empty cores, and two empty cores are equal.
        CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary("chopped", "sliced").Should().BeFalse();
    }

    [Fact]
    public void DifferOnlyByMergeVocabulary_LeavesTheCannedJudgementToTheModel()
    {
        // 'diced' is a cut word so the table allows this pair, but it can only refuse and never merge, because the model must read the line.
        CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary("tomatoes", "diced tomatoes").Should().BeTrue();
    }
}
