using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Derives a reference ingredient catalogue from the normalised corpus in four passes, only the second using the model.</summary>
public class IngredientCatalogueBuilder(
    SeedCacheStore cache,
    CatalogueBuildFileStore buildStore,
    ILlmStructuredClient llm,
    IOptions<RecipeSeedingOptions> options,
    ILogger<IngredientCatalogueBuilder> logger)
{
    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>Verbatim source lines shown to the model per name.</summary>
    public const int SampleLinesPerName = 3;

    /// <summary>Corpus identifier recorded in the artefact's provenance block.</summary>
    private const string CorpusName = "myplate";

    // ── The grouping schema ───────────────────────────────────────────────────

    /// <summary>The grouping schema: one object per labelled name, properties in decode order and all required so the grammar can emit them.</summary>
    internal static readonly string GroupingSchemaJson = $$"""
        {
          "type": "object",
          "properties": {
            "names": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "label":        { "type": "integer" },
                  "name":         { "type": "string" },
                  "same_as":      { "type": "integer" },
                  "display_name": { "type": "string" },
                  "category":     { "type": "string", "enum": {{JsonSerializer.Serialize(IngredientCategory.All)}} }
                },
                "required": ["label", "name", "same_as", "display_name", "category"]
              }
            }
          },
          "required": ["names"]
        }
        """;

    /// <summary>One name of the worked example, with the answer the prompt teaches for it.</summary>
    internal sealed record WorkedExampleName(
        string Name, int Rows, string SourceLine, int SameAs, string DisplayName, string Category);

    /// <summary>The worked example, held as data so the prompt and the tests render it through the same code as a real batch.</summary>
    internal static readonly IReadOnlyList<WorkedExampleName> WorkedExample =
    [
        new("plums",                  40, "4 ripe plums, pitted and sliced",            0, "Plums",                  IngredientCategory.Produce),
        new("canned plums",           12, "1 can (15 ounces) plums in juice, drained",  1, "Canned Plums",           IngredientCategory.Canned),
        new("plum",                    9, "1 plum, diced",                              0, "Plum",                   IngredientCategory.Produce),
        new("dried plums",             6, "1/2 cup dried plums, chopped",               3, "Dried Plums",            IngredientCategory.DryGoods),
        new("red plums",               4, "3 red plums, sliced",                        4, "Red Plums",              IngredientCategory.Produce),
        new("low-sugar canned plums",  3, "1 can (15 ounces) low-sugar plums, drained", 1, "Low-Sugar Canned Plums", IngredientCategory.Canned),
        new("fresh plums",             2, "2 fresh plums, sliced",                      0, "Fresh Plums",            IngredientCategory.Produce),
        new("apricots or plums",       1, "2 apricots or plums, halved",                7, "Apricots Or Plums",      IngredientCategory.Produce),
        new("plum sauce",              1, "2 tablespoons plum sauce",                   8, "Plum Sauce",             IngredientCategory.Condiments),
    ];

    /// <summary>Aisle boundaries for the categories whose names alone do not settle it, declared before the prompt built from it.</summary>
    internal static readonly string CategoryAisleRules =
        $"- {IngredientCategory.BakingSpices}: salt of every kind (table, kosher, garlic, celery, seasoned), " +
        "pepper, spices, seasoning blends, extracts, flour, sugar, baking powder and soda, yeast, " +
        "cornstarch, cornmeal, cocoa and chocolate chips. A name combining salt and pepper is here too.\n" +
        $"- Herbs: dried or ground herbs, or herbs the source lines measure by the teaspoon, are " +
        $"{IngredientCategory.BakingSpices} whether or not the name says dried. Fresh sprigs, bunches and " +
        $"leaves are {IngredientCategory.Produce}.\n" +
        $"- {IngredientCategory.DryGoods}: nuts, seeds, dried fruit, dried beans and lentils, breakfast " +
        "cereal, crackers and snacks. Not salt, not flour, not sugar.\n" +
        $"- {IngredientCategory.GrainsRice}: rice, oats, quinoa, couscous, barley and bulgur. Breakfast " +
        $"cereal is {IngredientCategory.DryGoods}.\n" +
        $"- {IngredientCategory.PastaSauces}: dried pasta, noodles, and pasta or pizza sauce. Plain canned " +
        $"tomato sauce is {IngredientCategory.Canned}; salsa, taco, picante and enchilada sauces are " +
        $"{IngredientCategory.Condiments}.\n" +
        $"- {IngredientCategory.JamNutButter}: honey, maple syrup and every other syrup, jams, jellies, " +
        "preserves, and peanut and other nut butters.\n" +
        $"- {IngredientCategory.SoupsBroth}: broth, stock and bouillon of every kind and packaging — " +
        "chicken, beef and vegetable, cubes, granules and cartons. Condensed and ready-to-eat soups, " +
        "including every cream-of soup, and soup and soup-base mixes.\n" +
        $"- {IngredientCategory.Canned}: canned vegetables, beans, fruit and tomatoes. Anything named " +
        $"broth, stock, bouillon or soup is {IngredientCategory.SoupsBroth}, canned or not.\n" +
        $"- {IngredientCategory.Dairy}: milk, cream, cheese and cheese spreads, yogurt, butter, margarine " +
        $"and buttery spreads, eggs and egg substitutes. A cream-of or cheese soup is " +
        $"{IngredientCategory.SoupsBroth} however much dairy it names.\n" +
        $"- {IngredientCategory.BakingSpices} also holds gelatin.\n" +
        $"- {IngredientCategory.Bakery}: bread, rolls, buns, tortillas, pita and pizza crusts or shells.\n" +
        $"- {IngredientCategory.Beverages}: water of any temperature, drinks, wine and beer. Broth and " +
        $"stock are {IngredientCategory.SoupsBroth}, even where a name offers water as the alternative.\n" +
        $"- {IngredientCategory.CoffeeTea}: coffee and tea.\n" +
        $"- {IngredientCategory.Condiments}: oils, cooking sprays, vinegars, savoury sauces, salsa, " +
        "dressings, mustard, ketchup and mayonnaise. Not syrups.\n" +
        $"- {IngredientCategory.Kitchen}: things used to cook or serve that are not eaten — foil, " +
        "parchment, skewers, toothpicks, straws, popsicle and craft sticks, paper cups, muffin liners.\n" +
        $"- {IngredientCategory.Household}: cleaning supplies, paper towels and toiletries — never " +
        "anything a recipe uses.\n" +
        $"- {IngredientCategory.Other}: only when no other value fits.\n";

    /// <summary>The merge rules as a per-name <c>same_as</c> decision, with the tie-break last because an over-split catalogue is the safe failure.</summary>
    private static readonly string GroupingSystemPrompt = BuildSystemPrompt();

    /// <summary>Exposed so a test can assert the prompt states the rules and proposes no grouping.</summary>
    internal static string SystemPrompt => GroupingSystemPrompt;

    private static string BuildSystemPrompt()
    {
        var prompt = new StringBuilder();

        prompt.Append(
            "You are a grocery data assistant. The user gives you ingredient names taken from a recipe " +
            "library, each with a label in square brackets, how many recipe lines use it, and up to " +
            $"{SampleLinesPerName} of those lines verbatim.\n" +
            "\n" +
            "Answer with one object per name: the same names, in the same order, each exactly once. " +
            "Never combine two names into one object, never add a name and never leave one out.\n" +
            "\n" +
            "label: the name's bracket label, copied exactly.\n" +
            "name: the name itself, copied exactly.\n" +
            "\n" +
            "same_as: if an EARLIER name in the list is the same thing to buy, that earlier name's " +
            "label. Otherwise this name's own label. Most of these names are DIFFERENT purchases, so " +
            "most names use their own label.\n" +
            "\n" +
            "The test for the same thing to buy: if a shopping list said only the earlier name, would " +
            "the shopper certainly bring home what this name's recipe lines need? If they could bring " +
            "home the wrong thing, it is a DIFFERENT purchase.\n" +
            "\n" +
            "The names you are given usually share a word. That is only how they were selected for you " +
            "— it is NOT evidence that they are the same thing. Two names ending in the same word are " +
            "different purchases until their source lines show otherwise.\n" +
            "\n" +
            "display_name: this name in title case, as a shopping list would print it.\n" +
            "\n" +
            $"category: MUST be exactly one value from this list: {string.Join(", ", IngredientCategory.All)}.\n" +
            "Judge it by the aisle the item is bought from, not by a word inside its name. Peanut " +
            "butter is not dairy. Chicken broth is not meat. Egg noodles are not dairy.\n" +
            CategoryAisleRules +
            "\n" +
            "Read the source lines before deciding same_as, because the name alone is often not " +
            "enough evidence.\n" +
            "\n" +
            "The SAME purchase — point same_as at the earlier name — when the difference does not " +
            "change what is bought:\n" +
            "- singular and plural — 'onions' and 'onion'\n" +
            "- fat, sodium and sugar variants — 'skim milk', 'fat-free milk' and 'milk'\n" +
            "- cut and size descriptors — 'medium onion', 'diced onion'\n" +
            "- redundant qualifiers — 'fresh cilantro' and 'cilantro'\n" +
            "\n" +
            "A DIFFERENT purchase — use its own label — when the difference changes the aisle or the " +
            "product:\n" +
            "- preservation state — canned, frozen and dried are separate purchases from fresh. " +
            "'diced tomatoes' comes in a can; 'dried cranberries' is not 'cranberries'\n" +
            "- cooked and raw where both are bought — 'cooked rice' is not 'rice'\n" +
            "- different species or cultivar — 'green onion' is not 'onion'; 'sweet potato' is not 'potato'\n" +
            "- a particular kind of a more general name — a variety, type, flavour or form. The shorter " +
            "name is the general word and this one is a specific product: 'jasmine rice' is not " +
            "'rice', 'rye bread' is not 'bread', 'spelt flour' is not 'flour', 'cinnamon sticks' is " +
            "not 'cinnamon'\n" +
            "- a choice between things — 'apples or pears' names two purchases, so it is neither one\n" +
            "- a branded or compound product — 'cream of chicken soup' is not 'chicken broth'\n" +
            "\n" +
            "When a rule is arguable, use its own label. Two lines in a shopping list are visible and " +
            "correctable; a wrongly merged one silently adds a can of tomatoes to a bag of fresh ones.\n" +
            "\n" +
            "Worked example. For these names:\n");

        for (var label = 0; label < WorkedExample.Count; label++)
        {
            var example = WorkedExample[label];
            AppendName(prompt, label, example.Name, example.Rows, [example.SourceLine]);
        }

        prompt.Append("the answer is:\n")
              .Append("{\"names\":[\n");

        for (var label = 0; label < WorkedExample.Count; label++)
        {
            var example = WorkedExample[label];
            var answer  = new CatalogueNameResponse(
                label, example.Name, example.SameAs, example.DisplayName, example.Category);

            prompt.Append(JsonSerializer.Serialize(answer))
                  .Append(label < WorkedExample.Count - 1 ? ",\n" : "]}\n");
        }

        prompt.Append(
            "Nine names gave nine objects, and six different purchases. 'plum' and 'fresh plums' are " +
            "the same fruit as 'plums', so both point at [0]. 'low-sugar canned plums' is a sugar " +
            "variant of 'canned plums', so it points at [1]. 'red plums' is a particular variety and " +
            "'apricots or plums' is a choice, so a list saying only 'plums' could bring home the wrong " +
            "thing, and each keeps its own label. The canned, the dried and the sauce are each bought " +
            "separately, in three different aisles, so each keeps its own label too.\n" +
            "\n" +
            "Respond with a single JSON object conforming to the schema and nothing else.");

        return prompt.ToString();
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>Runs all four passes and writes the build output, but only if the validator rules hold and the entry floor is met.</summary>
    public async Task<CatalogueBuildResult> BuildAsync(
        SeedManifest manifest, CancellationToken ct = default)
    {
        var corpus = await CollectAsync(manifest, ct);

        logger.LogInformation(
            "Collected {Names} distinct ingredient names from {Rows} rows across {Recipes} recipes.",
            corpus.Names.Count, corpus.Rows, corpus.Recipes);

        var grouping = await GroupAsync(corpus.Names, onlyBatches: null, ct);
        var entries  = BuildEntries(grouping.Groups, corpus.Names, out var recovered);

        var errors = new List<string>();
        errors.AddRange(IngredientCatalogueValidator.Validate(
            [.. entries.Select(entry => entry.ToCatalogueEntry())]));

        if (entries.Count < _options.CatalogueMinimumEntries)
            errors.Add($"Only {entries.Count} entries were produced, below the configured floor of " +
                       $"{_options.CatalogueMinimumEntries}. A short catalogue means the grouping " +
                       "pass collapsed the corpus, not that the corpus is small.");

        var result = new CatalogueBuildResult
        {
            Recipes                = corpus.Recipes,
            IngredientRows         = corpus.Rows,
            DistinctNames          = corpus.Names.Count,
            Entries                = entries.Count,
            LlmCalls               = grouping.Calls,
            NamesRecoveredFromGaps = recovered,
            ValidationErrors       = errors,
        };

        if (errors.Count > 0)
        {
            foreach (var error in errors) logger.LogError("Catalogue validation: {Error}", error);
            return result;
        }

        var file = new CatalogueBuildFile
        {
            GeneratedAt = DateTime.UtcNow,
            Source      = new CatalogueSource(CorpusName, corpus.Recipes, corpus.Rows, corpus.Names.Count),
            Entries     = entries,
        };

        await buildStore.SaveAsync(file, ct);
        return result with { Path = buildStore.Path };
    }

    /// <summary>Runs the grouping pass on the named 1-based batches only, logging every merge and writing nothing.</summary>
    public async Task<CataloguePreviewResult> PreviewAsync(
        SeedManifest manifest, IReadOnlyCollection<int> batchNumbers, CancellationToken ct = default)
    {
        var corpus   = await CollectAsync(manifest, ct);
        var grouping = await GroupAsync(corpus.Names, batchNumbers.ToHashSet(), ct);

        return new CataloguePreviewResult(
            grouping.TotalBatches, grouping.BatchesRun, grouping.NamesJudged, grouping.Groups.Count, grouping.Calls);
    }

    // ── Pass 1: collect (deterministic) ───────────────────────────────────────

    private record CorpusSummary(
        IReadOnlyList<CorpusIngredientName> Names, int Rows, int Recipes);

    /// <summary>Walks <c>normalised/</c> and accumulates, per distinct name, what the later passes may reason from, failing loudly rather than building a partial catalogue.</summary>
    private async Task<CorpusSummary> CollectAsync(SeedManifest manifest, CancellationToken ct)
    {
        var accumulators = new Dictionary<string, NameAccumulator>(StringComparer.OrdinalIgnoreCase);
        var missing      = new List<string>();
        var stale        = new List<string>();

        var rows     = 0;
        var recipes  = 0;
        var verified = 0;

        foreach (var entry in manifest.Recipes)
        {
            ct.ThrowIfCancellationRequested();

            if (!SeedCacheStore.IsValidSlug(entry.Slug))
            {
                missing.Add(entry.Slug);
                continue;
            }

            var normalised = await cache.TryLoadNormalisedAsync(entry.Slug, ct);
            if (normalised is null)
            {
                missing.Add(entry.Slug);
                continue;
            }

            // A missing parsed file is not an error, but one that disagrees with the normalised output would make the catalogue describe a stale corpus.
            var fingerprint = await cache.TryComputeParsedFingerprintAsync(entry.Slug, ct);
            if (fingerprint is not null)
            {
                if (fingerprint != normalised.ParsedFingerprint) stale.Add(entry.Slug);
                else verified++;
            }

            recipes++;

            foreach (var ingredient in normalised.Ingredients)
            {
                rows++;

                var name = ingredient.Name?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(name)) continue;

                if (!accumulators.TryGetValue(name, out var accumulator))
                    accumulators[name] = accumulator = new NameAccumulator(name);

                accumulator.Add(ingredient, entry.Slug);
            }
        }

        if (missing.Count > 0)
            throw new CatalogueValidationException(
                [$"{missing.Count} manifest entries have no normalised recipe " +
                 $"({string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", …" : string.Empty)}). " +
                 $"Run '{SeedRecipesCommand.CommandName} --normalise' first — a catalogue built from " +
                 "part of the corpus cannot resolve the rest of it."]);

        if (stale.Count > 0)
            throw new CatalogueValidationException(
                [$"{stale.Count} normalised recipes were derived from a parsed page that has since " +
                 $"changed ({string.Join(", ", stale.Take(10))}{(stale.Count > 10 ? ", …" : string.Empty)}). " +
                 $"Re-run '{SeedRecipesCommand.CommandName} --normalise' before building the catalogue."]);

        logger.LogInformation(
            "Corpus is complete: {Recipes} recipes, {Verified} of them with their parsed input still " +
            "on disk and matching.", recipes, verified);

        var names = accumulators.Values
            .Select(accumulator => accumulator.ToCorpusName())
            .OrderBy(name => name.Name, StringComparer.Ordinal)
            .ToList();

        return new CorpusSummary(names, rows, recipes);
    }

    /// <summary>Mutable tally for one distinct name while pass 1 walks the corpus.</summary>
    private sealed class NameAccumulator(string name)
    {
        private readonly Dictionary<string, int> _units        = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _displayNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lines        = new(StringComparer.Ordinal);
        private readonly HashSet<string> _recipes              = new(StringComparer.Ordinal);

        private int _rows;
        private int _unquantified;

        public void Add(NormalisedSeedIngredient ingredient, string slug)
        {
            _rows++;
            _recipes.Add(slug);

            if (ingredient.Unit is { } unit && !string.IsNullOrWhiteSpace(unit))
                _units[unit] = _units.GetValueOrDefault(unit) + 1;
            else
                _unquantified++;

            if (!string.IsNullOrWhiteSpace(ingredient.DisplayName))
                _displayNames[ingredient.DisplayName.Trim()] =
                    _displayNames.GetValueOrDefault(ingredient.DisplayName.Trim()) + 1;

            if (!string.IsNullOrWhiteSpace(ingredient.SourceText))
                _lines[ingredient.SourceText.Trim()] = _lines.GetValueOrDefault(ingredient.SourceText.Trim()) + 1;
        }

        public CorpusIngredientName ToCorpusName() => new()
        {
            Name              = name,
            Rows              = _rows,
            RecipeSlugs       = _recipes,
            UnitCounts        = _units,
            UnquantifiedRows  = _unquantified,
            DisplayNameCounts = _displayNames,

            // Most-used lines first, then ordinal, so the sample is stable and a regenerated artefact diffs cleanly.
            SampleLines = [.. _lines
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(SampleLinesPerName)
                .Select(pair => pair.Key)],
        };
    }

    // ── Pass 2: group and categorise (LLM) ────────────────────────────────────

    /// <summary>Batches names by family and asks the model, per name, which earlier name it is the same purchase as.</summary>
    /// <param name="onlyBatches">1-based batch numbers to run, or null for all of them.</param>
    private async Task<GroupingOutcome> GroupAsync(
        IReadOnlyList<CorpusIngredientName> names, IReadOnlySet<int>? onlyBatches, CancellationToken ct)
    {
        var batches    = BuildBatches(names, _options.CatalogueBatchSize);
        var familyKeys = FamilyKeys(names);
        var schema     = JsonNode.Parse(GroupingSchemaJson)!;
        var groups     = new List<CatalogueGroup>();
        var calls      = 0;
        var run        = 0;
        var judged     = 0;

        logger.LogInformation(
            "Grouping {Names} names in {Batches} batched calls (cap {Cap} per batch).",
            names.Count, batches.Count, _options.CatalogueBatchSize);

        foreach (var missing in (onlyBatches ?? new HashSet<int>()).Where(number => number > batches.Count).Order())
            logger.LogWarning("There is no batch {Number}; this corpus makes {Total}.", missing, batches.Count);

        var attemptCap = 1 + Math.Max(0, _options.MaxLlmRetries);

        for (var index = 0; index < batches.Count; index++)
        {
            if (onlyBatches is not null && !onlyBatches.Contains(index + 1)) continue;

            var batch = batches[index];
            run++;
            judged += batch.Count;

            // Keep the best answer seen rather than the last, because a later attempt can throw or come back worse.
            BatchReading? best = null;
            var attemptsUsed = 0;

            for (var attempt = 1; attempt <= attemptCap; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                calls++;
                attemptsUsed = attempt;

                try
                {
                    var answer  = await CompleteAsync(schema, BuildUserContent(batch), ct);
                    var reading = ReadAnswer(answer, batch, familyKeys);

                    if (best is null || reading.Defects < best.Defects) best = reading;

                    // An answer that leaves names out or collapses them has not done the job, so it is retried.
                    if (reading.Defects == 0) break;

                    logger.LogInformation(
                        "[{Index}/{Total}] attempt {Attempt} left {Missed} of {Names} names " +
                        "unanswered and {Collapsed} in collapsed groups; retrying.",
                        index + 1, batches.Count, attempt, reading.Unanswered, batch.Count, reading.Collapsed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed batch is recorded and skipped, and its names fall through to gap recovery as single-name entries.
                    logger.LogWarning(ex,
                        "[{Index}/{Total}] grouping call failed on attempt {Attempt} of {Cap}.",
                        index + 1, batches.Count, attempt, attemptCap);
                }
            }

            var unanswered = best?.Unanswered ?? batch.Count;
            if (unanswered > 0)
                logger.LogWarning(
                    "[{Index}/{Total}] {Missed} of {Names} names were still unanswered after " +
                    "{Attempts} attempt(s).",
                    index + 1, batches.Count, unanswered, batch.Count, attemptsUsed);

            if (best is { Collapsed: > 0 })
                logger.LogWarning(
                    "[{Index}/{Total}] {Collapsed} names were still in a group spanning more than " +
                    "{Cap} families after {Attempts} attempt(s); that group is dissolved and its " +
                    "names stand alone.",
                    index + 1, batches.Count, best.Collapsed, _options.CatalogueMaxFamiliesPerGroup, attemptsUsed);

            var batchGroups = best is null ? [] : AssembleGroups(best, batch, index + 1, batches.Count);

            // Log every merge by name, because a count alone cannot tell a family-dense batch from an over-merging model.
            foreach (var merge in batchGroups.Where(group => group.Members.Count > 1))
                logger.LogInformation(
                    "[{Index}/{Total}] merged: {Canonical} ← {Absorbed} ({Category})",
                    index + 1, batches.Count, merge.Members[0],
                    string.Join(" | ", merge.Members.Skip(1)), merge.Category);

            // Log every category, because most names stand alone and their aisle would otherwise be invisible.
            foreach (var category in IngredientCategory.All)
            {
                var named = batchGroups.Where(group => group.Category == category)
                                       .Select(group => group.Members[0])
                                       .ToList();

                if (named.Count > 0)
                    logger.LogInformation(
                        "[{Index}/{Total}] {Category}: {Names}",
                        index + 1, batches.Count, category, string.Join(" | ", named));
            }

            logger.LogInformation(
                "[{Index}/{Total}] {Names} names → {Groups} groups ({Attempts} attempt(s)).",
                index + 1, batches.Count, batch.Count, batchGroups.Count, attemptsUsed);

            groups.AddRange(batchGroups);
        }

        return new GroupingOutcome(groups, calls, batches.Count, run, judged);
    }

    private sealed record GroupingOutcome(
        IReadOnlyList<CatalogueGroup> Groups, int Calls, int TotalBatches, int BatchesRun, int NamesJudged);

    /// <summary>One attempt's answer: accepted answers by label, the <c>same_as</c> components (flagged when they span too many families), unanswered labels and collapsed names.</summary>
    private sealed record BatchReading(
        IReadOnlyDictionary<int, CatalogueNameResponse> Answers,
        IReadOnlyList<(IReadOnlyList<int> Labels, bool Collapsed)> Components,
        int Unanswered,
        int Collapsed)
    {
        /// <summary>What a retry tries to reduce, and what picks the best attempt.</summary>
        public int Defects => Unanswered + Collapsed;
    }

    /// <summary>Batches names by family (final word, plural folded) so every member of a family is judged in the same call.</summary>
    internal static IReadOnlyList<IReadOnlyList<CorpusIngredientName>> BuildBatches(
        IReadOnlyList<CorpusIngredientName> names, int batchSize)
    {
        var cap        = Math.Max(1, batchSize);
        var familyKeys = FamilyKeys(names);

        var families = names
            .GroupBy(name => familyKeys[name.Name], StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family.Key, StringComparer.Ordinal)
            .Select(family => OrderMostUsedFirst(family, name => name).ToList())
            .ToList();

        var batches = new List<IReadOnlyList<CorpusIngredientName>>();
        var current = new List<CorpusIngredientName>();

        foreach (var family in families)
        {
            // A family too large for any batch is chunked on its own instead of dragging a partly filled batch along.
            if (family.Count > cap)
            {
                if (current.Count > 0) { batches.Add(current); current = []; }
                for (var offset = 0; offset < family.Count; offset += cap)
                    batches.Add(family.Skip(offset).Take(cap).ToList());
                continue;
            }

            if (current.Count + family.Count > cap)
            {
                batches.Add(current);
                current = [];
            }

            current.AddRange(family);
        }

        if (current.Count > 0) batches.Add(current);
        return batches;
    }

    /// <summary>Each name's family key: its final word, with a plural folded onto its singular when the corpus itself uses that singular.</summary>
    internal static IReadOnlyDictionary<string, string> FamilyKeys(IReadOnlyList<CorpusIngredientName> names)
    {
        var finalWords = names
            .Select(name => FinalWord(name.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var word = FinalWord(name.Name);
            keys[name.Name] = SingularCandidates(word).FirstOrDefault(finalWords.Contains) ?? word;
        }

        return keys;
    }

    /// <summary>English plural endings, most specific first; <see cref="FamilyKeys"/> accepts a candidate only when the corpus already uses it.</summary>
    private static IEnumerable<string> SingularCandidates(string word)
    {
        if (word.Length > "ies".Length && word.EndsWith("ies", StringComparison.Ordinal))
            yield return word[..^"ies".Length] + "y";

        if (word.Length > "es".Length && word.EndsWith("es", StringComparison.Ordinal))
            yield return word[..^"es".Length];

        if (word.Length > "s".Length && word.EndsWith('s'))
            yield return word[..^"s".Length];
    }

    /// <summary>The last whitespace-separated word of a name, which is its head noun.</summary>
    private static string FinalWord(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? name : words[^1];
    }

    /// <summary>One batch as the model sees it: labelled names with their corpus frequency and verbatim source lines.</summary>
    internal static string BuildUserContent(IReadOnlyList<CorpusIngredientName> batch)
    {
        var content = new StringBuilder();

        content.Append("Answer for each of these ").Append(batch.Count)
               .Append(" ingredient names, in order, one object per name. Labels run from [0] to [")
               .Append(batch.Count - 1).Append("].\n\n");

        for (var label = 0; label < batch.Count; label++)
            AppendName(content, label, batch[label].Name, batch[label].Rows, batch[label].SampleLines);

        return content.ToString();
    }

    /// <summary>One labelled name and its evidence, shared with the worked example so both render identically.</summary>
    private static void AppendName(
        StringBuilder content, int label, string name, int rows, IEnumerable<string> sampleLines)
    {
        content.Append('[').Append(label).Append("] ").Append(name)
               .Append(" — ").Append(rows)
               .Append(rows == 1 ? " recipe line" : " recipe lines")
               .Append('\n');

        foreach (var line in sampleLines)
            content.Append("      e.g. ").Append(line).Append('\n');
    }

    private async Task<JsonNode> CompleteAsync(JsonNode schema, string userContent, CancellationToken ct)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.LlmTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        try
        {
            return await llm.CompleteStructuredAsync(
                GroupingSystemPrompt, userContent, schema, _options.MaxLlmOutputTokens, linked.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No answer within {_options.LlmTimeoutSeconds}s.");
        }
    }

    /// <summary>Reads one batch's answer, discarding any object whose echoed name does not match its label because that signals drift.</summary>
    private BatchReading ReadAnswer(
        JsonNode answer,
        IReadOnlyList<CorpusIngredientName> batch,
        IReadOnlyDictionary<string, string> familyKeys)
    {
        var response = answer.Deserialize<CatalogueGroupingResponse>(IngredientCatalogueFileStore.JsonOptions)
                       ?? throw new JsonException("the answer deserialised to nothing");

        if (response.Names is null)
            throw new JsonException("the answer is missing the names array");

        var accepted = new Dictionary<int, CatalogueNameResponse>();

        foreach (var item in response.Names)
        {
            if (item.Label is not { } label || label < 0 || label >= batch.Count)
            {
                logger.LogWarning("An answer for '{Name}' cites label [{Label}], which this batch " +
                                  "does not have. Ignored.", item.Name, item.Label);
                continue;
            }

            if (!IsFaithfulEcho(item.Name, label, batch))
            {
                logger.LogWarning("The answer for label [{Label}] names '{Echoed}', but [{Label}] is " +
                                  "'{Actual}'. The answer is out of step with the list; ignored.",
                                  label, item.Name, label, batch[label].Name);
                continue;
            }

            // First answer wins, because honouring two answers for one name could put it in two entries.
            if (!accepted.TryAdd(label, item))
                logger.LogWarning("Label [{Label}] ({Name}) was answered twice; the first answer is " +
                                  "kept.", label, batch[label].Name);
        }

        var components = Connect(accepted, batch)
            .Select(labels => (Labels: labels, Collapsed: SpansTooManyFamilies(labels, batch, familyKeys)))
            .ToList();

        return new BatchReading(
            accepted,
            components,
            Unanswered: batch.Count - accepted.Count,
            Collapsed:  components.Where(component => component.Collapsed).Sum(component => component.Labels.Count));
    }

    /// <summary>Whether a group spans more families than any real merge was measured to, which marks a collapsed answer rather than a judgement.</summary>
    private bool SpansTooManyFamilies(
        IReadOnlyList<int> labels,
        IReadOnlyList<CorpusIngredientName> batch,
        IReadOnlyDictionary<string, string> familyKeys) =>
        labels
            .Select(label => familyKeys.TryGetValue(batch[label].Name, out var key) ? key : batch[label].Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > _options.CatalogueMaxFamiliesPerGroup;

    /// <summary>Whether an echoed name is a faithful copy of its label's name, tolerating case, punctuation and bracketed asides but never another label's exact name.</summary>
    internal static bool IsFaithfulEcho(string? echoed, int label, IReadOnlyList<CorpusIngredientName> batch)
    {
        if (string.IsNullOrWhiteSpace(echoed)) return false;

        var actual = batch[label].Name;
        if (string.Equals(echoed.Trim(), actual, StringComparison.OrdinalIgnoreCase)) return true;

        for (var other = 0; other < batch.Count; other++)
            if (other != label && string.Equals(echoed.Trim(), batch[other].Name, StringComparison.OrdinalIgnoreCase))
                return false;

        var echoedWords = ComparableWords(echoed, keepAsides: true);

        return echoedWords.Length > 0
               && (BeginsWith(ComparableWords(actual, keepAsides: true), echoedWords)
                   || BeginsWith(ComparableWords(actual, keepAsides: false), echoedWords));
    }

    private static bool BeginsWith(string[] words, string[] leading) =>
        leading.Length <= words.Length
        && leading.SequenceEqual(words.Take(leading.Length), StringComparer.Ordinal);

    /// <summary>A name's lowercase words without punctuation, with bracketed asides dropped or kept as plain words.</summary>
    private static string[] ComparableWords(string name, bool keepAsides)
    {
        var words = new StringBuilder();
        var depth = 0;

        foreach (var character in name)
        {
            if (character == '(') { depth++; words.Append(' '); continue; }
            if (character == ')') { depth = Math.Max(0, depth - 1); words.Append(' '); continue; }
            if (depth > 0 && !keepAsides) continue;

            words.Append(char.IsLetterOrDigit(character) || character == '-'
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The groups a judged answer yields, dissolving any group still flagged as collapsed and refining the rest by <see cref="CatalogueMergeVocabulary"/>.</summary>
    private IReadOnlyList<CatalogueGroup> AssembleGroups(
        BatchReading reading, IReadOnlyList<CorpusIngredientName> batch, int batchNumber, int totalBatches) =>
        reading.Components
            .SelectMany(component => component.Collapsed
                ? component.Labels.Select(label => (IReadOnlyList<int>)[label])
                : RefineByMergeVocabulary(component.Labels, batch, batchNumber, totalBatches))
            .Where(labels => labels.Any(reading.Answers.ContainsKey))
            .Select(labels => ToGroup(labels, reading, batch))
            .ToList();

    /// <summary>Splits one model group into the parts whose names differ only by <see cref="CatalogueMergeVocabulary"/>.</summary>
    private IReadOnlyList<IReadOnlyList<int>> RefineByMergeVocabulary(
        IReadOnlyList<int> labels, IReadOnlyList<CorpusIngredientName> batch, int batchNumber, int totalBatches)
    {
        if (labels.Count == 1) return [labels];

        var parts = new List<List<int>>();

        foreach (var label in OrderMostUsedFirst(labels, member => batch[member]))
        {
            var home = parts.FirstOrDefault(part =>
                CatalogueMergeVocabulary.DifferOnlyByMergeVocabulary(batch[part[0]].Name, batch[label].Name));

            if (home is null) parts.Add([label]);
            else home.Add(label);
        }

        // Log every merge the model proposed and the vocabulary refused, for the review.
        if (parts.Count > 1)
            logger.LogInformation(
                "[{Index}/{Total}] kept apart: {Canonical} | {Others}",
                batchNumber, totalBatches, batch[parts[0][0]].Name,
                string.Join(" | ", parts.Skip(1).Select(part => batch[part[0]].Name)));

        return parts;
    }

    /// <summary>The connected components of <c>same_as</c> by union-find, so chains and cycles are harmless and answer order does not matter.</summary>
    private IReadOnlyList<IReadOnlyList<int>> Connect(
        IReadOnlyDictionary<int, CatalogueNameResponse> answers, IReadOnlyList<CorpusIngredientName> batch)
    {
        var parent = Enumerable.Range(0, batch.Count).ToArray();

        int Find(int label)
        {
            while (parent[label] != label)
            {
                parent[label] = parent[parent[label]];
                label = parent[label];
            }

            return label;
        }

        foreach (var (label, answer) in answers)
        {
            if (answer.SameAs is not { } target || target == label) continue;

            if (target < 0 || target >= batch.Count)
            {
                logger.LogWarning("'{Name}' is marked the same as label [{Target}], which this batch " +
                                  "does not have. It stands alone.", batch[label].Name, target);
                continue;
            }

            // The lower root wins, so the components are the same whichever order they were joined in.
            var (left, right) = (Find(label), Find(target));
            if (left != right) parent[Math.Max(left, right)] = Math.Min(left, right);
        }

        return Enumerable.Range(0, batch.Count)
            .GroupBy(Find)
            .Select(component => (IReadOnlyList<int>)component.ToList())
            .ToList();
    }

    private CatalogueGroup ToGroup(
        IReadOnlyList<int> labels, BatchReading reading, IReadOnlyList<CorpusIngredientName> batch)
    {
        // Canonical order, so the member the entry is named after is consulted first.
        var ordered = OrderMostUsedFirst(labels, label => batch[label]).ToList();

        var canonical = ordered[0];

        var displayName = reading.Answers.TryGetValue(canonical, out var canonicalAnswer) &&
                          !string.IsNullOrWhiteSpace(canonicalAnswer.DisplayName)
            ? canonicalAnswer.DisplayName.Trim()
            : null;

        return new CatalogueGroup(
            DisplayName: displayName,
            Category:    ResolveCategory(ordered, reading, batch),
            Members:     [.. ordered.Select(label => batch[label].Name)]);
    }

    /// <summary>The category of the most-used answered member when valid, else the next member's, else the keyword table's guess.</summary>
    private string ResolveCategory(
        IReadOnlyList<int> orderedLabels, BatchReading reading, IReadOnlyList<CorpusIngredientName> batch)
    {
        foreach (var label in orderedLabels)
        {
            if (!reading.Answers.TryGetValue(label, out var answer)) continue;

            var candidate = answer.Category?.Trim().ToUpperInvariant().Replace(' ', '_');
            if (candidate is not null && IngredientCategory.IsValid(candidate)) return candidate;

            logger.LogWarning("'{Name}' came back with category '{Proposed}', which is not one of ours.",
                              batch[label].Name, answer.Category);
        }

        var primary = batch[orderedLabels[0]].Name;
        var guess   = RecipeScrapeService.CategoriseIngredient(primary);

        logger.LogWarning("No member of '{Name}''s group has a usable category. Falling back to the " +
                          "keyword table's '{Guess}'.", primary, guess);

        return guess;
    }

    // ── Passes 3 and 4: derive, recover, order ────────────────────────────────

    /// <summary>Turns the model's groups into catalogue entries, deriving units and display names and recovering every name the grouping pass dropped.</summary>
    private IReadOnlyList<CatalogueBuildEntry> BuildEntries(
        IReadOnlyList<CatalogueGroup> groups,
        IReadOnlyList<CorpusIngredientName> corpusNames,
        out int recoveredNames)
    {
        var byName = corpusNames.ToDictionary(name => name.Name, StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<MergedGroup>();

        foreach (var group in groups)
        {
            // Batches partition the corpus, so a name in two groups is an upstream defect and the first group keeps it.
            var members = group.Members
                .Where(member => byName.ContainsKey(member))
                .Where(member => placed.Add(member))
                .Select(member => byName[member])
                .ToList();

            if (members.Count == 0) continue;

            merged.Add(new MergedGroup(
                ChooseCanonicalName(members), group.DisplayName, group.Category, members));
        }

        // Gap recovery. A name in no group becomes its own entry.
        recoveredNames = 0;
        foreach (var name in corpusNames)
        {
            if (placed.Contains(name.Name)) continue;

            recoveredNames++;
            merged.Add(new MergedGroup(
                name.Name,
                DisplayName: null,
                Category: RecipeScrapeService.CategoriseIngredient(name.Name),
                Members: [name]));
        }

        if (recoveredNames > 0)
            logger.LogWarning(
                "{Count} names were not placed in any group and became single-name entries. " +
                "They are reachable, but their category came from the keyword table rather than " +
                "the model — worth a look during review.", recoveredNames);

        var entries = merged
            .Select(ToEntry)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        return AttachRegionalSpellings(entries);
    }

    private sealed record MergedGroup(
        string Name, string? DisplayName, string Category, List<CorpusIngredientName> Members);

    /// <summary>The group's canonical name: its most-used member, ties going to the shorter name.</summary>
    private static string ChooseCanonicalName(IReadOnlyList<CorpusIngredientName> members) =>
        OrderMostUsedFirst(members, member => member).First().Name;

    /// <summary>The one ordering of names the builder uses: most rows first, then shorter, then ordinal, so output is total and diffs cleanly.</summary>
    private static IOrderedEnumerable<T> OrderMostUsedFirst<T>(
        IEnumerable<T> source, Func<T, CorpusIngredientName> nameOf) =>
        source
            .OrderByDescending(item => nameOf(item).Rows)
            .ThenBy(item => nameOf(item).Name.Length)
            .ThenBy(item => nameOf(item).Name, StringComparer.Ordinal);

    private CatalogueBuildEntry ToEntry(MergedGroup group)
    {
        // The default unit is the modal unit over the merged group's rows, null when no row is quantified.
        var unitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in group.Members)
            foreach (var (unit, count) in member.UnitCounts)
                unitCounts[unit] = unitCounts.GetValueOrDefault(unit) + count;

        // Ties break on the canonical unit order so the same corpus always yields the same artefact.
        var defaultUnit = unitCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => CanonicalUnitOrder(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        // Canonicalise before storing so a non-canonical spelling cannot reach the artefact.
        if (defaultUnit is not null && MeasurementUnit.TryCanonicalise(defaultUnit, out var canonical))
            defaultUnit = canonical;

        // The model's display name wins when supplied, because the corpus modal form is sometimes a leaked worked example.
        var displayName = group.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            var modal = group.Members
                .SelectMany(member => member.DisplayNameCounts)
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .OrderByDescending(candidate => candidate.Sum(pair => pair.Value))
                .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
                .Select(candidate => candidate.Key)
                .FirstOrDefault();

            displayName = TitleCase(modal ?? group.Name);
        }

        var aliases = group.Members
            .Select(member => member.Name)
            .Where(name => !string.Equals(name, group.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new CatalogueBuildEntry(
            Name:          group.Name,
            DisplayName:   displayName,
            Category:      group.Category,
            DefaultUnit:   defaultUnit,
            Aliases:       aliases,
            CorpusRows:    group.Members.Sum(member => member.Rows),

            // Union the recipe slugs rather than summing, because a recipe using two names from one group is still one recipe.
            CorpusRecipes: group.Members.SelectMany(member => member.RecipeSlugs)
                                        .Distinct(StringComparer.Ordinal)
                                        .Count());
    }

    /// <summary>Attaches the regional spellings of <see cref="RegionalIngredientSpellings"/>, skipping any that is itself a corpus name or whose target is absent.</summary>
    private IReadOnlyList<CatalogueBuildEntry> AttachRegionalSpellings(
        IReadOnlyList<CatalogueBuildEntry> entries)
    {
        var result      = new List<CatalogueBuildEntry>(entries);
        var reachable   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexByForm = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Names first, then aliases through TryAdd, so a name always wins.
        for (var index = 0; index < result.Count; index++)
        {
            reachable.Add(result[index].Name);
            indexByForm[result[index].Name] = index;
        }

        for (var index = 0; index < result.Count; index++)
            foreach (var alias in result[index].Aliases)
            {
                reachable.Add(alias);
                indexByForm.TryAdd(alias, index);
            }

        foreach (var (target, spellings) in RegionalIngredientSpellings.ByCorpusName)
        {
            if (!indexByForm.TryGetValue(target, out var entryIndex))
            {
                logger.LogDebug(
                    "Regional spellings for '{Target}' were not attached — this corpus has no such " +
                    "name.", target);
                continue;
            }

            var additions = spellings
                .Select(spelling => spelling.Trim().ToLowerInvariant())
                .Where(spelling => !reachable.Contains(spelling))
                .ToList();

            if (additions.Count == 0) continue;

            foreach (var addition in additions) reachable.Add(addition);

            var entry = result[entryIndex];
            result[entryIndex] = entry with
            {
                Aliases = [.. entry.Aliases.Concat(additions).OrderBy(a => a, StringComparer.Ordinal)],
            };

            logger.LogInformation("'{Entry}' also answers to {Spellings}.",
                entry.Name, string.Join(", ", additions));
        }

        return [.. result.OrderBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    /// <summary>Position of a unit in the canonical display order, or last if it is not storable.</summary>
    private static int CanonicalUnitOrder(string unit)
    {
        for (var index = 0; index < MeasurementUnit.All.Count; index++)
            if (string.Equals(MeasurementUnit.All[index], unit, StringComparison.Ordinal)) return index;

        return MeasurementUnit.All.Count;
    }

    /// <summary>Title case for a display name derived from a corpus form, which is often all-lowercase.</summary>
    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
}
