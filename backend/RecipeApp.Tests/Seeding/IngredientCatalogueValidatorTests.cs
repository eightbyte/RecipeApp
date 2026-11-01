using FluentAssertions;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>Rules 1–4 of the catalogue validator, each failing on its own, where rule 4 is the one that corrupts silently.</summary>
public class IngredientCatalogueValidatorTests
{
    private static IngredientCatalogueEntry Entry(
        string name,
        string? displayName = null,
        string category = IngredientCategory.Produce,
        string? defaultUnit = MeasurementUnit.Piece,
        string[]? aliases = null) =>
        new(name, displayName ?? name, category, defaultUnit, aliases ?? []);

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

    [Theory]
    [InlineData("Sage")]
    [InlineData("sage")]
    [InlineData(" Sage ")]
    public void Rule1_RejectsADisplayNameSharedByTwoEntries(string secondDisplayName)
    {
        var entries = new[] { Entry("sage", "Sage"), Entry("dried sage", secondDisplayName) };

        IngredientCatalogueValidator.Validate(entries)
            .Should().ContainSingle(e => e.Contains("'dried sage'") && e.Contains("already uses"));
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
        // MeasurementUnit.IsValid is strict by design, so a hand-edited "teaspoons" would never match a stored row.
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
        // The lookup loads names before aliases, so this alias would be permanently unreachable.
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
}
