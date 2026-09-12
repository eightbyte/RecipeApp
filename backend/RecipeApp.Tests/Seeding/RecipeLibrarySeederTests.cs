using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// The Stage 3 and Stage 4 runners — the loops around <see cref="MyPlateRecipeParser"/> and
/// <see cref="SeedRecipeNormaliser"/> that walk the manifest, isolate a bad recipe, and leave the
/// run resumable. File system only; no database, no network, and inference is scripted through
/// <see cref="StubLlmStructuredClient"/>.
///
/// <para>Stage 5 will join this class and get its own coverage; the integration tests §17.3
/// describes are blocked on it.</para>
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

    /// <summary>A model that is never reached — Stage 3 does no inference.</summary>
    private static readonly StubLlmStructuredClient UnusedLlm =
        StubLlmStructuredClient.AlwaysFailing("Stage 3 must not call the model.");

    private RecipeLibrarySeeder BuildSeeder(
        SeedCacheStore cache, StubLlmStructuredClient? llm = null) =>
        new(cache,
            new MyPlateRecipeParser(Options.Create(_options)),
            new SeedRecipeNormaliser(
                llm ?? UnusedLlm, Options.Create(_options),
                NullLogger<SeedRecipeNormaliser>.Instance),
            Options.Create(_options),
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

    // ── Stage 4 — the normalise runner ────────────────────────────────────────

    /// <summary>The answer a well-behaved model gives for <see cref="RecipePage"/>'s defaults.</summary>
    private static JsonNode RiceAnswer(string ingredientName = "rice") => new JsonObject
    {
        ["name"]     = "Test Recipe",
        ["servings"] = 4,
        ["ingredients"] = new JsonArray(new JsonObject
        {
            ["name"]         = ingredientName,
            ["display_name"] = "Rice",
            ["amount"]       = 1,
            ["unit"]         = "cup",
            ["notes"]        = null,
        }),
        ["steps"] = new JsonArray(new JsonObject
        {
            ["step_number"]        = 1,
            ["instruction"]        = "Cook the rice.",
            ["ingredient_indexes"] = new JsonArray(0),
        }),
    };

    /// <summary>A model that always breaks the gate — it invents a quantity the page never stated.</summary>
    private static JsonNode InventedQuantityAnswer() => new JsonObject
    {
        ["name"]     = "Test Recipe",
        ["servings"] = 4,
        ["ingredients"] = new JsonArray(new JsonObject
        {
            ["name"] = "salt", ["display_name"] = "Salt", ["amount"] = 1, ["unit"] = "tsp",
        }),
        ["steps"] = new JsonArray(new JsonObject
        {
            ["step_number"] = 1, ["instruction"] = "Season.", ["ingredient_indexes"] = new JsonArray(0),
        }),
    };

    private static string SaltPage() =>
        RecipePage(ingredients: "<li>salt</li>", steps: "<li>Season.</li>");

    private async Task<SeedCacheStore> GivenParsedRecipesAsync(params string[] slugs)
    {
        var cache = BuildCache();
        foreach (var slug in slugs) await GivenCachedPageAsync(cache, slug, RecipePage());
        await BuildSeeder(cache).ParseAsync(ManifestOf(slugs));
        return cache;
    }

    [Fact]
    public async Task NormaliseAsync_WritesOneNormalisedRecipePerParsedRecipe()
    {
        var cache = await GivenParsedRecipesAsync("first", "second");
        var llm   = new StubLlmStructuredClient(RiceAnswer());

        var result = await BuildSeeder(cache, llm).NormaliseAsync(ManifestOf("first", "second"));

        result.Normalised.Should().Be(2);
        result.Failed.Should().Be(0);

        var normalised = await cache.TryLoadNormalisedAsync("second");
        normalised.Should().NotBeNull();
        normalised!.Ingredients.Should().ContainSingle().Which.Unit.Should().Be("cup");
        normalised.Steps.Should().ContainSingle().Which.Instruction.Should().Be("Cook the rice.");
    }

    [Fact]
    public async Task NormaliseAsync_RecordsTheNormalisedStageSoAReportCanSeeProgress()
    {
        var cache = await GivenParsedRecipesAsync("first");

        await BuildSeeder(cache, new StubLlmStructuredClient(RiceAnswer()))
            .NormaliseAsync(ManifestOf("first"));

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["first"].Stage.Should().Be(SeedStage.Normalised);
        state.Slugs["first"].LastError.Should().BeNull();
    }

    [Fact]
    public async Task NormaliseAsync_SkipsARecipeWhoseCachedOutputMatchesItsParsedInput()
    {
        var cache = await GivenParsedRecipesAsync("first");
        var llm   = new StubLlmStructuredClient(RiceAnswer());
        var seeder = BuildSeeder(cache, llm);

        await seeder.NormaliseAsync(ManifestOf("first"));
        var second = await seeder.NormaliseAsync(ManifestOf("first"));

        second.Normalised.Should().Be(0);
        second.Skipped.Should().Be(1);
        llm.CallCount.Should().Be(1, "a skipped recipe must not cost a GPU pass");
    }

    /// <summary>
    /// The staleness the fingerprint exists for: <c>--parse --force</c> leaves normalised output
    /// alone, so without this check a re-parse would silently keep an answer to a question the
    /// corpus no longer asks.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RedoesARecipeWhoseParsedInputChangedUnderneathIt()
    {
        var cache  = await GivenParsedRecipesAsync("first");
        var llm    = new StubLlmStructuredClient(RiceAnswer());
        var seeder = BuildSeeder(cache, llm);
        await seeder.NormaliseAsync(ManifestOf("first"));

        // The page is re-harvested and re-parsed with a corrected ingredient line. The quantity is
        // left alone: changing it would make the cached answer wrong as well as stale, and this
        // test is about staleness.
        await GivenCachedPageAsync(cache, "first",
            RecipePage(ingredients: "<li>1 cup brown rice</li>"));
        await seeder.ParseAsync(ManifestOf("first"), force: true);

        var result = await seeder.NormaliseAsync(ManifestOf("first"));

        result.Normalised.Should().Be(1);
        result.Stale.Should().Be(1);
        result.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_ReNormalisesWhenForced()
    {
        var cache  = await GivenParsedRecipesAsync("first");
        var llm    = new StubLlmStructuredClient(call => RiceAnswer(call == 1 ? "rice" : "brown rice"));
        var seeder = BuildSeeder(cache, llm);
        await seeder.NormaliseAsync(ManifestOf("first"));

        var result = await seeder.NormaliseAsync(ManifestOf("first"), force: true);

        result.Normalised.Should().Be(1);
        result.Skipped.Should().Be(0);
        (await cache.TryLoadNormalisedAsync("first"))!.Ingredients[0].Name.Should().Be("brown rice");
    }

    [Fact]
    public async Task NormaliseAsync_ReportsManifestEntriesThatHaveNotBeenParsedYet()
    {
        var cache = await GivenParsedRecipesAsync("parsed-already");

        var result = await BuildSeeder(cache, new StubLlmStructuredClient(RiceAnswer()))
            .NormaliseAsync(ManifestOf("parsed-already", "never-parsed"));

        result.Normalised.Should().Be(1);
        result.NotParsed.Should().Be(1);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_HonoursTheLimitAndCountsOnlyRecipesItActuallyNormalised()
    {
        var cache = await GivenParsedRecipesAsync("one", "two", "three");

        var result = await BuildSeeder(cache, new StubLlmStructuredClient(RiceAnswer()))
            .NormaliseAsync(ManifestOf("one", "two", "three"), limit: 2);

        result.Normalised.Should().Be(2);
        cache.HasNormalised("three").Should().BeFalse();
    }

    [Fact]
    public async Task NormaliseAsync_IsolatesARejectedRecipeFromTheRestOfTheRun()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 0 };

        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "good", RecipePage());
        await GivenCachedPageAsync(cache, "bad", SaltPage());
        await GivenCachedPageAsync(cache, "also-good", RecipePage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("good", "bad", "also-good"));

        // The middle recipe's page states no quantity, and the model invents one for it.
        var llm = new StubLlmStructuredClient(
            call => call == 2 ? InventedQuantityAnswer() : RiceAnswer());

        var result = await BuildSeeder(cache, llm)
            .NormaliseAsync(ManifestOf("good", "bad", "also-good"));

        result.Normalised.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Failures["bad"].Should()
            .Contain(nameof(SeedNormaliseFailure.QuantityRejected))
            .And.Contain(nameof(SeedQuantityRejection.InventedQuantity));
        cache.HasNormalised("also-good").Should().BeTrue();
        cache.HasNormalised("bad").Should().BeFalse("a suspect recipe is never written");
    }

    [Fact]
    public async Task NormaliseAsync_CountsARecipeThatNeededARetry()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", SaltPage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("first"));

        var corrected = new JsonObject
        {
            ["name"]     = "Test Recipe",
            ["servings"] = 4,
            ["ingredients"] = new JsonArray(new JsonObject
            {
                ["name"] = "salt", ["display_name"] = "Salt",
                ["amount"] = null, ["unit"] = null,
            }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["step_number"] = 1, ["instruction"] = "Season.",
                ["ingredient_indexes"] = new JsonArray(0),
            }),
        };

        var llm = new StubLlmStructuredClient(
            call => call == 1 ? InventedQuantityAnswer() : corrected);

        var result = await BuildSeeder(cache, llm).NormaliseAsync(ManifestOf("first"));

        result.Normalised.Should().Be(1);
        result.Retried.Should().Be(1);
    }

    /// <summary>
    /// §16's rule is that one bad recipe never aborts a run. This is the other case: no answer comes
    /// back at all, so inference itself is unavailable and every remaining recipe would fail the
    /// same way.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_StopsOnceNoAnswerHasComeBackForEveryRecipeInARow()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 0, MaxConsecutiveLlmFailures = 2 };

        var cache = await GivenParsedRecipesAsync("one", "two", "three");
        var llm   = StubLlmStructuredClient.AlwaysFailing("Local LLM model is not loaded.");

        var result = await BuildSeeder(cache, llm).NormaliseAsync(ManifestOf("one", "two", "three"));

        result.Aborted.Should().BeTrue();
        result.Failed.Should().Be(2);
        result.Failures.Should().NotContainKey("three", "the run stopped before reaching it");
    }

    /// <summary>
    /// The failure that wedged the corpus pass completely, and the reason the abort guard counts only
    /// systemic failures.
    ///
    /// <para>Cached successes are skipped before the counter is reached, so on a resumed run every
    /// recipe attempted below the high-water mark is one that already failed — and those failures
    /// reproduce. The guard fired on the first five of a thirty-recipe backlog and stopped at
    /// manifest position 77 with nothing normalised, leaving 744 recipes that had never been tried,
    /// and every re-run did the same thing. A gate rejection means the model answered, the grammar
    /// held and the gate disagreed: the pipeline working, never evidence that it is broken.</para>
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_DoesNotAbortWhenEveryRecipeIsRejectedByTheGate()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 0, MaxConsecutiveLlmFailures = 2 };

        var cache = BuildCache();
        foreach (var slug in new[] { "one", "two", "three" })
            await GivenCachedPageAsync(cache, slug, SaltPage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("one", "two", "three"));

        var llm = new StubLlmStructuredClient(InventedQuantityAnswer());

        var result = await BuildSeeder(cache, llm).NormaliseAsync(ManifestOf("one", "two", "three"));

        result.Aborted.Should().BeFalse("the gate disagreeing is not inference being unavailable");
        result.Failed.Should().Be(3);
        result.Failures.Should().ContainKey("three", "the run must reach the end of the manifest");
    }

    /// <summary>
    /// A recipe that burned its retries and lost read <c>attempts: 0</c> in <c>state.json</c>,
    /// because the runner only ever added the attempt count of a recipe that succeeded. The GPU time
    /// a failure cost is exactly what <c>--report</c> needs to show.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RecordsTheAttemptsAFailedRecipeConsumed()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 1 };

        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "bad", SaltPage());
        await BuildSeeder(cache).ParseAsync(ManifestOf("bad"));

        var llm = new StubLlmStructuredClient(InventedQuantityAnswer());
        await BuildSeeder(cache, llm).NormaliseAsync(ManifestOf("bad"));

        llm.CallCount.Should().Be(2, "one attempt and one retry");

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["bad"].Stage.Should().Be(SeedStage.Failed);
        state.Slugs["bad"].Attempts.Should().Be(3,
            "the one parse attempt plus the two LLM passes the failure actually cost");
    }

    [Fact]
    public async Task NormaliseAsync_RecordsAManifestSlugThatIsUnsafeAsAFilenameRatherThanThrowing()
    {
        var cache = await GivenParsedRecipesAsync("good");

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

        var result = await BuildSeeder(cache, new StubLlmStructuredClient(RiceAnswer()))
            .NormaliseAsync(manifest);

        result.Normalised.Should().Be(1);
        result.Failures.Should().ContainKey("../escape");
    }

    // ── Stage 4 cache maintenance ─────────────────────────────────────────────

    [Fact]
    public async Task ClearNormalisedContent_RewindsToParsedAndKeepsTheParsedRecipes()
    {
        var cache = await GivenParsedRecipesAsync("first");
        await BuildSeeder(cache, new StubLlmStructuredClient(RiceAnswer()))
            .NormaliseAsync(ManifestOf("first"));

        await cache.ClearNormalisedContentAsync();

        cache.HasNormalised("first").Should().BeFalse();
        cache.HasParsed("first").Should().BeTrue();
        cache.HasRaw("first").Should().BeTrue();

        var state = await BuildCache().LoadStateAsync();
        state.Slugs["first"].Stage.Should().Be(SeedStage.Parsed);
    }

    [Fact]
    public async Task TryLoadNormalised_ReturnsNullForACorruptFileSoItIsSimplyReNormalised()
    {
        var cache = BuildCache();
        cache.EnsureDirectories();
        await File.WriteAllTextAsync(cache.NormalisedPath("first"), "{ not json");

        (await cache.TryLoadNormalisedAsync("first")).Should().BeNull();
    }

    [Fact]
    public async Task TryComputeParsedFingerprint_ChangesWithTheParsedContentAndIsNullWithout()
    {
        var cache = BuildCache();
        await GivenCachedPageAsync(cache, "first", RecipePage("Original"));

        (await cache.TryComputeParsedFingerprintAsync("first")).Should().BeNull();

        var seeder = BuildSeeder(cache);
        await seeder.ParseAsync(ManifestOf("first"));
        var original = await cache.TryComputeParsedFingerprintAsync("first");

        await GivenCachedPageAsync(cache, "first", RecipePage("Corrected"));
        await seeder.ParseAsync(ManifestOf("first"), force: true);

        (await cache.TryComputeParsedFingerprintAsync("first")).Should().NotBe(original);
    }
}
