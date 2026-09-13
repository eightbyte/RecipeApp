using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 4.5 (Phase 9.3 §4.3) against a scripted model: the deterministic passes that surround the
/// one LLM call, and the invariants that stop a self-contradicting catalogue reaching disk.
/// No GPU, no database, no network.
///
/// <para>Every scripted answer is in the per-name shape <c>GroupingSchemaJson</c> constrains the
/// model to, so its labels must follow the order <see cref="IngredientCatalogueBuilder.BuildBatches"/>
/// produces: family key ordinally, then most rows first, then the shorter name.</para>
/// </summary>
public class IngredientCatalogueBuilderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "catalogue-builder-" + Guid.NewGuid().ToString("N"));

    private RecipeSeedingOptions _options;

    public IngredientCatalogueBuilderTests()
    {
        Directory.CreateDirectory(_root);
        _options = new RecipeSeedingOptions
        {
            CacheDirectory          = Path.Combine(_root, "cache"),
            CatalogueFilePath       = Path.Combine(_root, "ingredient-catalogue.json"),
            CatalogueMinimumEntries = 1,
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private SeedCacheStore BuildCache() => new(
        Options.Create(_options),
        new TestHostEnvironment(_root),
        NullLogger<SeedCacheStore>.Instance);

    private IngredientCatalogueFileStore BuildCatalogueStore() =>
        new(Options.Create(_options), new TestHostEnvironment(_root));

    private IngredientCatalogueBuilder BuildBuilder(StubLlmStructuredClient llm) => new(
        BuildCache(),
        BuildCatalogueStore(),
        llm,
        Options.Create(_options),
        NullLogger<IngredientCatalogueBuilder>.Instance);

    private static CorpusIngredientName Name(
        string name,
        int rows = 1,
        (string Unit, int Count)[]? units = null,
        string[]? displayNames = null,
        string[]? lines = null) => new()
    {
        Name              = name,
        Rows              = rows,
        RecipeSlugs       = [.. Enumerable.Range(0, rows).Select(i => $"{name.Replace(' ', '-')}-{i}")],
        UnitCounts        = (units ?? []).ToDictionary(u => u.Unit, u => u.Count),
        UnquantifiedRows  = 0,
        DisplayNameCounts = (displayNames ?? []).ToDictionary(d => d, _ => 1),
        SampleLines       = lines ?? [],
    };

    /// <summary>Writes a manifest plus a matching normalised recipe per slug.</summary>
    private async Task<SeedManifest> SeedCorpusAsync(
        params (string Slug, (string Name, decimal? Amount, string? Unit, string Line)[] Ingredients)[] recipes)
    {
        var cache = BuildCache();
        cache.EnsureDirectories();

        foreach (var (slug, ingredients) in recipes)
        {
            await cache.WriteNormalisedAsync(slug, new NormalisedSeedRecipe
            {
                Slug              = slug,
                SourceUrl         = $"https://www.myplate.gov/recipes/{slug}",
                ParsedFingerprint = "deadbeef",
                NormalisedAt      = DateTime.UtcNow,
                Attempts          = 1,
                Name              = slug,
                Servings          = 4,
                Ingredients       = [.. ingredients.Select(i => new NormalisedSeedIngredient(
                    i.Name, i.Name, i.Amount, i.Unit, i.Amount, i.Unit, null, i.Line))],
                Steps             = [new NormalisedSeedStep(1, "Cook it.", [])],
            });
        }

        return new SeedManifest
        {
            HarvestedAt = DateTime.UtcNow,
            Recipes     = [.. recipes.Select(r => new SeedManifestEntry(
                r.Slug, "20250101000000", $"https://www.myplate.gov/recipes/{r.Slug}"))],
        };
    }

    /// <summary>One grouping answer, in the per-name shape <c>GroupingSchemaJson</c> constrains the model to.</summary>
    private static JsonNode GroupingAnswer(
        params (int Label, string Name, int SameAs, string DisplayName, string Category)[] names) =>
        new JsonObject
        {
            ["names"] = new JsonArray([.. names.Select(name => (JsonNode)new JsonObject
            {
                ["label"]        = name.Label,
                ["name"]         = name.Name,
                ["same_as"]      = name.SameAs,
                ["display_name"] = name.DisplayName,
                ["category"]     = name.Category,
            })]),
        };

    // ── Pass 1: collect ───────────────────────────────────────────────────────

    [Fact]
    public async Task Pass1_AggregatesRowsRecipesUnitsAndSampleLines()
    {
        var manifest = await SeedCorpusAsync(
            ("soup",  [("onion", 1m, "pcs", "1 medium onion, chopped"),
                       ("salt",  null, null, "salt")]),
            ("stew",  [("onion", 2m, "pcs", "2 medium onions"),
                       ("onion", 1m, "cup", "1 cup onion, diced")]));

        var llm     = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", IngredientCategory.Produce),
            (1, "salt",  1, "Salt",  IngredientCategory.DryGoods)));
        var result  = await BuildBuilder(llm).BuildAsync(manifest);

        result.Succeeded.Should().BeTrue();
        result.Recipes.Should().Be(2);
        result.IngredientRows.Should().Be(4);
        result.DistinctNames.Should().Be(2);

        var written = await BuildCatalogueStore().LoadAsync();
        var onion   = written.Entries.Single(e => e.Name == "onion");

        onion.CorpusRows.Should().Be(3);

        // Unioned, not summed: 'onion' appears twice in 'stew', which is still one recipe.
        onion.CorpusRecipes.Should().Be(2);
    }

    [Fact]
    public async Task Pass1_SendsUpToThreeDistinctSourceLinesPerName()
    {
        // The lines are the evidence a merge decision rests on — 'diced tomatoes' is only
        // identifiable as canned from its line — so they must actually reach the model.
        var manifest = await SeedCorpusAsync(
            ("a", [("tomatoes", 1m, "pcs", "1 large tomato")]),
            ("b", [("tomatoes", 2m, "pcs", "2 medium tomatoes")]),
            ("c", [("tomatoes", 1m, "cup", "1 cup canned tomatoes")]),
            ("d", [("tomatoes", 3m, "pcs", "3 plum tomatoes")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "tomatoes", 0, "Tomatoes", IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var sent = llm.Calls.Single().UserContent;
        var lines = new[] { "1 large tomato", "2 medium tomatoes", "1 cup canned tomatoes", "3 plum tomatoes" }
            .Count(line => sent.Contains(line));

        lines.Should().Be(IngredientCatalogueBuilder.SampleLinesPerName);
    }

    [Fact]
    public async Task Pass1_FailsLoudlyWhenARecipeHasNotBeenNormalised()
    {
        var manifest = await SeedCorpusAsync(("soup", [("onion", 1m, "pcs", "1 onion")]));

        // A manifest entry with no normalised recipe. A catalogue built from part of the corpus
        // cannot resolve the rest of it, so this must stop rather than produce a short file.
        manifest = manifest with
        {
            Recipes = [.. manifest.Recipes, new SeedManifestEntry(
                "never-normalised", "20250101000000",
                "https://www.myplate.gov/recipes/never-normalised")],
        };

        var build = async () => await BuildBuilder(
            new StubLlmStructuredClient(GroupingAnswer())).BuildAsync(manifest);

        await build.Should().ThrowAsync<CatalogueValidationException>()
            .Where(ex => ex.Message.Contains("never-normalised"));
    }

    // ── Pass 2: batching ──────────────────────────────────────────────────────

    [Fact]
    public void Batching_KeepsAFamilyInOneCall()
    {
        // The whole reason batching is not arbitrary. A family split across two calls guarantees an
        // inconsistent answer, because neither call can see what the other decided.
        var names = new[]
        {
            Name("tomatoes", 66), Name("diced tomatoes", 32), Name("crushed tomatoes", 5),
            Name("onion", 352), Name("red onion", 20),
            Name("milk", 104), Name("skim milk", 13),
        };

        var batches    = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 3);
        var familyKeys = IngredientCatalogueBuilder.FamilyKeys(names);

        foreach (var family in names.GroupBy(name => familyKeys[name.Name]))
        {
            var batchesHoldingFamily = batches
                .Count(batch => batch.Any(name => family.Contains(name)));

            batchesHoldingFamily.Should().Be(1,
                "the '{0}' family must be judged in one call", family.Key);
        }
    }

    [Fact]
    public void Batching_KeepsASingularAndItsPluralInOneCall()
    {
        // Measured against the corpus: keyed on the raw final word, 'tomato' and 'tomatoes' were
        // different families, and greedy packing put a batch boundary between them in 8 families —
        // leaving singular ↔ plural, the merge rule with the most evidence, one the model could
        // never apply. Here 'apple' fills the first batch just enough to force that boundary.
        var names = new[]
        {
            Name("apple", 5), Name("tomato", 63), Name("tomatoes", 66), Name("diced tomatoes", 32),
        };

        var batches = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 3);

        batches.Should().ContainSingle(batch => batch.Any(name => name.Name == "tomato"))
            .Which.Select(name => name.Name).Should().Contain(["tomato", "tomatoes", "diced tomatoes"]);
    }

    [Fact]
    public void FamilyKeys_FoldAPluralOnlyOntoASingularTheCorpusUses()
    {
        // Routing by a stemmer would put 'molasses' in a 'molasse' family. Here a candidate singular
        // counts only when some corpus name already ends in it.
        var names = new[]
        {
            Name("chilies"), Name("green chili"),
            Name("blueberries"), Name("blueberry"),
            Name("tomatoes"), Name("tomato"),
            Name("molasses"), Name("hummus"), Name("green beans"),
        };

        var keys = IngredientCatalogueBuilder.FamilyKeys(names);

        keys["chilies"].Should().Be("chili");
        keys["blueberries"].Should().Be("blueberry");
        keys["tomatoes"].Should().Be("tomato");

        keys["molasses"].Should().Be("molasses");
        keys["hummus"].Should().Be("hummus");
        keys["green beans"].Should().Be("beans", "no corpus name here ends in 'bean'");
    }

    [Fact]
    public void Batching_SplitsAnOversizedFamilyByFrequencyMostFrequentFirst()
    {
        var names = new[]
        {
            Name("tomatoes", 66), Name("diced tomatoes", 32), Name("crushed tomatoes", 5),
            Name("canned tomatoes", 2), Name("stewed tomatoes", 1),
        };

        var batches = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 2);

        batches.Should().HaveCount(3);
        batches[0].Select(n => n.Name).Should().Equal("tomatoes", "diced tomatoes");
        batches[1].Select(n => n.Name).Should().Equal("crushed tomatoes", "canned tomatoes");
        batches[2].Select(n => n.Name).Should().Equal("stewed tomatoes");
    }

    [Fact]
    public void Batching_BreaksARowTieTowardsTheShorterName()
    {
        // The name an entry is called by is its family's earliest label, which is where the prompt
        // tells same_as to point. On a tie the unqualified form should be that name.
        var names = new[] { Name("skim milk", 3), Name("milk", 3), Name("fat-free milk", 3) };

        var batch = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 10).Single();

        batch.Select(n => n.Name).Should().Equal("milk", "skim milk", "fat-free milk");
    }

    [Fact]
    public void Batching_NeverExceedsTheCap()
    {
        var names = Enumerable.Range(0, 97)
            .Select(i => Name($"thing{i} noun{i % 7}", rows: i + 1))
            .ToList();

        var batches = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 10);

        batches.Should().OnlyContain(batch => batch.Count <= 10);
        batches.SelectMany(b => b).Should().HaveCount(97);
        batches.SelectMany(b => b.Select(n => n.Name)).Should().OnlyHaveUniqueItems();
    }

    // ── Pass 2: the prompt ────────────────────────────────────────────────────

    [Fact]
    public void Prompt_ProposesNoGrouping()
    {
        // Phase 9 §23.3's measured lesson, guarded. Handed a menu of candidate answers, this model
        // family stops reading the evidence and starts choosing from the list — the fraction table
        // made its error class nearly three times worse. Mechanical folding routes names into
        // batches and is then thrown away; it must never reach the model as a suggestion.
        var batch = new[]
        {
            Name("tomatoes", 66, lines: ["2 medium tomatoes"]),
            Name("diced tomatoes", 32, lines: ["1 can (14.5 ounces) diced tomatoes"]),
        };

        var content = IngredientCatalogueBuilder.BuildUserContent(batch);

        content.Should().NotContainAny(
            "group:", "family", "suggested", "proposed", "probably", "likely", "same_as", "->", "→");

        // Each name stands on its own line with its own label; nothing pairs them up.
        content.Should().Contain("[0] tomatoes");
        content.Should().Contain("[1] diced tomatoes");
    }

    [Fact]
    public void Prompt_StatesTheRangeOfLabelsTheBatchHas()
    {
        var batch = new[] { Name("onion"), Name("paprika"), Name("salt") };

        IngredientCatalogueBuilder.BuildUserContent(batch).Should().Contain("[0] to [2]");
    }

    [Fact]
    public void Prompt_StatesTheMergeRulesAndTheirCategories()
    {
        var prompt = IngredientCatalogueBuilder.SystemPrompt;

        foreach (var category in IngredientCategory.All)
            prompt.Should().Contain(category, "the model must be told category '{0}' exists", category);

        // The two halves of §5, and the tie-break that resolves the arguable cases.
        prompt.Should().Contain("The SAME purchase");
        prompt.Should().Contain("A DIFFERENT purchase");
        prompt.Should().Contain("When a rule is arguable, use its own label.",
            "an arguable case must fall to the safe side");
    }

    [Fact]
    public void Prompt_TellsTheModelTheSharedWordIsAnArtefactOfBatching()
    {
        // Batching by family makes every batch share a word, and a rules-only prompt read that
        // resemblance as evidence: one batch of the group-shaped prompt put all 39 of its names in a
        // single group. Stating it outright took one batch from 1 group to 19.
        var prompt = IngredientCatalogueBuilder.SystemPrompt;

        prompt.Should().Contain("NOT evidence");
        prompt.Should().Contain("DIFFERENT purchases");
    }

    [Fact]
    public void Prompt_FilesSpicesWhereTheKeywordTableAlreadyDoes()
    {
        // Measured: with no rule for them, the model invented SPICES. The rule stated is the
        // codebase's existing convention, not a new one — paprika and cumin are already CONDIMENTS
        // in the keyword table a user-scraped name falls back to.
        IngredientCatalogueBuilder.SystemPrompt.Should().Contain(
            $"Spices, dried herbs, seasonings and extracts are {IngredientCategory.Condiments}.");

        RecipeApp.API.Services.RecipeScrapeService.CategoriseIngredient("paprika")
            .Should().Be(IngredientCategory.Condiments);
    }

    [Fact]
    public void Schema_ConstrainsCategoryToExactlyOurCategories()
    {
        // As a free string the model answered SPICES and CONDIMENT. The enum is generated from the
        // one category table, so a category added there reaches the grammar with no second edit.
        var schema   = JsonNode.Parse(IngredientCatalogueBuilder.GroupingSchemaJson)!;
        var category = schema["properties"]!["names"]!["items"]!["properties"]!["category"]!;

        category["enum"]!.AsArray().Select(value => value!.GetValue<string>())
            .Should().Equal(IngredientCategory.All);
    }

    [Fact]
    public void WorkedExample_IsRenderedExactlyAsTheBuilderWouldBatchIt()
    {
        // The example's labels teach where same_as points. If they were not in the order the
        // builder really produces, the example would teach an ordering no real batch has.
        var names = IngredientCatalogueBuilder.WorkedExample
            .Select(example => Name(example.Name, example.Rows, lines: [example.SourceLine]))
            .ToList();

        var batch = IngredientCatalogueBuilder.BuildBatches(names, batchSize: 40).Single();

        batch.Select(name => name.Name).Should().Equal(
            IngredientCatalogueBuilder.WorkedExample.Select(example => example.Name));

        // And each name line is rendered in the prompt exactly as a real batch renders it.
        var rendered  = IngredientCatalogueBuilder.BuildUserContent(batch);
        var firstLine = $"[0] {IngredientCatalogueBuilder.WorkedExample[0].Name} ";
        var listing   = rendered[rendered.IndexOf(firstLine, StringComparison.Ordinal)..];

        IngredientCatalogueBuilder.SystemPrompt.Should().Contain(listing);
    }

    [Fact]
    public async Task WorkedExample_AnswerGoesThroughTheRealReaderAndSplitsMoreThanItMerges()
    {
        // Stage 4's lesson about examples teaching the wrong rule: with a bare 'salt' as its null
        // case the model learned *salt* was the exemption rather than *no number*. An example that
        // merged a whole family here would teach exactly the failure it exists to prevent. The answer
        // is lifted out of the prompt text itself, so what the model is shown is what is tested.
        var prompt = IngredientCatalogueBuilder.SystemPrompt;
        var start  = prompt.IndexOf("{\"names\":[", StringComparison.Ordinal);
        var end    = prompt.IndexOf("]}", start, StringComparison.Ordinal) + "]}".Length;
        var answer = JsonNode.Parse(prompt[start..end])!;

        var manifest = await SeedCorpusAsync(("plum-corpus",
            [.. IngredientCatalogueBuilder.WorkedExample.SelectMany(example =>
                Enumerable.Repeat(((string, decimal?, string?, string))
                    (example.Name, 1m, MeasurementUnit.Piece, example.SourceLine), example.Rows))]));

        var llm    = new StubLlmStructuredClient(answer);
        var result = await BuildBuilder(llm).BuildAsync(manifest);

        result.Succeeded.Should().BeTrue();
        llm.CallCount.Should().Be(1, "the example answers every one of its names");

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Select(e => e.Name).Should().BeEquivalentTo(
            ["plums", "canned plums", "dried plums", "red plums", "apricots or plums", "plum sauce"]);
        entries.Single(e => e.Name == "plums").Aliases.Should().BeEquivalentTo(["fresh plums", "plum"]);
        entries.Single(e => e.Name == "canned plums").Aliases.Should().Equal("low-sugar canned plums");

        // The split must cross aisle boundaries, or it only teaches that names differ.
        entries.Select(e => e.Category).Distinct().Should().HaveCountGreaterThanOrEqualTo(3);
    }

    // ── Pass 2: reading the answer ────────────────────────────────────────────

    [Fact]
    public async Task Pass2_RetriesAnAnswerThatLeavesNamesUnanswered()
    {
        // Measured on the first real corpus run: one batch came back empty — valid JSON, no
        // exception — and nine names were silently demoted to the keyword categoriser this phase
        // exists to stop relying on. One object per name is the prompt's central instruction, so
        // failing it is detectable and worth one more call.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("onions", 2m, "pcs", "2 onions")]));

        var llm = new StubLlmStructuredClient(call => call == 1
            ? GroupingAnswer()
            : GroupingAnswer(
                (0, "onion",  0, "Onion",  IngredientCategory.Produce),
                (1, "onions", 0, "Onions", IngredientCategory.Produce)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(2);
        result.NamesRecoveredFromGaps.Should().Be(0);

        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();
        entry.Name.Should().Be("onion");
        entry.Aliases.Should().Equal("onions");
    }

    [Fact]
    public async Task Pass2_RetriesAPartialAnswerNotOnlyAnEmptyOne()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"),
                   ("onions", 2m, "pcs", "2 onions"),
                   ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        var llm = new StubLlmStructuredClient(call => call == 1
            ? GroupingAnswer(
                (0, "onion",  0, "Onion",  IngredientCategory.Produce),
                (1, "onions", 0, "Onions", IngredientCategory.Produce))
            : GroupingAnswer(
                (0, "onion",   0, "Onion",   IngredientCategory.Produce),
                (1, "onions",  0, "Onions",  IngredientCategory.Produce),
                (2, "paprika", 2, "Paprika", IngredientCategory.DryGoods)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(2);
        result.NamesRecoveredFromGaps.Should().Be(0);
    }

    [Fact]
    public async Task Pass2_GivesUpAfterTheRetryBudgetAndKeepsWhatItGot()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        // Never answers for 'paprika', however many times it is asked.
        var llm = new StubLlmStructuredClient(
            GroupingAnswer((0, "onion", 0, "Onion", IngredientCategory.Produce)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1 + _options.MaxLlmRetries);
        result.Succeeded.Should().BeTrue();
        result.NamesRecoveredFromGaps.Should().Be(1);

        // The answers it did get are kept; only the unanswered name falls back.
        (await BuildCatalogueStore().LoadAsync())
            .Entries.Select(e => e.Name).Should().BeEquivalentTo(["onion", "paprika"]);
    }

    [Fact]
    public async Task Pass2_KeepsTheBestAttemptWhenALaterOneFails()
    {
        // A retry that throws must not cost the grouping an earlier attempt already earned.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"),
                   ("onions", 2m, "pcs", "2 onions"),
                   ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        var llm = new StubLlmStructuredClient(call => call == 1
            ? GroupingAnswer(
                (0, "onion",  0, "Onion",  IngredientCategory.Produce),
                (1, "onions", 0, "Onions", IngredientCategory.Produce))
            : throw new InvalidOperationException("model fell over"));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        result.Succeeded.Should().BeTrue();

        // Only the name attempt 1 never answered falls back.
        result.NamesRecoveredFromGaps.Should().Be(1);

        var entries = await BuildCatalogueStore().LoadAsync();
        entries.Entries.Single(e => e.Name == "onion").Aliases.Should().Equal("onions");
    }

    [Fact]
    public async Task Pass2_AFailedBatchCostsGroupingNotNames()
    {
        var manifest = await SeedCorpusAsync(
            ("soup", [("onion", 1m, "pcs", "1 onion"), ("onions", 2m, "pcs", "2 onions")]));

        var llm    = StubLlmStructuredClient.AlwaysFailing();
        var result = await BuildBuilder(llm).BuildAsync(manifest);

        // An over-split catalogue, not a lost one — which is the direction §5's tie-break already
        // says to fail in.
        result.Succeeded.Should().BeTrue();
        result.NamesRecoveredFromGaps.Should().Be(2);

        var written = await BuildCatalogueStore().LoadAsync();
        written.Entries.Select(e => e.Name).Should().BeEquivalentTo(["onion", "onions"]);
    }

    [Fact]
    public async Task Pass2_AnAnswerOutOfStepWithTheListIsNotTrusted()
    {
        // Stage 4's silent off-by-one: an answer drifted a label, and only the overrun past the end
        // was detectable. Here label [1] is 'onions', but the answer for [1] is about 'paprika' —
        // and its same_as would fold paprika into onion if it were believed.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"),
                   ("onions", 1m, "pcs", "1 onions"),
                   ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion",   0, "Onion",   IngredientCategory.Produce),
            (1, "paprika", 0, "Paprika", IngredientCategory.DryGoods)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        // Drift leaves names unanswered, which the retry loop sees.
        llm.CallCount.Should().Be(1 + _options.MaxLlmRetries);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Select(e => e.Name).Should().BeEquivalentTo(["onion", "onions", "paprika"]);
        entries.Should().OnlyContain(e => e.Aliases.Count == 0);
        result.NamesRecoveredFromGaps.Should().Be(2);
    }

    [Fact]
    public async Task Pass2_AnEchoThatDropsABracketedAsideIsStillFaithful()
    {
        // Measured on the first run of this shape: the model copied 'paprika (optional)' as
        // 'paprika', an exact check called it drift, and the batch spent two retries recovering an
        // answer that was right the first time.
        var manifest = await SeedCorpusAsync(
            ("a", [("paprika (optional)", 1m, "tsp", "1/4 teaspoon paprika (optional)")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "paprika", 0, "Paprika", IngredientCategory.Condiments)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1);
        result.NamesRecoveredFromGaps.Should().Be(0);
        (await BuildCatalogueStore().LoadAsync()).Entries.Single().Category
            .Should().Be(IngredientCategory.Condiments, "the model's answer was used, not the keyword fallback");
    }

    [Theory]
    [InlineData("green onions",                   1, true)]   // aside dropped
    [InlineData("GREEN ONIONS (SCALLIONS)",       1, true)]   // case only
    [InlineData("green onions scallions",         1, true)]   // brackets dropped, words kept
    [InlineData("chopped chives",                 2, true)]   // leading words of the name
    [InlineData("onions",                         1, false)]  // not how the name begins
    [InlineData("paprika",                        1, false)]  // another label's name exactly: drift
    [InlineData("",                               0, false)]
    public void IsFaithfulEcho_AllowsTidyingButNeverANeighboursName(string echoed, int label, bool faithful)
    {
        var batch = new[]
        {
            Name("paprika"), Name("green onions (scallions)"), Name("chopped chives or green onions (scallions)"),
        };

        IngredientCatalogueBuilder.IsFaithfulEcho(echoed, label, batch).Should().Be(faithful);
    }

    [Fact]
    public async Task Pass2_AChainOfSameAsResolvesToOneGroup()
    {
        // Union-find: [2] → [1] → [0] is one purchase however the pointers are arranged, and a
        // mutual pair is not a contradiction.
        var manifest = await SeedCorpusAsync(
            ("a", [("tomatoes", 1m, "pcs", "1 tomatoes"), ("tomatoes", 1m, "pcs", "1 tomatoes"),
                   ("tomatoes", 1m, "pcs", "1 tomatoes"),
                   ("tomato", 1m, "pcs", "1 tomato"), ("tomato", 1m, "pcs", "1 tomato"),
                   ("fresh tomatoes", 1m, "pcs", "1 fresh tomatoes")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "tomatoes",       1, "Tomatoes",       IngredientCategory.Produce),
            (1, "tomato",         0, "Tomato",         IngredientCategory.Produce),
            (2, "fresh tomatoes", 1, "Fresh Tomatoes", IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();

        entry.Name.Should().Be("tomatoes");
        entry.Aliases.Should().BeEquivalentTo(["fresh tomatoes", "tomato"]);
        entry.DisplayName.Should().Be("Tomatoes", "the display name comes from the member the entry is named after");
    }

    [Fact]
    public async Task Pass2_ANameNobodyAnsweredForJoinsTheGroupThatPointsAtIt()
    {
        // The pointer is the model's judgement about the unanswered name, so it is not thrown away.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("onion", 1m, "pcs", "1 onion"),
                   ("onions", 1m, "pcs", "1 onions")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (1, "onions", 0, "Onions", IngredientCategory.Frozen)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        result.NamesRecoveredFromGaps.Should().Be(0);

        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();

        entry.Name.Should().Be("onion");
        entry.Aliases.Should().Equal("onions");

        // From the member that did answer, not from the keyword table.
        entry.Category.Should().Be(IngredientCategory.Frozen);
    }

    [Fact]
    public async Task Pass2_ASameAsOutsideTheBatchStandsAlone()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion",   0, "Onion",   IngredientCategory.Produce),
            (1, "paprika", 7, "Paprika", IngredientCategory.DryGoods)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1, "both names were answered; only the pointer was bad");
        result.NamesRecoveredFromGaps.Should().Be(0);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;
        entries.Select(e => e.Name).Should().BeEquivalentTo(["onion", "paprika"]);
        entries.Single(e => e.Name == "paprika").Category.Should().Be(IngredientCategory.DryGoods);
    }

    [Fact]
    public async Task Pass2_ACollapsedAnswerIsRetried()
    {
        // Measured: one batch of 36 came back with toothpicks, tortellini and whipped topping all
        // pointing at 'tomatoes'. Every name was answered, so the unanswered count saw nothing wrong.
        var manifest = await FiveFamilyCorpusAsync();

        var llm = new StubLlmStructuredClient(call => call == 1
            ? GroupingAnswer(FiveFamilyAnswer(sameAs: _ => 0))
            : GroupingAnswer(FiveFamilyAnswer(sameAs: label => label)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(2);
        result.NamesRecoveredFromGaps.Should().Be(0);
        (await BuildCatalogueStore().LoadAsync()).Entries.Should().HaveCount(5)
            .And.OnlyContain(entry => entry.Aliases.Count == 0);
    }

    [Fact]
    public async Task Pass2_AGroupStillCollapsedAfterEveryRetryIsDissolved()
    {
        var manifest = await FiveFamilyCorpusAsync();

        var llm = new StubLlmStructuredClient(GroupingAnswer(FiveFamilyAnswer(sameAs: _ => 0)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1 + _options.MaxLlmRetries);
        result.NamesRecoveredFromGaps.Should().Be(0, "every name was answered; only the grouping was refused");

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Should().HaveCount(5).And.OnlyContain(entry => entry.Aliases.Count == 0);

        // Each name keeps the model's own answer rather than falling back to the keyword table.
        entries.Should().OnlyContain(entry => entry.Category == IngredientCategory.Frozen);
    }

    [Fact]
    public async Task Pass2_AGroupAtTheFamilyCapIsKept()
    {
        // Spelling variants and asides legitimately cross family keys — 'fillet' and 'filet' — and
        // the measured corpus maximum for a real merge was three.
        var manifest = await SeedCorpusAsync(
            ("a", [("tilapia fillet", 1m, "pcs", "2 tilapia fillets"), ("tilapia fillet", 1m, "pcs", "2 tilapia fillets"),
                   ("tilapia filet", 1m, "pcs", "1 tilapia filet"),
                   ("tilapia fillets (skinless)", 1m, "pcs", "1 tilapia fillets (skinless)")]));

        // Families ordinally: '(skinless)', 'filet', 'fillet'.
        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "tilapia fillets (skinless)", 2, "Tilapia Fillets", IngredientCategory.MeatSeafood),
            (1, "tilapia filet",              2, "Tilapia Filet",   IngredientCategory.MeatSeafood),
            (2, "tilapia fillet",             2, "Tilapia Fillet",  IngredientCategory.MeatSeafood)));

        await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1);
        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();
        entry.Name.Should().Be("tilapia fillet");
        entry.Aliases.Should().BeEquivalentTo(["tilapia filet", "tilapia fillets (skinless)"]);
    }

    [Fact]
    public async Task Pass2_AMergeTheVocabularyCannotExplainIsKeptApart()
    {
        // Measured on the benchmark batches: the model put lasagna noodles into egg noodles on every
        // prompt tried, including one naming that exact shape. §5's merge list cannot explain the
        // difference, so the two stay apart — each with the model's own answer, not a fallback.
        var manifest = await SeedCorpusAsync(
            ("a", [("egg noodles", 1m, "cup", "1 pound egg noodles"), ("egg noodles", 1m, "cup", "1 pound egg noodles"),
                   ("lasagna noodles", 1m, "cup", "8 ounces lasagna noodles"),
                   ("wide egg noodles", 1m, "cup", "2 cups wide egg noodles")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "egg noodles",      0, "Egg Noodles",      IngredientCategory.DryGoods),
            (1, "lasagna noodles",  0, "Lasagna Noodles",  IngredientCategory.DryGoods),
            (2, "wide egg noodles", 0, "Wide Egg Noodles", IngredientCategory.DryGoods)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        llm.CallCount.Should().Be(1, "a refined answer is a complete answer, not one to retry");
        result.NamesRecoveredFromGaps.Should().Be(0);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Select(e => e.Name).Should().BeEquivalentTo(["egg noodles", "lasagna noodles", "wide egg noodles"]);
        entries.Single(e => e.Name == "lasagna noodles").DisplayName.Should().Be("Lasagna Noodles");
        entries.Should().OnlyContain(e => e.Aliases.Count == 0);
    }

    [Fact]
    public async Task Pass2_TheVocabularyKeepsTheMergesItCanExplainAndSplitsTheRest()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("chicken broth", 1m, "cup", "2 cups chicken broth"), ("chicken broth", 1m, "cup", "2 cups chicken broth"),
                   ("low-sodium chicken broth", 1m, "cup", "1 cup low-sodium chicken broth"),
                   ("chicken or vegetable broth", 1m, "cup", "4 cups chicken or vegetable broth")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "chicken broth",              0, "Chicken Broth",              IngredientCategory.Canned),
            (1, "low-sodium chicken broth",   0, "Low-Sodium Chicken Broth",   IngredientCategory.Canned),
            (2, "chicken or vegetable broth", 0, "Chicken Or Vegetable Broth", IngredientCategory.Canned)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        // 'chicken stock' is there too, from the regional spelling table.
        entries.Single(e => e.Name == "chicken broth").Aliases
            .Should().Contain("low-sodium chicken broth").And.NotContain("chicken or vegetable broth");
        entries.Should().Contain(e => e.Name == "chicken or vegetable broth" && e.Aliases.Count == 0);
    }

    /// <summary>Five names in five families, which sort as butter, onion, paprika, rice, salt.</summary>
    private Task<SeedManifest> FiveFamilyCorpusAsync() => SeedCorpusAsync(
        ("a", [("onion", 1m, "pcs", "1 onion"), ("paprika", 1m, "tsp", "1 teaspoon paprika"),
               ("salt", 1m, "tsp", "1 teaspoon salt"), ("rice", 1m, "cup", "1 cup rice"),
               ("butter", 1m, "tbsp", "1 tablespoon butter")]));

    private static (int, string, int, string, string)[] FiveFamilyAnswer(Func<int, int> sameAs) =>
        [.. new[] { "butter", "onion", "paprika", "rice", "salt" }
            .Select((name, label) => (label, name, sameAs(label), name, IngredientCategory.Frozen))];

    [Fact]
    public async Task Pass2_ANameAnsweredTwiceKeepsTheFirstAnswer()
    {
        var manifest = await SeedCorpusAsync(("a", [("onion", 1m, "pcs", "1 onion")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", IngredientCategory.Produce),
            (0, "onion", 0, "Onion", IngredientCategory.Dairy)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();

        entry.Name.Should().Be("onion");
        entry.Category.Should().Be(IngredientCategory.Produce);
    }

    // ── Preview: --build-catalogue --batch ────────────────────────────────────

    [Fact]
    public async Task Preview_RunsOnlyTheNamedBatchesAndWritesNothing()
    {
        _options = new RecipeSeedingOptions
        {
            CacheDirectory          = _options.CacheDirectory,
            CatalogueFilePath       = _options.CatalogueFilePath,
            CatalogueMinimumEntries = 1,
            CatalogueBatchSize      = 1,
        };

        // Batch size 1: 'apple' is batch 1 and 'onion' batch 2.
        var manifest = await SeedCorpusAsync(
            ("a", [("apple", 1m, "pcs", "1 apple"), ("onion", 1m, "pcs", "1 onion")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", IngredientCategory.Produce)));

        var preview = await BuildBuilder(llm).PreviewAsync(manifest, [2, 9]);

        llm.Calls.Should().ContainSingle().Which.UserContent.Should().Contain("[0] onion")
            .And.NotContain("apple");

        preview.Should().Be(new CataloguePreviewResult(
            TotalBatches: 2, BatchesRun: 1, Names: 1, Groups: 1, LlmCalls: 1));

        BuildCatalogueStore().Exists().Should().BeFalse("a preview never writes the artefact");
    }

    // ── Pass 3: derived names, units and display names ───────────────────────

    [Fact]
    public async Task Pass3_DefaultUnitIsTheModalUnitOverTheMergedGroup()
    {
        // 'onion' is pcs in most lines and cup in some; merged with 'onions', the answer is still
        // pcs — which is how you buy one. Derived from 8,882 observations, never asked for.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("onion", 1m, "pcs", "1 onion")]),
            ("b", [("onion", 1m, "cup", "1 cup onion"), ("onions", 2m, "pcs", "2 onions")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion",  0, "Onion",  IngredientCategory.Produce),
            (1, "onions", 0, "Onions", IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var written = await BuildCatalogueStore().LoadAsync();
        written.Entries.Single().DefaultUnit.Should().Be(MeasurementUnit.Piece);
    }

    [Fact]
    public async Task Pass3_DefaultUnitIsNullWhenNoRowInTheGroupIsQuantified()
    {
        // Phase 9.1's population — 'salt and pepper', 'nonstick cooking spray'. The column allows
        // null, and inventing a unit for something the corpus never measures would be a guess.
        var manifest = await SeedCorpusAsync(
            ("a", [("nonstick cooking spray", null, null, "nonstick cooking spray")]),
            ("b", [("nonstick cooking spray", null, null, "nonstick cooking spray")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "nonstick cooking spray", 0, "Nonstick Cooking Spray", IngredientCategory.Condiments)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var written = await BuildCatalogueStore().LoadAsync();
        written.Entries.Single().DefaultUnit.Should().BeNull();
    }

    [Fact]
    public async Task Pass3_TheModelsDisplayNameWinsOverTheCorpusModalForm()
    {
        // §2.7: the modal display name is sometimes a leaked worked example. 13 rows across 4
        // recipes carry 'Diced Tomatoes' on things that are not tomatoes — dirty-rice has it on all
        // ten of its rows, including 'water' and 'black pepper'.
        var manifest = await SeedCorpusAsync(
            ("dirty-rice", [("water", 2m, "cup", "2 cups water")]),
            ("other",      [("water", 1m, "cup", "1 cup water")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "water", 0, "Water", IngredientCategory.Beverages)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var written = await BuildCatalogueStore().LoadAsync();
        written.Entries.Single().DisplayName.Should().Be("Water");
    }

    [Fact]
    public async Task Pass3_FallsBackToTheCorpusModalDisplayNameTitleCased()
    {
        var manifest = await SeedCorpusAsync(("a", [("olive oil", 1m, "tbsp", "1 tablespoon olive oil")]));

        // The model answered with a blank display name; the corpus form is the fallback.
        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "olive oil", 0, "", IngredientCategory.Condiments)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var written = await BuildCatalogueStore().LoadAsync();
        written.Entries.Single().DisplayName.Should().Be("Olive Oil");
    }

    [Fact]
    public async Task Pass3_MembersBecomeAliasesAndTheCanonicalNameDoesNot()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("milk", 1m, "cup", "1 cup milk"),
                   ("skim milk", 1m, "cup", "1 cup skim milk"),
                   ("fat-free milk", 1m, "cup", "1 cup fat-free milk")]));

        // A three-way row tie, so batch order is shortest first.
        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "milk",          0, "Milk",          IngredientCategory.Dairy),
            (1, "skim milk",     0, "Skim Milk",     IngredientCategory.Dairy),
            (2, "fat-free milk", 0, "Fat-Free Milk", IngredientCategory.Dairy)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entry = (await BuildCatalogueStore().LoadAsync()).Entries.Single();

        entry.Name.Should().Be("milk");
        entry.Aliases.Should().BeEquivalentTo(["fat-free milk", "skim milk"]);
        entry.Aliases.Should().NotContain("milk");
    }

    [Fact]
    public async Task Pass3_TheEntryIsNamedAfterItsMostUsedMember()
    {
        // The model is never asked for a name. A proposed one could be another batch's corpus name —
        // one string both an entry's name and an alias — and no batch can see that.
        var manifest = await SeedCorpusAsync(
            ("a", [("tomatoes", 1m, "pcs", "1 tomatoes"), ("tomatoes", 1m, "pcs", "1 tomatoes"),
                   ("tomato", 1m, "pcs", "1 tomato")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "tomatoes", 0, "Tomatoes", IngredientCategory.Produce),
            (1, "tomato",   0, "Tomato",   IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Single().Name.Should().Be("tomatoes");
        entries.Single().Aliases.Should().Equal("tomato");
        IngredientCatalogueValidator.Validate(entries).Should().BeEmpty();
    }

    [Fact]
    public async Task Pass3_RecoversANameTheModelNeverAnswered()
    {
        // §4.7 rule 5 makes an unreachable name a build failure, and a dropped name is a recipe
        // Stage 5 cannot persist — so a gap in the answer is repaired rather than propagated.
        var manifest = await SeedCorpusAsync(
            ("a", [("onion", 1m, "pcs", "1 onion"), ("paprika", 1m, "tsp", "1 teaspoon paprika")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", IngredientCategory.Produce)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        result.Succeeded.Should().BeTrue();
        result.NamesRecoveredFromGaps.Should().Be(1);

        (await BuildCatalogueStore().LoadAsync())
            .Entries.Select(e => e.Name).Should().Contain("paprika");
    }

    // ── Regional spellings ────────────────────────────────────────────────────

    [Fact]
    public async Task RegionalSpellings_AttachToTheirCorpusEntry()
    {
        // §2.2's real problem: 'capsicum' seeded as its own row sat permanently beside
        // 'bell pepper' and never consolidated in a shopping list.
        var manifest = await SeedCorpusAsync(("a", [("bell pepper", 1m, "pcs", "1 bell pepper")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "bell pepper", 0, "Bell Pepper", IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        (await BuildCatalogueStore().LoadAsync())
            .Entries.Single().Aliases.Should().Contain("capsicum");
    }

    [Fact]
    public async Task RegionalSpellings_AttachThroughAnAliasWhenTheEntryIsNamedDifferently()
    {
        // The table names 'beet', but 'beets' is used more and names the entry. Looking the target
        // up by entry name alone would skip 'beetroot' without a word.
        var manifest = await SeedCorpusAsync(
            ("a", [("beets", 1m, "pcs", "2 beets"), ("beets", 1m, "pcs", "3 beets"),
                   ("beet", 1m, "pcs", "1 beet")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "beets", 0, "Beets", IngredientCategory.Produce),
            (1, "beet",  0, "Beet",  IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Single().Name.Should().Be("beets");
        entries.Single().Aliases.Should().BeEquivalentTo(["beet", "beetroot"]);
        IngredientCatalogueValidator.Validate(entries).Should().BeEmpty();
    }

    [Fact]
    public async Task RegionalSpellings_AreSkippedWhenTheSpellingIsItselfACorpusName()
    {
        // 'garbanzo beans' is a corpus name in its own right, so the grouping pass has already
        // judged it on evidence. Overriding that from a hardcoded table would contradict the review
        // and risk making one name an alias of two entries.
        var manifest = await SeedCorpusAsync(
            ("a", [("chickpeas", 1m, "cup", "1 cup chickpeas"),
                   ("garbanzo beans", 1m, "cup", "1 cup garbanzo beans")]));

        // Families 'beans' then 'chickpeas', so 'garbanzo beans' carries the first label.
        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "garbanzo beans", 0, "Garbanzo Beans", IngredientCategory.Canned),
            (1, "chickpeas",      1, "Chickpeas",      IngredientCategory.Canned)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var entries = (await BuildCatalogueStore().LoadAsync()).Entries;

        entries.Single(e => e.Name == "chickpeas").Aliases.Should().NotContain("garbanzo beans");
        IngredientCatalogueValidator.Validate(entries).Should().BeEmpty();
    }

    // ── Pass 4: the artefact ──────────────────────────────────────────────────

    [Fact]
    public async Task Artefact_IsOrderedByNameAndCarriesItsProvenance()
    {
        var manifest = await SeedCorpusAsync(
            ("a", [("zucchini", 1m, "pcs", "1 zucchini"), ("apple", 1m, "pcs", "1 apple")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "apple",    0, "Apple",    IngredientCategory.Produce),
            (1, "zucchini", 1, "Zucchini", IngredientCategory.Produce)));

        await BuildBuilder(llm).BuildAsync(manifest);

        var written = await BuildCatalogueStore().LoadAsync();

        written.Entries.Select(e => e.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
        written.Source.Corpus.Should().Be("myplate");
        written.Source.Recipes.Should().Be(1);
        written.Source.IngredientRows.Should().Be(2);
        written.Source.DistinctNames.Should().Be(2);
    }

    [Fact]
    public async Task Artefact_IsNotWrittenWhenValidationFails()
    {
        _options = new RecipeSeedingOptions
        {
            CacheDirectory          = _options.CacheDirectory,
            CatalogueFilePath       = _options.CatalogueFilePath,
            CatalogueMinimumEntries = 500,
        };

        var manifest = await SeedCorpusAsync(("a", [("onion", 1m, "pcs", "1 onion")]));
        var llm      = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", IngredientCategory.Produce)));

        var result = await BuildBuilder(llm).BuildAsync(manifest);

        result.Succeeded.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle(e => e.Contains("below the configured floor"));
        BuildCatalogueStore().Exists().Should().BeFalse();
    }

    [Fact]
    public async Task Artefact_UsesTheKeywordTableWhenTheModelNamesNoRealCategory()
    {
        var manifest = await SeedCorpusAsync(("a", [("onion", 1m, "pcs", "1 onion")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "onion", 0, "Onion", "VEGETABLES_AND_THINGS")));

        await BuildBuilder(llm).BuildAsync(manifest);

        (await BuildCatalogueStore().LoadAsync())
            .Entries.Single().Category.Should().Be(IngredientCategory.Produce);
    }

    [Fact]
    public async Task Artefact_AcceptsACategoryTheModelSpelledInLowercase()
    {
        var manifest = await SeedCorpusAsync(("a", [("milk", 1m, "cup", "1 cup milk")]));

        var llm = new StubLlmStructuredClient(GroupingAnswer(
            (0, "milk", 0, "Milk", "dairy")));

        await BuildBuilder(llm).BuildAsync(manifest);

        (await BuildCatalogueStore().LoadAsync())
            .Entries.Single().Category.Should().Be(IngredientCategory.Dairy);
    }
}
