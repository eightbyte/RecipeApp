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
        arguments!.RequiresFullPipeline.Should().BeTrue();
    }

    [Theory]
    [InlineData("--discover")]
    [InlineData("--harvest")]
    [InlineData("--parse")]
    [InlineData("--normalise")]
    [InlineData("--report")]
    public void TryParse_AnImplementedStage_DoesNotNeedTheUnbuiltStages(string flag)
    {
        Parse(flag)!.RequiresFullPipeline.Should().BeFalse();
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
