using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// The on-disk seed cache (Phase 9 §5). Pure file system — no database, no network, no inference.
/// </summary>
public class SeedCacheStoreTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "recipeapp-seed-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private SeedCacheStore Build(RecipeSeedingOptions? options = null) =>
        new(Options.Create(options ?? new RecipeSeedingOptions()),
            new TestHostEnvironment(_contentRoot),
            NullLogger<SeedCacheStore>.Instance);

    // ── Layout ────────────────────────────────────────────────────────────────

    [Fact]
    public void RootPath_ResolvesRelativeCacheDirectoryAgainstContentRoot()
    {
        var store = Build(new RecipeSeedingOptions { CacheDirectory = "data/seed/myplate" });

        store.RootPath.Should().Be(Path.Combine(_contentRoot, "data/seed/myplate"));
    }

    [Fact]
    public void RootPath_UsesAnAbsoluteCacheDirectoryAsGiven()
    {
        var absolute = Path.Combine(_contentRoot, "elsewhere");
        var store    = Build(new RecipeSeedingOptions { CacheDirectory = absolute });

        store.RootPath.Should().Be(absolute);
    }

    [Fact]
    public void EnsureDirectories_CreatesEveryStageDirectory()
    {
        var store = Build();

        store.EnsureDirectories();

        Directory.Exists(store.RawDirectory).Should().BeTrue();
        Directory.Exists(store.ParsedDirectory).Should().BeTrue();
        Directory.Exists(store.NormalisedDirectory).Should().BeTrue();
        Directory.Exists(store.ImagesDirectory).Should().BeTrue();
    }

    // ── Slug safety (path traversal guard) ────────────────────────────────────

    [Theory]
    [InlineData("20-minute-chicken-creole")]
    [InlineData("3-can-chili")]
    [InlineData("apple_oatmeal_bars")]
    [InlineData("chili")]
    public void ValidateSlug_AcceptsPathautoSlugs(string slug) =>
        SeedCacheStore.ValidateSlug(slug).Should().Be(slug);

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..")]
    [InlineData("nested/slug")]
    [InlineData(@"nested\slug")]
    [InlineData("slug.html")]
    [InlineData("UPPERCASE")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateSlug_RejectsAnythingThatCouldEscapeTheCache(string? slug)
    {
        // Slugs come from a remote index and are used as filenames, so this is a security guard,
        // not a tidiness check.
        var reject = () => SeedCacheStore.ValidateSlug(slug);

        reject.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RawPath_RejectsATraversingSlug()
    {
        var store  = Build();
        var attack = () => store.RawPath("../../../evil");

        attack.Should().Throw<ArgumentException>();
    }

    // ── manifest.json ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Manifest_RoundTrips()
    {
        var store = Build();
        var manifest = new SeedManifest
        {
            HarvestedAt   = new DateTime(2026, 9, 6, 14, 0, 0, DateTimeKind.Utc),
            SourcePattern = "myplate.gov/recipes/*",
            Recipes =
            [
                new("20-minute-chicken-creole", "20251231013807",
                    "https://www.myplate.gov/recipes/20-minute-chicken-creole"),
                new("3-can-chili", "20250104120000", "https://www.myplate.gov/recipes/3-can-chili"),
            ],
        };

        await store.SaveManifestAsync(manifest);
        var reloaded = await Build().TryLoadManifestAsync();

        reloaded.Should().NotBeNull();
        reloaded!.Version.Should().Be(SeedManifest.CurrentVersion);
        reloaded.SourcePattern.Should().Be("myplate.gov/recipes/*");
        reloaded.Recipes.Should().BeEquivalentTo(manifest.Recipes);
    }

    [Fact]
    public async Task TryLoadManifest_ReturnsNullWhenTheCacheIsEmpty()
    {
        var store = Build();

        store.HasManifest().Should().BeFalse();
        (await store.TryLoadManifestAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Manifest_IsWrittenAsCamelCaseJson()
    {
        var store = Build();
        await store.SaveManifestAsync(new SeedManifest
        {
            SourcePattern = "myplate.gov/recipes/*",
            Recipes       = [new("3-can-chili", "20250104120000", "https://www.myplate.gov/recipes/3-can-chili")],
        });

        var json = await File.ReadAllTextAsync(store.ManifestPath);

        json.Should().Contain("\"sourcePattern\"").And.Contain("\"originalUrl\"");
    }

    // ── state.json ────────────────────────────────────────────────────────────

    [Fact]
    public async Task State_RoundTripsStageAttemptsAndError()
    {
        var store = Build();

        await store.RecordAttemptAsync("3-can-chili");
        await store.RecordAttemptAsync("3-can-chili");
        await store.RecordStageAsync("3-can-chili", SeedStage.Failed, "FetchFailed: HTTP 503");
        await store.RecordStageAsync("apple-oatmeal-bars", SeedStage.Fetched);

        var reloaded = await Build().LoadStateAsync();

        reloaded.Version.Should().Be(SeedState.CurrentVersion);
        reloaded.Slugs["3-can-chili"].Stage.Should().Be(SeedStage.Failed);
        reloaded.Slugs["3-can-chili"].Attempts.Should().Be(2);
        reloaded.Slugs["3-can-chili"].LastError.Should().Be("FetchFailed: HTTP 503");
        reloaded.Slugs["apple-oatmeal-bars"].Stage.Should().Be(SeedStage.Fetched);
        reloaded.Slugs["apple-oatmeal-bars"].LastError.Should().BeNull();
    }

    [Fact]
    public async Task State_SerialisesStagesInTheSpecifiedLowerCaseForm()
    {
        var store = Build();
        await store.RecordStageAsync("3-can-chili", SeedStage.Normalised);

        var json = await File.ReadAllTextAsync(store.StatePath);

        json.Should().Contain("\"normalised\"");
    }

    [Fact]
    public async Task State_ResumesFromPartialProgress()
    {
        var store = Build();
        await store.RecordStageAsync("already-fetched", SeedStage.Fetched);
        await store.RecordStageAsync("already-persisted", SeedStage.Persisted);
        await store.RecordStageAsync("gave-up", SeedStage.Failed, "ParseFailed: no ingredient block");

        var resumed = await Build().LoadStateAsync();

        resumed.Slugs["already-fetched"].HasReached(SeedStage.Fetched).Should().BeTrue();
        resumed.Slugs["already-fetched"].HasReached(SeedStage.Parsed).Should().BeFalse();
        resumed.Slugs["already-persisted"].HasReached(SeedStage.Normalised).Should().BeTrue();

        // Failed sorts last in the enum but is terminal, not "furthest along".
        resumed.Slugs["gave-up"].HasReached(SeedStage.Fetched).Should().BeFalse();
    }

    [Fact]
    public async Task State_TreatsACorruptFileAsEmptyRatherThanCrashing()
    {
        var store = Build();
        store.EnsureDirectories();
        await File.WriteAllTextAsync(store.StatePath, "{ this is not json");

        var state = await Build().LoadStateAsync();

        state.Slugs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrAdd_TracksANewSlugAtDiscovered()
    {
        var state = await Build().LoadStateAsync();

        state.GetOrAdd("new-slug").Stage.Should().Be(SeedStage.Discovered);
        state.GetOrAdd("new-slug").Attempts.Should().Be(0);
    }

    // ── Stage artefacts ───────────────────────────────────────────────────────

    [Fact]
    public async Task Raw_RoundTripsTheArchivedBytes()
    {
        var store = Build();
        const string html = "<html><body>Chicken Creole — 8 servings</body></html>";

        store.HasRaw("chicken-creole").Should().BeFalse();
        await store.WriteRawAsync("chicken-creole", Encoding.UTF8.GetBytes(html));

        store.HasRaw("chicken-creole").Should().BeTrue();
        (await store.ReadRawAsync("chicken-creole")).Should().Be(html);
    }

    [Fact]
    public async Task Raw_LeavesNoTemporaryFileBehind()
    {
        var store = Build();
        await store.WriteRawAsync("chicken-creole", Encoding.UTF8.GetBytes("<html></html>"));

        Directory.EnumerateFiles(store.RawDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Image_IsFoundWhateverExtensionItWasStoredUnder()
    {
        var store = Build();

        store.HasImage("chicken-creole").Should().BeFalse();
        await store.WriteImageAsync("chicken-creole", [0x89, 0x50], ".png");

        store.HasImage("chicken-creole").Should().BeTrue();
        store.FindImage("chicken-creole").Should().EndWith("chicken-creole.png");
    }

    // ── --refresh-cache ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClearFetchedContent_DropsPagesAndImagesAndRewindsProgress()
    {
        var store = Build();
        await store.WriteRawAsync("chicken-creole", Encoding.UTF8.GetBytes("<html></html>"));
        await store.WriteImageAsync("chicken-creole", [0xFF, 0xD8], ".jpg");
        await store.RecordStageAsync("chicken-creole", SeedStage.Persisted);
        await store.SaveManifestAsync(new SeedManifest
        {
            Recipes = [new("chicken-creole", "20250104120000", "https://www.myplate.gov/recipes/chicken-creole")],
        });

        await store.ClearFetchedContentAsync();

        store.HasRaw("chicken-creole").Should().BeFalse();
        store.HasImage("chicken-creole").Should().BeFalse();

        var state = await store.LoadStateAsync();
        state.Slugs["chicken-creole"].Stage.Should().Be(SeedStage.Discovered);
        state.Slugs["chicken-creole"].Attempts.Should().Be(0);

        // The manifest survives — re-harvesting the same pinned snapshots is the point.
        store.HasManifest().Should().BeTrue();
    }
}
