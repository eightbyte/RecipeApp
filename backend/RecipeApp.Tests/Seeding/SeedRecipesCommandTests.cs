using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// The <c>seed-recipes</c> CLI's own argument vector (Phase 9 §15) and the manifest narrowing that
/// <c>--slug</c> drives. Pure functions — no cache, no network, no inference.
/// </summary>
public class SeedRecipesCommandTests
{
    private static SeedRecipesCommand.SeedRecipesArguments? Parse(params string[] args) =>
        SeedRecipesCommand.TryParse(args, NullLogger.Instance);

    // ── Argument parsing ──────────────────────────────────────────────────────

    [Fact]
    public void TryParse_NoArguments_AsksForTheFullPipeline()
    {
        var arguments = Parse();

        arguments.Should().NotBeNull();
        arguments!.RunsFullPipeline.Should().BeTrue();
    }

    [Theory]
    [InlineData("--discover")]
    [InlineData("--harvest")]
    [InlineData("--parse")]
    [InlineData("--normalise")]
    [InlineData("--build-catalogue")]
    [InlineData("--persist")]
    [InlineData("--report")]
    public void TryParse_ANamedStage_RunsOnlyThatStage(string flag)
    {
        Parse(flag)!.RunsFullPipeline.Should().BeFalse();
    }

    [Theory]
    [InlineData("--trial")]
    [InlineData("--force")]
    [InlineData("--limit", "50")]
    public void TryParse_AModifierAlone_StillRunsTheFullPipeline(params string[] args)
    {
        // §15: `seed-recipes --trial`, `--force` and `--limit 50` are full-pipeline modes.
        Parse(args)!.RunsFullPipeline.Should().BeTrue();
    }

    [Fact]
    public void TryParse_PersistWithTrial_PersistsOnlyTheTrialSubset()
    {
        var arguments = Parse("--persist", "--trial")!;

        arguments.Persist.Should().BeTrue();
        arguments.Trial.Should().BeTrue();
        arguments.RunsFullPipeline.Should().BeFalse();
    }

    [Fact]
    public void TryParse_TrialWithSlug_IsRejected()
    {
        // Both choose the recipes to run on; honouring either silently would run the wrong set.
        Parse("--trial", "--slug", "apple-crisp-0").Should().BeNull();
    }

    [Fact]
    public void SelectRecipes_Trial_NarrowsToThePinnedSlugsInManifestOrder()
    {
        var manifest = new SeedManifest
        {
            Recipes = [.. new[] { "zucchini-bread", "apple-crisp-0", "grape-salsa", "not-in-the-trial" }
                .Select(slug => new SeedManifestEntry(slug, "20250101000000", "https://www.myplate.gov/recipes/" + slug))],
        };

        var selected = SeedRecipesCommand.SelectRecipes(manifest, Parse("--trial")!, NullLogger.Instance);

        selected.Recipes.Select(entry => entry.Slug).Should().Equal("apple-crisp-0", "grape-salsa");
    }

    [Fact]
    public void TrialSelection_PinsTwentyDistinctSlugs()
    {
        // §14: twenty, stratified, reproducible. A duplicate would silently shrink the trial.
        SeedTrialSelection.Slugs.Should().HaveCount(20).And.OnlyHaveUniqueItems();
        SeedTrialSelection.Slugs.Should().OnlyContain(slug => SeedCacheStore.IsValidSlug(slug));
    }

    [Fact]
    public void TryParse_BuildCatalogue_SetsOnlyThatStage()
    {
        var arguments = Parse("--build-catalogue")!;

        arguments.BuildCatalogue.Should().BeTrue();
        arguments.Normalise.Should().BeFalse();
        arguments.Parse.Should().BeFalse();
        arguments.Harvest.Should().BeFalse();
        arguments.Discover.Should().BeFalse();
    }

    [Fact]
    public void TryParse_Normalise_SetsOnlyThatStage()
    {
        var arguments = Parse("--normalise")!;

        arguments.Normalise.Should().BeTrue();
        arguments.Parse.Should().BeFalse();
        arguments.Harvest.Should().BeFalse();
    }

    [Fact]
    public void TryParse_RepeatedSlug_CollectsEveryOneInOrder()
    {
        var arguments = Parse("--normalise", "--slug", "apple-cake", "--slug", "3-can-chili")!;

        arguments.Slugs.Should().Equal("apple-cake", "3-can-chili");
    }

    /// <summary>
    /// Slugs become filenames, so the CLI applies the same guard the cache does rather than letting
    /// an unsafe value travel any further.
    /// </summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData("Apple-Cake")]
    [InlineData("apple cake")]
    public void TryParse_AnUnsafeSlug_IsAUsageError(string slug) =>
        Parse("--normalise", "--slug", slug).Should().BeNull();

    [Fact]
    public void TryParse_SlugWithNoValue_IsAUsageError() =>
        Parse("--normalise", "--slug").Should().BeNull();

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("many")]
    public void TryParse_ANonPositiveLimit_IsAUsageError(string limit) =>
        Parse("--normalise", "--limit", limit).Should().BeNull();

    [Fact]
    public void TryParse_Limit_IsRead() => Parse("--normalise", "--limit", "50")!.Limit.Should().Be(50);

    [Fact]
    public void TryParse_AnUnrecognisedArgument_IsAUsageError() =>
        Parse("--normalize").Should().BeNull("the repo spells it --normalise");

    [Fact]
    public void TryParse_ForceAndSlugTogether_IsTheIterationLoopAndParsesCleanly()
    {
        var arguments = Parse("--normalise", "--force", "--slug", "apple-carrot-soup")!;

        arguments.Force.Should().BeTrue();
        arguments.Slugs.Should().Equal("apple-carrot-soup");
    }

    [Fact]
    public void TryParse_RepeatedBatch_CollectsEveryOneInOrder()
    {
        var arguments = Parse("--build-catalogue", "--batch", "7", "--batch", "2")!;

        arguments.CatalogueBatches.Should().Equal(7, 2);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("seven")]
    public void TryParse_ANonPositiveBatch_IsAUsageError(string batch) =>
        Parse("--build-catalogue", "--batch", batch).Should().BeNull();

    [Fact]
    public void TryParse_BatchWithoutBuildCatalogue_IsAUsageError() =>
        Parse("--normalise", "--batch", "3").Should().BeNull(
            "ignoring it would run a full pass the caller only asked to preview");

    // ── Manifest narrowing ────────────────────────────────────────────────────

    private static SeedManifest ManifestOf(params string[] slugs) => new()
    {
        HarvestedAt = DateTime.UtcNow,
        Recipes = [.. slugs.Select(slug => new SeedManifestEntry(
            slug, "20251231013807", "https://www.myplate.gov/recipes/" + slug))],
    };

    [Fact]
    public void Restrict_NoSlugs_LeavesTheManifestAlone()
    {
        var manifest = ManifestOf("one", "two", "three");

        SeedRecipesCommand.Restrict(manifest, [], NullLogger.Instance)
            .Should().BeSameAs(manifest);
    }

    [Fact]
    public void Restrict_KeepsOnlyTheNamedRecipesInManifestOrder()
    {
        var manifest = ManifestOf("one", "two", "three");

        var restricted = SeedRecipesCommand.Restrict(
            manifest, ["three", "one"], NullLogger.Instance);

        restricted.Recipes.Select(entry => entry.Slug).Should().Equal("one", "three");
    }

    /// <summary>A typo must not look like a finished run over nothing.</summary>
    [Fact]
    public void Restrict_ASlugThatIsNotInTheManifest_SelectsNothingForIt()
    {
        var restricted = SeedRecipesCommand.Restrict(
            ManifestOf("one", "two"), ["one", "not-harvested"], NullLogger.Instance);

        restricted.Recipes.Select(entry => entry.Slug).Should().Equal("one");
    }

    [Fact]
    public void Restrict_KeepsTheManifestsOwnMetadata()
    {
        var manifest = ManifestOf("one", "two") with { SourcePattern = "myplate.gov/recipes/*" };

        var restricted = SeedRecipesCommand.Restrict(manifest, ["two"], NullLogger.Instance);

        restricted.SourcePattern.Should().Be("myplate.gov/recipes/*");
        restricted.HarvestedAt.Should().Be(manifest.HarvestedAt);
    }
}
