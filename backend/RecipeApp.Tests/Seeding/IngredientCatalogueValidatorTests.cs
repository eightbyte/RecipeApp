using FluentAssertions;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Phase 9.3 §4.7's six rules, each failing on its own.
///
/// <para>These are the invariants that make the catalogue trustworthy enough to be a dictionary
/// rather than a suggestion, and rule 4 is the one that corrupts silently — an alias claimed by two
/// entries, or one that shadows a name, resolves by whichever row the lookup saw first, which is a
/// function of row order rather than of intent.</para>
/// </summary>
public class IngredientCatalogueValidatorTests
{
    private static IngredientCatalogueEntry Entry(
        string name,
        string? displayName = null,
        string category = IngredientCategory.Produce,
        string? defaultUnit = MeasurementUnit.Piece,
        string[]? aliases = null,
        int corpusRows = 1) =>
        new(name, displayName ?? name, category, defaultUnit, aliases ?? [], corpusRows, 1);

    [Fact]
    public void AValidCatalogueRaisesNothing()
    {
        var entries = new[]
        {
            Entry("onion", "Onion", aliases: ["onions", "yellow onion"]),
            Entry("milk", "Milk", IngredientCategory.Dairy, MeasurementUnit.Cup, ["skim milk"]),
        };

        IngredientCatalogueValidator.Validate(entries).Should().BeEmpty();
    }

    // ── Rule 1: names ─────────────────────────────────────────────────────────

    [Fact]
    public void Rule1_RejectsADuplicateName()
    {
        var entries = new[] { Entry("onion"), Entry("onion") };

        IngredientCatalogueValidator.Validate(entries)
            .Should().ContainSingle(e => e.Contains("more than one entry"));
    }

    [Theory]
    [InlineData("Onion")]
    [InlineData(" onion")]
    [InlineData("onion ")]
    public void Rule1_RejectsANameThatIsNotNormalised(string name)
    {
        IngredientCatalogueValidator.Validate([Entry(name)])
            .Should().Contain(e => e.Contains("not stored normalised"));
    }

    [Fact]
    public void Rule1_RejectsABlankName()
    {
        IngredientCatalogueValidator.Validate([Entry("  ", "Something")])
            .Should().ContainSingle(e => e.Contains("blank name"));
    }

    [Fact]
    public void Rule1_RejectsABlankDisplayName()
    {
        IngredientCatalogueValidator.Validate([Entry("onion", "")])
            .Should().ContainSingle(e => e.Contains("no display name"));
    }

    // ── Rule 2: category ──────────────────────────────────────────────────────

    [Fact]
    public void Rule2_RejectsACategoryThatIsNotOneOfOurs()
    {
        IngredientCatalogueValidator.Validate([Entry("onion", category: "VEGETABLES")])
            .Should().ContainSingle(e => e.Contains("VEGETABLES"));
    }

    // ── Rule 3: default unit ──────────────────────────────────────────────────

    [Fact]
    public void Rule3_RejectsAUnitThatIsNotStorable()
    {
        IngredientCatalogueValidator.Validate([Entry("garlic", defaultUnit: "clove")])
            .Should().ContainSingle(e => e.Contains("clove"));
    }

    [Fact]
    public void Rule3_RejectsANonCanonicalSpellingBecauseValidatorsAreStrict()
    {
        // MeasurementUnit.IsValid is strict by design — that is what lets consolidation group by
        // plain equality — so a hand-edited "teaspoons" here would never match a stored row.
        IngredientCatalogueValidator.Validate([Entry("salt", defaultUnit: "teaspoons")])
            .Should().ContainSingle(e => e.Contains("teaspoons"));
    }

    [Fact]
    public void Rule3_AcceptsANullUnit()
    {
        IngredientCatalogueValidator.Validate([Entry("salt and pepper", defaultUnit: null)])
            .Should().BeEmpty();
    }

    // ── Rule 4: aliases ───────────────────────────────────────────────────────

    [Fact]
    public void Rule4_RejectsAnAliasClaimedByTwoEntries()
    {
        var entries = new[]
        {
            Entry("chickpeas", aliases: ["garbanzo beans"]),
            Entry("lentils",   aliases: ["garbanzo beans"]),
        };

        IngredientCatalogueValidator.Validate(entries)
            .Should().ContainSingle(e => e.Contains("claimed by both"));
    }

    [Fact]
    public void Rule4_RejectsAnAliasThatShadowsAnEntrysName()
    {
        // The lookup loads names before aliases, so this alias would be permanently unreachable —
        // and which entry a caller meant becomes a question about insertion order.
        var entries = new[]
        {
            Entry("tomato", aliases: ["diced tomatoes"]),
            Entry("diced tomatoes"),
        };

        IngredientCatalogueValidator.Validate(entries)
            .Should().ContainSingle(e => e.Contains("also an entry's name"));
    }

    [Fact]
    public void Rule4_RejectsADuplicateAliasWithinOneEntry()
    {
        IngredientCatalogueValidator.Validate([Entry("onion", aliases: ["onions", "onions"])])
            .Should().ContainSingle(e => e.Contains("twice"));
    }

    [Fact]
    public void Rule4_RejectsAnAliasThatIsNotNormalised()
    {
        IngredientCatalogueValidator.Validate([Entry("onion", aliases: ["Onions"])])
            .Should().Contain(e => e.Contains("not stored normalised"));
    }

    // ── Rule 5: corpus reachability ───────────────────────────────────────────

    [Fact]
    public void Rule5_RejectsACorpusNameReachableAsNeitherANameNorAnAlias()
    {
        var entries = new[] { Entry("onion", aliases: ["onions"], corpusRows: 2) };

        IngredientCatalogueValidator
            .ValidateAgainstCorpus(entries, ["onion", "onions", "shallot"], corpusRows: 2)
            .Should().Contain(e => e.Contains("shallot"));
    }

    [Fact]
    public void Rule5_CountsAnAliasAsReachable()
    {
        var entries = new[] { Entry("onion", aliases: ["onions"], corpusRows: 2) };

        IngredientCatalogueValidator
            .ValidateAgainstCorpus(entries, ["onion", "onions"], corpusRows: 2)
            .Should().BeEmpty();
    }

    // ── Rule 6: provenance adds up ────────────────────────────────────────────

    [Fact]
    public void Rule6_RejectsRowCountsThatDoNotSumToTheCorpus()
    {
        var entries = new[] { Entry("onion", corpusRows: 5) };

        IngredientCatalogueValidator
            .ValidateAgainstCorpus(entries, ["onion"], corpusRows: 8)
            .Should().ContainSingle(e => e.Contains("claim 5 corpus rows"));
    }
}
