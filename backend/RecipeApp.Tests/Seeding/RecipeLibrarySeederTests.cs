using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// The Stage 3 runner — the loop around <see cref="MyPlateRecipeParser"/> that walks the manifest,
/// isolates a bad page, and leaves the run resumable. File system only; no database, no network,
/// no inference.
///
/// <para>Stages 4 and 5 will join this class and get their own coverage; the integration tests
/// §17.3 describes are blocked on them.</para>
/// </summary>
public class RecipeLibrarySeederTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "recipeapp-seed-parse-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private RecipeSeedingOptions _options = new();

    private SeedCacheStore BuildCache() =>
        new(Options.Create(_options),
            new TestHostEnvironment(_contentRoot),
            NullLogger<SeedCacheStore>.Instance);

    private RecipeLibrarySeeder BuildSeeder(SeedCacheStore cache) =>
        new(cache,
            new MyPlateRecipeParser(Options.Create(_options)),
            NullLogger<RecipeLibrarySeeder>.Instance);

    private static SeedManifest ManifestOf(params string[] slugs) => new()
    {
        HarvestedAt   = DateTime.UtcNow,
        SourcePattern = "myplate.gov/recipes/*",
        Recipes = [.. slugs.Select(slug => new SeedManifestEntry(
            slug, "20251231013807", "https://www.myplate.gov/recipes/" + slug))],
    };

    private static string RecipePage(
        string name = "Test Recipe",
        string ingredients = "<li>1 cup rice</li>",
        string steps = "<li>Cook the rice.</li>") =>
        $$"""
        <html><head><script type="application/ld+json">
          {"@type":"Recipe","name":"{{name}}","recipeYield":"4 servings"}
        </script></head>
        <body><article class="mp-recipe-full">
          <div class="field field--name-field-mp-ingredients"><ul>{{ingredients}}</ul></div>
          <div class="field field--name-field-instructions">
            <div class="field__item"><ol>{{steps}}</ol></div>
          </div>
        </article></body></html>
        """;

    private async Task GivenCachedPageAsync(SeedCacheStore cache, string slug, string html) =>
        await cache.WriteRawAsync(slug, System.Text.Encoding.UTF8.GetBytes(html));

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WritesOneParsedRecipePerCachedPage()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage("First"));
        await GivenCachedPageAsync(cache, "second", RecipePage("Second"));

        var result = await BuildSeeder(cache).ParseAsync(ManifestOf("first", "second"));

        result.Parsed.Should().Be(2);
        result.Failed.Should().Be(0);
        cache.HasParsed("first").Should().BeTrue();

        var parsed = await cache.TryLoadParsedAsync("second");
        parsed.Should().NotBeNull();
        parsed!.Name.Should().Be("Second");
        parsed.SourceUrl.Should().Be("https://www.myplate.gov/recipes/second");
    }

    [Fact]
    public async Task ParseAsync_RecordsTheParsedStageSoAReportCanSeeProgress()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage());

        await BuildSeeder(cache).ParseAsync(ManifestOf("first"));

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["first"].Stage.Should().Be(SeedStage.Parsed);
        state.Slugs["first"].LastError.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_CountsEachTemplateSeparately()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "modern", RecipePage());
        await GivenCachedPageAsync(cache, "older",
            RecipePage().Replace("field--name-field-mp-ingredients", "field--name-field-ingredients"));

        var result = await BuildSeeder(cache).ParseAsync(ManifestOf("modern", "older"));

        result.PrimaryTemplate.Should().Be(1);
        result.LegacyTemplate.Should().Be(1);
    }

    // ── Resuming ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_SkipsRecipesAlreadyParsed()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage());
        var seeder = BuildSeeder(cache);

        await seeder.ParseAsync(ManifestOf("first"));
        var second = await seeder.ParseAsync(ManifestOf("first"));

        second.Parsed.Should().Be(0);
        second.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_ReparsesWhenForced()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage("Original"));
        var seeder = BuildSeeder(cache);
        await seeder.ParseAsync(ManifestOf("first"));

        // The page is re-harvested with a corrected name; --force re-derives from it.
        await GivenCachedPageAsync(cache, "first", RecipePage("Corrected"));
        var result = await seeder.ParseAsync(ManifestOf("first"), force: true);

        result.Parsed.Should().Be(1);
        result.Skipped.Should().Be(0);
        (await cache.TryLoadParsedAsync("first"))!.Name.Should().Be("Corrected");
    }

    [Fact]
    public async Task ParseAsync_ReportsManifestEntriesThatHaveNotBeenHarvestedYet()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "cached", RecipePage());

        var result = await BuildSeeder(cache).ParseAsync(ManifestOf("cached", "never-fetched"));

        result.Parsed.Should().Be(1);
        result.NotHarvested.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ParseAsync_HonoursTheLimitAndCountsOnlyRecipesItActuallyParsed()
    {
        var cache = BuildCache();
        foreach (var slug in new[] { "one", "two", "three" })
            await GivenCachedPageAsync(cache, slug, RecipePage());

        var result = await BuildSeeder(cache).ParseAsync(ManifestOf("one", "two", "three"), limit: 2);

        result.Parsed.Should().Be(2);
        cache.HasParsed("three").Should().BeFalse();
    }

    // ── Isolation (§10.4) ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_IsolatesAnUnparseablePageFromTheRestOfTheRun()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "good", RecipePage());
        await GivenCachedPageAsync(cache, "bad", "<html><body>Not a recipe page.</body></html>");
        await GivenCachedPageAsync(cache, "also-good", RecipePage());

        var result = await BuildSeeder(cache).ParseAsync(ManifestOf("good", "bad", "also-good"));

        result.Parsed.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Failures.Should().ContainKey("bad");
        result.Failures["bad"].Should().Be("ParseFailed: NoContentRoot");
        cache.HasParsed("also-good").Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_RecordsTheFailureReasonAgainstTheSlug()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "bad", "<html><body>Not a recipe page.</body></html>");

        await BuildSeeder(cache).ParseAsync(ManifestOf("bad"));

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["bad"].Stage.Should().Be(SeedStage.Failed);
        state.Slugs["bad"].LastError.Should().Be("ParseFailed: NoContentRoot");
        state.Slugs["bad"].HasReached(SeedStage.Parsed).Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_RecordsAManifestSlugThatIsUnsafeAsAFilenameRatherThanThrowing()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "good", RecipePage());

        var manifest = new SeedManifest
        {
            HarvestedAt = DateTime.UtcNow,
            Recipes =
            [
                new SeedManifestEntry("../escape", "20251231013807", "https://example.test"),
                new SeedManifestEntry("good", "20251231013807",
                    "https://www.myplate.gov/recipes/good"),
            ],
        };

        var result = await BuildSeeder(cache).ParseAsync(manifest);

        result.Parsed.Should().Be(1);
        result.Failures.Should().ContainKey("../escape");
    }

    // ── Cache maintenance ─────────────────────────────────────────────────────

    [Fact]
    public async Task ClearParsedContent_DropsParsedRecipesButKeepsTheCachedPages()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("first"));

        await cache.ClearParsedContentAsync();

        cache.HasParsed("first").Should().BeFalse();
        cache.HasRaw("first").Should().BeTrue();
    }

    [Fact]
    public async Task ClearParsedContent_RewindsAParsedSlugToFetchedRatherThanToDiscovered()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("first"));

        await cache.ClearParsedContentAsync();

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["first"].Stage.Should().Be(SeedStage.Fetched);
        state.Slugs["first"].LastError.Should().BeNull();
    }

    [Fact]
    public async Task ClearParsedContent_DoesNotCreditASlugWithAFetchItNeverMade()
    {
        var cache = BuildCache();
        await cache.RecordStageAsync("never-fetched", SeedStage.Failed, "HTTP 503");

        await cache.ClearParsedContentAsync();

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["never-fetched"].Stage.Should().Be(SeedStage.Discovered);
    }

    [Fact]
    public async Task TryLoadParsed_ReturnsNullForACorruptFileSoItIsSimplyReParsed()
    {
        var cache = BuildCache();
        cache.EnsureDirectories();
        await File.WriteAllTextAsync(cache.ParsedPath("first"), "{ not json");

        (await cache.TryLoadParsedAsync("first")).Should().BeNull();
    }

    [Fact]
    public async Task TryLoadParsed_ReturnsNullWhenNothingHasBeenParsed()
    {
        (await BuildCache().TryLoadParsedAsync("first")).Should().BeNull();
    }
}
