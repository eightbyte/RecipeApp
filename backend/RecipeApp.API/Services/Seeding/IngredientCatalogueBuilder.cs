using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stage 4.5 (Phase 9.3 §4.3): derives the committed ingredient catalogue from the normalised
/// corpus. Four passes, of which only the second uses the model.
///
/// <para><b>Why a catalogue is derived rather than generated.</b> The retired
/// <c>IngredientCatalogueSeeder</c> asked a model for "common household cooking ingredients", which
/// produced a list whose relationship to this corpus was coincidental by construction — 41
/// hand-written names matched 22.9% of corpus rows, and six of them named nothing in the library at
/// all (§2.2). The recipes are the best available evidence for what the catalogue should contain,
/// and using them turns Stage 5's most expensive step into a dictionary lookup.</para>
///
/// <para><b>What the model is and is not asked.</b> Grouping and category, because no mechanical
/// rule gets them right — folding <c>onions</c> into <c>onion</c> is safe, folding
/// <c>diced tomatoes</c> into <c>tomatoes</c> is wrong, and the keyword categoriser files peanut
/// butter under Dairy (§2.3, §2.4). Not the default unit: the corpus states that 8,882 times, and
/// replacing observations with a guess would be a downgrade (§2.5). Not the canonical name either:
/// that is the group's most-used member, for the same reason. Never the density — that is
/// curated, cited and human-reviewed in <see cref="Data.IngredientDensitySeeder"/>, and two sources
/// would be one too many.</para>
/// </summary>
public class IngredientCatalogueBuilder(
    SeedCacheStore cache,
    IngredientCatalogueFileStore catalogueStore,
    ILlmStructuredClient llm,
    IOptions<RecipeSeedingOptions> options,
    ILogger<IngredientCatalogueBuilder> logger)
{
    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>Verbatim source lines shown to the model per name (§4.3 pass 2).</summary>
    public const int SampleLinesPerName = 3;

    /// <summary>Corpus identifier recorded in the artefact's provenance block.</summary>
    private const string CorpusName = "myplate";

    // ── The grouping schema ───────────────────────────────────────────────────

    /// <summary>
    /// <b>One object per labelled name</b> — the shape Phase 9 Stage 4 proved at 100% — rather than
    /// a list of groups. Groups are assembled afterwards from <c>same_as</c>.
    ///
    /// <para><b>Why not ask for groups.</b> The first three real runs did, and retained about 40% of
    /// the names where §8 expects 70–78%; one batch put all 39 of its names in a single group (§12.1).
    /// A partition of forty names is one global, combinatorial judgement. <c>same_as</c> turns it
    /// into forty local ones, and makes the failure expensive to reach: collapsing a batch now means
    /// writing the same label forty times, not emitting one object.</para>
    ///
    /// <para><b>Property order is decode order.</b> <see cref="JsonSchemaGrammar"/> emits properties
    /// in the order they appear here, so the model copies <c>label</c> and <c>name</c> — putting the
    /// name it is judging immediately in front of it — before it writes <c>same_as</c>. The echoed
    /// name is also the misalignment check Stage 4 lacked: there, an answer that drifted by one label
    /// was only detectable when it ran off the end.</para>
    ///
    /// <para>Every property is <b>required</b>, deliberately. <see cref="JsonSchemaGrammar"/> omits
    /// non-required properties from the grammar entirely, so an optional property is not one the
    /// model may skip — it is one the model <i>cannot emit</i>. Phase 9.1 lost every ingredient
    /// description to exactly that, and Stage 4 lost recipes because <c>notes</c> was unreachable
    /// and preparation words went into <c>unit</c> instead.</para>
    ///
    /// <para><b><c>category</c> is an enum, generated from <see cref="IngredientCategory.All"/>.</b>
    /// As a free string, the first run's opening batches came back with <c>SPICES</c> and
    /// <c>CONDIMENT</c>: the prompt said "MUST be exactly one value from this list" and the grammar
    /// did not, so the grammar won and the keyword table — the thing this phase replaces — decided
    /// those names instead.</para>
    /// </summary>
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

    /// <summary>
    /// The worked example, held as data so the prompt renders it through the same code that renders
    /// a real batch, and a test can run its answer through the same reader a real answer goes
    /// through. An example the reader would not accept teaches a shape nothing downstream honours.
    ///
    /// <para><b>A family this corpus does not contain.</b> Plums appear only as <c>plum tomato</c>.
    /// The first version used the real tomato family, which handed the tomato batch its answer —
    /// exactly the menu Phase 9 §23.3 measured this model family choosing from instead of reading the
    /// evidence. An absent family teaches the rules without answering any batch's question.</para>
    ///
    /// <para><b>The labels are in the order the builder would produce.</b> Most-used first within
    /// the <c>plum</c> family, then <c>plum sauce</c>'s own family — so the example looks exactly like
    /// a real batch, and <c>same_as</c> is shown pointing both at the next name and further back.</para>
    ///
    /// <para><b>It splits more than it merges, and on purpose.</b> Seven names become four purchases
    /// across four aisles. Every §5 rule the corpus exercises most is here: plural, redundant
    /// qualifier and sugar variant merge; canned, dried and a compound product do not.</para>
    /// </summary>
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

    /// <summary>
    /// The §5 rules, each derived from a measured corpus case rather than from taste, phrased as the
    /// per-name <c>same_as</c> decision the schema asks for.
    ///
    /// <para><b>The tie-break is stated last and on purpose.</b> An over-split catalogue shows two
    /// lines in a shopping list, which a user can see and understand. An over-merged one silently
    /// sums a can of tomatoes into a punnet of fresh ones, which they cannot.</para>
    ///
    /// <para><b>No proposed grouping for the actual names appears here or in the user message.</b>
    /// Mechanical folding routes names into batches so a family is judged in one call, and is then
    /// thrown away. Phase 9 §23.3 measured what this prompt family does with a menu: given the ten
    /// fraction conversions it needed, the model stopped reading the source line and started
    /// choosing from the list, and the error class got nearly three times worse.</para>
    ///
    /// <para><b>The warning that the shared word is not evidence survives from the group-shaped
    /// prompt</b>, where it measurably helped — one batch went from 1 group to 19. It was not enough
    /// on its own, because the task shape was the larger cause (§12.1).</para>
    /// </summary>
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
            "butter is not dairy. Chicken broth is not meat. Egg noodles are not dairy. Spices, " +
            $"dried herbs, seasonings and extracts are {IngredientCategory.Condiments}.\n" +
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

    /// <summary>
    /// Runs all four passes and writes the artefact. Nothing is written unless every §4.7 rule
    /// holds — a catalogue that contradicts itself is worse than no catalogue, and every consumer
    /// downstream treats this file as authoritative.
    /// </summary>
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
        errors.AddRange(IngredientCatalogueValidator.Validate(entries));
        errors.AddRange(IngredientCatalogueValidator.ValidateAgainstCorpus(
            entries, [.. corpus.Names.Select(name => name.Name)], corpus.Rows));

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

        var file = new IngredientCatalogueFile
        {
            GeneratedAt = DateTime.UtcNow,
            Source      = new CatalogueSource(CorpusName, corpus.Recipes, corpus.Rows, corpus.Names.Count),
            Entries     = entries,
        };

        await catalogueStore.SaveAsync(file, ct);
        return result with { Path = catalogueStore.Path };
    }

    /// <summary>
    /// Runs the grouping pass on the named batches only (1-based, as the build log numbers them),
    /// logging every merge, and writes nothing — the prompt-iteration loop <c>--slug</c> gives
    /// Stage 4. Pass 1 still reads the whole corpus, because batch composition depends on it: a
    /// batch number only means the same names if every name is there to be batched.
    /// </summary>
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

    /// <summary>
    /// Walks <c>normalised/</c> and accumulates, per distinct name, everything the later passes are
    /// allowed to reason from. No model and no database.
    ///
    /// <para>Fails loudly rather than building a partial catalogue. The artefact must describe the
    /// corpus that will actually be persisted, and one silently missing recipe is a handful of names
    /// that Stage 5 then cannot resolve.</para>
    /// </summary>
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

            // The parsed file is a gitignored intermediate, so a fresh clone may hold a complete
            // normalised/ and no parsed/ at all. Its absence is therefore not an error — but where
            // it IS present and disagrees, the normalised output answers a question the parser no
            // longer asks, and a catalogue derived from it would describe a corpus that no longer
            // exists.
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

            // Most-used lines first, then ordinal, so the sample is stable across runs and a
            // regenerated artefact diffs cleanly. Identical lines collapse, so a name whose lines
            // genuinely vary — 'cranberries' is fresh in some and dried in others — shows that
            // variation rather than three copies of its commonest form.
            SampleLines = [.. _lines
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(SampleLinesPerName)
                .Select(pair => pair.Key)],
        };
    }

    // ── Pass 2: group and categorise (LLM) ────────────────────────────────────

    /// <summary>
    /// Batches names by family and asks the model, per name, which earlier name it is the same
    /// purchase as.
    ///
    /// <para>A failed batch is not fatal. Its names fall through to pass 3's gap recovery as
    /// single-name entries, which is an over-split catalogue rather than a lost one — and §5's
    /// tie-break already says an over-split catalogue is the safe direction to fail in.</para>
    /// </summary>
    /// <param name="onlyBatches">
    /// 1-based batch numbers to run, or null for all of them. A number past the last batch is
    /// reported rather than silently yielding an empty preview.
    /// </param>
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

            // The best answer seen for this batch, not the last one. A later attempt can throw, or
            // come back worse than the one before it, and discarding a good partial answer for a
            // failed retry would cost grouping this batch had already earned.
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

                    // One object per name is the prompt's central instruction, so an answer that
                    // leaves names out has not done the job. Measured on the first real run: one
                    // batch came back empty — valid JSON, no exception, and nine names silently
                    // demoted to the keyword categoriser this phase exists to stop relying on.
                    // A collapsed answer has not done it either, just less visibly.
                    if (reading.Defects == 0) break;

                    logger.LogInformation(
                        "[{Index}/{Total}] attempt {Attempt} left {Missed} of {Names} names " +
                        "unanswered and {Collapsed} in collapsed groups; retrying.",
                        index + 1, batches.Count, attempt, reading.Unanswered, batch.Count, reading.Collapsed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Recorded and retried, then skipped, exactly as Stages 3 and 4 isolate one bad
                    // recipe. Losing a batch costs grouping quality for those names, never the names
                    // themselves — they fall through to gap recovery as single-name entries, which
                    // is an over-split catalogue rather than a lost one.
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

            // Every merge, named. A count alone cannot tell a family-dense batch from an
            // over-merging model — batch 1 of the corpus is 37 names that honestly form about 20
            // purchases — and the merges are exactly what §8's review has to judge against §5.
            foreach (var merge in batchGroups.Where(group => group.Members.Count > 1))
                logger.LogInformation(
                    "[{Index}/{Total}] merged: {Canonical} ← {Absorbed} ({Category})",
                    index + 1, batches.Count, merge.Members[0],
                    string.Join(" | ", merge.Members.Skip(1)), merge.Category);

            logger.LogInformation(
                "[{Index}/{Total}] {Names} names → {Groups} groups ({Attempts} attempt(s)).",
                index + 1, batches.Count, batch.Count, batchGroups.Count, attemptsUsed);

            groups.AddRange(batchGroups);
        }

        return new GroupingOutcome(groups, calls, batches.Count, run, judged);
    }

    private sealed record GroupingOutcome(
        IReadOnlyList<CatalogueGroup> Groups, int Calls, int TotalBatches, int BatchesRun, int NamesJudged);

    /// <summary>
    /// One attempt's answer as the builder judged it.
    /// </summary>
    /// <param name="Answers">Accepted answers, keyed by label.</param>
    /// <param name="Components">
    /// The labels grouped into connected components of <c>same_as</c>, each flagged when it spans
    /// more families than <see cref="RecipeSeedingOptions.CatalogueMaxFamiliesPerGroup"/>.
    /// </param>
    /// <param name="Unanswered">Labels with no accepted answer.</param>
    /// <param name="Collapsed">Names inside a flagged component.</param>
    private sealed record BatchReading(
        IReadOnlyDictionary<int, CatalogueNameResponse> Answers,
        IReadOnlyList<(IReadOnlyList<int> Labels, bool Collapsed)> Components,
        int Unanswered,
        int Collapsed)
    {
        /// <summary>What a retry tries to reduce, and what picks the best attempt.</summary>
        public int Defects => Unanswered + Collapsed;
    }

    /// <summary>
    /// Names batched by <b>family</b> — their final word, with a plural folded onto its singular —
    /// so every member of a family is judged in the same call.
    ///
    /// <para>This is the whole reason batching is not arbitrary: <c>tomato</c>, <c>tomatoes</c>,
    /// <c>diced tomatoes</c>, <c>crushed tomatoes</c> and <c>low-sodium tomatoes</c> split across
    /// two calls <i>guarantee</i> an inconsistent answer, because neither call can see what the
    /// other decided. A family larger than the cap is split by frequency, most-frequent first, so
    /// the split falls between the rare members rather than through the common ones.</para>
    /// </summary>
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
            // A family that cannot fit in any batch is chunked on its own rather than dragging a
            // partly-filled batch along with it.
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

    /// <summary>
    /// Each name's family key: its final word, with a plural folded onto the singular <b>when the
    /// corpus itself uses that singular</b> as some name's final word.
    ///
    /// <para><b>Why the fold exists.</b> Keyed on the raw final word, <c>tomato</c> and
    /// <c>tomatoes</c> are different families. They sort next to each other, so they usually share
    /// a batch — but greedy packing can put the boundary between them, and then the rule with the
    /// most evidence behind it (§2.3: 86 singular/plural families, 2,234 rows) is one the model can
    /// never apply. Measured against the corpus before this was written: 8 families were split that
    /// way, <c>tomato</c>, <c>mushroom</c>, <c>bean</c> and <c>pea</c> among them. With the fold,
    /// 68 plurals join their singular and only <c>pepper</c> spans batches — because it is larger
    /// than the cap, which is chunked by design.</para>
    ///
    /// <para><b>Why the corpus decides, not a stemmer.</b> A candidate singular is used only if it
    /// already occurs, so <c>chilies</c> finds <c>chili</c> and <c>tomatoes</c> finds
    /// <c>tomato</c>, while <c>molasses</c> and <c>hummus</c> have no candidate to fall into. This
    /// is routing only: a wrong key costs one family a batch boundary, never an answer.</para>
    /// </summary>
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

    /// <summary>
    /// English plural endings, most specific first: <c>berries → berry</c>, <c>tomatoes → tomato</c>,
    /// <c>onions → onion</c>. Only candidates — <see cref="FamilyKeys"/> accepts one only when the
    /// corpus already uses it.
    /// </summary>
    private static IEnumerable<string> SingularCandidates(string word)
    {
        if (word.Length > "ies".Length && word.EndsWith("ies", StringComparison.Ordinal))
            yield return word[..^"ies".Length] + "y";

        if (word.Length > "es".Length && word.EndsWith("es", StringComparison.Ordinal))
            yield return word[..^"es".Length];

        if (word.Length > "s".Length && word.EndsWith('s'))
            yield return word[..^"s".Length];
    }

    /// <summary>
    /// The last whitespace-separated word of a name, which is the head noun in every shape the
    /// corpus uses — <c>diced tomatoes</c>, <c>low-sodium chicken broth</c>, <c>fresh spinach</c>.
    /// </summary>
    private static string FinalWord(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? name : words[^1];
    }

    /// <summary>
    /// One batch as the model sees it: a labelled list of names, each with its corpus frequency and
    /// its verbatim source lines.
    ///
    /// <para><b>Labels, because labels are what is asked back.</b> Stage 4 established that this
    /// model family will not reliably derive an index it was not handed — asked for 0-based
    /// positions while the list was numbered from 1, a single answer mixed both bases and only the
    /// overrun past the end was detectable. Here the labels count from zero, and both
    /// <c>label</c> and <c>same_as</c> are those same labels, copied.</para>
    ///
    /// <para><b>The source lines are evidence, not decoration.</b> <c>diced tomatoes</c> is only
    /// identifiable as canned from its line — all 32 read <c>1 can (14.5 ounces) … diced
    /// tomatoes</c> — and <c>cranberries</c> covers both <c>2 cups fresh or frozen cranberries</c>
    /// and <c>1 cup dried cranberries</c>, which are different purchases.</para>
    /// </summary>
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

    /// <summary>
    /// One labelled name and its evidence. Shared by <see cref="BuildUserContent"/> and the worked
    /// example, so the example is rendered exactly as a real batch is.
    /// </summary>
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

    /// <summary>
    /// Reads one batch's answer: which of the batch's labels it answered, discarding anything the
    /// labels cannot justify.
    ///
    /// <para><b>An object is accepted only when its echoed name is the name its label carries.</b>
    /// A mismatch means the answer has drifted out of step with the list — the silent off-by-one
    /// Stage 4 could only catch when it ran off the end — and a <c>same_as</c> from a drifted object
    /// would merge whatever name it happens to be sitting beside. Discarding it leaves that name
    /// unanswered, which the retry loop then sees.</para>
    /// </summary>
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

            // First answer wins. Two answers for one name are a contradiction the model cannot
            // resolve for us, and honouring both could put one corpus name in two entries — which
            // is precisely the ambiguous-alias corruption §4.7 rule 4 exists to stop.
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

    /// <summary>
    /// Whether a group reaches across more families than any real merge was measured to — the shape
    /// of a collapsed answer rather than of a judgement.
    ///
    /// <para><b>A signal, never a decision.</b> It does not say which names belong together; it only
    /// refuses to trust an answer that put <c>toothpicks</c> and <c>whipped topping</c> into
    /// <c>tomatoes</c>, which is what the first full per-name run did with one batch of 36 names.
    /// Mechanical folding still routes and never proposes (§4.3).</para>
    /// </summary>
    private bool SpansTooManyFamilies(
        IReadOnlyList<int> labels,
        IReadOnlyList<CorpusIngredientName> batch,
        IReadOnlyDictionary<string, string> familyKeys) =>
        labels
            .Select(label => familyKeys.TryGetValue(batch[label].Name, out var key) ? key : batch[label].Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > _options.CatalogueMaxFamiliesPerGroup;

    /// <summary>
    /// Whether an echoed name is a copy of the name its label carries — allowing for the tidying a
    /// model does when it copies — and not the name of a different label, which is what drift looks
    /// like.
    ///
    /// <para><b>Why not an exact comparison.</b> The corpus names are not clean: Stage 4 kept
    /// asides such as <c>paprika (optional)</c> and <c>green onions (scallions)</c> inside the name.
    /// Measured on the first run of this shape, the model copied those as <c>paprika</c> and
    /// <c>green onions</c>, an exact check called three faithful answers drift, and the batch spent
    /// two retries recovering names it had answered correctly the first time.</para>
    ///
    /// <para>So a copy matches when the two agree once case, punctuation and bracketed asides are
    /// set aside, or when the echo is the leading words of the name. Neither relaxation may admit a
    /// neighbour: an echo that is <i>exactly</i> some other label's name is refused outright.</para>
    /// </summary>
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

    /// <summary>
    /// A name's words, lowercased, with punctuation removed — and bracketed asides either dropped or
    /// kept as plain words, since a copy may do either.
    /// </summary>
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

    /// <summary>
    /// The groups a judged answer yields.
    ///
    /// <para><b>A name the model never answered for still joins a group another name pointed it
    /// into</b>, because that pointer is the model's judgement about it. A name nobody answered for
    /// and nobody pointed at is left out entirely, for pass 3's gap recovery.</para>
    ///
    /// <para><b>A group still flagged as collapsed after every retry is dissolved</b>: each answered
    /// name in it stands alone, keeping the category and display name the model gave it. That
    /// discards whatever real merges were inside, which is the over-split direction §5's tie-break
    /// already chose.</para>
    ///
    /// <para><b>Every other group is refined by <see cref="CatalogueMergeVocabulary"/></b>: a name
    /// stays with the group only if it differs from a kept name by §5's merge vocabulary alone.</para>
    /// </summary>
    private IReadOnlyList<CatalogueGroup> AssembleGroups(
        BatchReading reading, IReadOnlyList<CorpusIngredientName> batch, int batchNumber, int totalBatches) =>
        reading.Components
            .SelectMany(component => component.Collapsed
                ? component.Labels.Select(label => (IReadOnlyList<int>)[label])
                : RefineByMergeVocabulary(component.Labels, batch, batchNumber, totalBatches))
            .Where(labels => labels.Any(reading.Answers.ContainsKey))
            .Select(labels => ToGroup(labels, reading, batch))
            .ToList();

    /// <summary>
    /// Splits one of the model's groups into the parts whose names differ only by
    /// <see cref="CatalogueMergeVocabulary"/>. Each name joins the first part, in canonical order,
    /// whose leading name it matches; the match is an equality of cores, so this is a partition and
    /// its result does not depend on that order.
    /// </summary>
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

        // Logged so the review can see every merge the model proposed and the vocabulary refused —
        // the list a reviewer reads to decide whether a word belongs in the table.
        if (parts.Count > 1)
            logger.LogInformation(
                "[{Index}/{Total}] kept apart: {Canonical} | {Others}",
                batchNumber, totalBatches, batch[parts[0][0]].Name,
                string.Join(" | ", parts.Skip(1).Select(part => batch[part[0]].Name)));

        return parts;
    }

    /// <summary>
    /// The connected components of <c>same_as</c>, resolved by union-find — so a chain
    /// (<c>[2]→[1]→[0]</c>) and a cycle are both harmless, and the result does not depend on the
    /// order the answers arrived in. Every label is in exactly one component, answered or not.
    /// </summary>
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
        // In canonical order, so the member the entry will be named after (see ChooseCanonicalName)
        // is the one whose answer is consulted first.
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

    /// <summary>
    /// The category the most-used answered member was given, when it is one of ours; otherwise the
    /// next member's; otherwise the keyword table's guess for the most-used name.
    ///
    /// <para>The keyword table is a poor fallback by design — §2.4 measured it filing peanut butter
    /// under Dairy and chicken broth under Meat — but "the model answered with something that is not
    /// a category" leaves nothing better, and it beats defaulting everything to OTHER.</para>
    /// </summary>
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

    /// <summary>
    /// Turns the model's groups into catalogue entries, deriving the units and display names the
    /// model was never asked for, and folding in every name the grouping pass failed to place.
    /// </summary>
    /// <param name="recoveredNames">
    /// Names the model never returned, which became single-name entries. §4.7 rule 5 makes an
    /// unreachable name a build failure and a dropped name is a recipe Stage 5 cannot persist — so
    /// a gap is repaired rather than propagated, and counted so it can be reviewed.
    /// </param>
    private IReadOnlyList<IngredientCatalogueEntry> BuildEntries(
        IReadOnlyList<CatalogueGroup> groups,
        IReadOnlyList<CorpusIngredientName> corpusNames,
        out int recoveredNames)
    {
        var byName = corpusNames.ToDictionary(name => name.Name, StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<MergedGroup>();

        foreach (var group in groups)
        {
            // Batches partition the corpus, so a name can only reach two groups through a defect
            // upstream. The first group keeps it: one name in two entries is §4.7 rule 4's
            // ambiguous alias, and the validator would refuse the whole artefact over it.
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

    /// <summary>
    /// The group's canonical name: its most-used member, ties going to the shorter name.
    ///
    /// <para><b>Derived, never asked for</b>, for the same reason the default unit is. Only this pass
    /// sees the whole corpus, and a name the model proposed could be some other batch's corpus name —
    /// making one string both an entry's name here and an alias there, which resolves by row order
    /// rather than by intent (§4.7 rule 4). A member of the group's own is always safe to claim,
    /// because batches partition the corpus. And the most-used form is the corpus's own answer to
    /// what the thing is called.</para>
    /// </summary>
    private static string ChooseCanonicalName(IReadOnlyList<CorpusIngredientName> members) =>
        OrderMostUsedFirst(members, member => member).First().Name;

    /// <summary>
    /// The one ordering of names the builder uses: most rows first; a tie goes to the shorter name —
    /// <c>onion</c> before <c>onions</c>, <c>milk</c> before <c>skim milk</c>, the unqualified form
    /// being the likelier canonical one — and then ordinally, so the order is total and a regenerated
    /// artefact diffs cleanly.
    ///
    /// <para>Shared by batching, group assembly and the canonical-name choice on purpose. The name an
    /// entry is called by is then always the earliest label of its family in the batch, which is
    /// exactly where the prompt tells <c>same_as</c> to point.</para>
    /// </summary>
    private static IOrderedEnumerable<T> OrderMostUsedFirst<T>(
        IEnumerable<T> source, Func<T, CorpusIngredientName> nameOf) =>
        source
            .OrderByDescending(item => nameOf(item).Rows)
            .ThenBy(item => nameOf(item).Name.Length)
            .ThenBy(item => nameOf(item).Name, StringComparer.Ordinal);

    private IngredientCatalogueEntry ToEntry(MergedGroup group)
    {
        // Pass 3a — the default unit is the modal unit over the MERGED group's rows. Derived, never
        // asked for: the corpus states a unit 8,882 times, and 'onion' being 176 pcs against 131 cup
        // is a measured answer about how you buy an onion. Null when no row in the group is
        // quantified at all — Phase 9.1's population, which the column already allows.
        var unitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in group.Members)
            foreach (var (unit, count) in member.UnitCounts)
                unitCounts[unit] = unitCounts.GetValueOrDefault(unit) + count;

        // Ties break on the canonical unit order, so the same corpus always yields the same
        // artefact — a regenerated file must diff cleanly, and a tie resolved by dictionary
        // enumeration order would not.
        var defaultUnit = unitCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => CanonicalUnitOrder(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        // Canonicalised before storing, so a corpus that ever carried a non-canonical spelling
        // cannot put one in the artefact for MeasurementUnit.IsValid to reject at seed time.
        if (defaultUnit is not null && MeasurementUnit.TryCanonicalise(defaultUnit, out var canonical))
            defaultUnit = canonical;

        // Pass 3b — the display name. The model wins when it supplied one, because §2.7 found the
        // corpus modal form is sometimes a leaked worked example: 13 rows across 4 recipes carry
        // 'Diced Tomatoes' on things that are not tomatoes, and dirty-rice has it on all ten of its
        // rows including 'water' and 'black pepper'.
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

        return new IngredientCatalogueEntry(
            Name:          group.Name,
            DisplayName:   displayName,
            Category:      group.Category,
            DefaultUnit:   defaultUnit,
            Aliases:       aliases,
            CorpusRows:    group.Members.Sum(member => member.Rows),

            // Unioned, not summed. A recipe using two names from one group — 'tomatoes' beside
            // 'diced tomatoes' is exactly that shape — is still one recipe, and a provenance figure
            // a reviewer cannot trust is worse than none.
            CorpusRecipes: group.Members.SelectMany(member => member.RecipeSlugs)
                                        .Distinct(StringComparer.Ordinal)
                                        .Count());
    }

    /// <summary>
    /// Attaches the regional spellings of <see cref="RegionalIngredientSpellings"/>, under two rules
    /// that keep §4.7 rule 4 true.
    ///
    /// <para>A spelling that is <i>itself</i> a corpus name is skipped: the grouping pass has
    /// already judged it on evidence, and overriding that from a hardcoded table would both
    /// contradict the review and risk making one name an alias of two entries. A spelling whose
    /// target is not in this corpus is skipped too — there is nothing to attach it to.</para>
    ///
    /// <para><b>The target is found by name or by alias.</b> The table names the American corpus
    /// form (<c>beet</c>), but the entry is named after its most-used member, which may be the plural
    /// (<c>beets</c>) with the table's form as its alias. Looking the target up by name alone would
    /// skip <c>beetroot</c> silently.</para>
    /// </summary>
    private IReadOnlyList<IngredientCatalogueEntry> AttachRegionalSpellings(
        IReadOnlyList<IngredientCatalogueEntry> entries)
    {
        var result      = new List<IngredientCatalogueEntry>(entries);
        var reachable   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexByForm = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Names first, then aliases through TryAdd, so a name always wins — the same precedence
        // every lookup against the seeded catalogue uses.
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
