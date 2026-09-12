using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Enums;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stage 4 (Phase 9 §11), one recipe at a time: the grammar-constrained LLM pass that turns a
/// parsed page's ingredient lines into measured ingredients and links each step to the ingredients
/// it uses, then holds the result to the §11.3 validation gate.
///
/// <para><b>The schema is Phase 3's, verbatim.</b> <see cref="RecipeScrapeService.RecipeSchemaJson"/>
/// already describes exactly this shape and is already enforced through
/// <see cref="JsonSchemaGrammar"/>; a second, divergent schema would be a maintenance trap (§11).
/// Only the prompt differs, because the input here is clean structured text rather than stripped
/// page HTML.</para>
///
/// <para><b>What the model is allowed to change is deliberately narrow.</b> Stage 3 found that
/// every page publishes an ordered step list (§22.1), so the steps' wording, the name, the servings
/// and the description are taken from the parsed page and the model's versions are dropped on the
/// floor. It supplies ingredient measurements and step→ingredient linkage, and nothing else.</para>
///
/// <para>No database and no network: the input is <c>parsed/</c> and the output is
/// <c>normalised/</c>. Catalogue matching and persistence are Stage 5's.</para>
/// </summary>
public class SeedRecipeNormaliser(
    ILlmStructuredClient llm,
    IOptions<RecipeSeedingOptions> options,
    ILogger<SeedRecipeNormaliser> logger)
{
    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>
    /// Every unit the pipeline will accept, spelled out for the model: the storable set plus the
    /// customary spellings Stage A converts. Derived from the two authoritative tables rather than
    /// restated, so the vocabulary the prompt promises and the one the gate enforces cannot drift
    /// apart (CLAUDE.md).
    /// </summary>
    private static readonly string AllowedUnits = string.Join(", ",
        MeasurementUnit.AcceptedSpellings
            .Concat(MeasurementConverter.ConvertibleUnits)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(unit => unit, StringComparer.OrdinalIgnoreCase));

    // Do not add a fraction conversion table here. It was tried, against a real failure — 37.3% of
    // ingredient lines state a fraction and the surviving quantity errors were all fractions read as
    // an adjacent value. Handing the model the ten conversions it needed made the failure class
    // nearly three times worse, because it stopped reading the line and started choosing from the
    // list: 1/8 came back as 1, 1/2 as 0.125, and '1 teaspoon salt', which contains no fraction at
    // all, came back as 0.25. A list of plausible answers is a list of things to guess from.
    // See the Stage 4 results section of the phase 9 spec.

    /// <summary>
    /// Words the corpus writes where a unit would go, none of which measures anything. Two kinds,
    /// and Phase 9 already decided both go to <c>pcs</c> with the word kept in notes:
    /// <list type="bullet">
    /// <item><b>Sizes</b> — 356 <c>medium</c>, 122 <c>large</c>, 86 <c>small</c>, plus <c>whole</c>,
    /// <c>ripe</c> and <c>mini</c>. §11.1's own table: <c>"1 medium onion"</c> is <c>1 pcs</c>.</item>
    /// <item><b>Counted things</b> — 97 <c>clove</c>, 45 <c>slice</c>, 45 <c>package</c>,
    /// 23 <c>head</c>, 23 <c>dash</c>, 12 <c>spray</c>, and the rest. A clove is not a unit; three
    /// packages of relish is three things.</item>
    /// </list>
    ///
    /// <para>Applied here rather than asked for, because asking did not work: with the rule stated
    /// twice in the prompt and demonstrated in the worked example, a descriptor in the unit column
    /// was still <b>12 of the 23 failures</b> in a 53-recipe sample. This is a fixed rename of a
    /// closed set of words, not a guess — the amount is untouched, <c>SourceUnit</c> still records
    /// what the model said, and a word that <i>is</i> a measurement never reaches this set.</para>
    /// </summary>
    private static readonly HashSet<string> CountWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Sizes and conditions
            "medium", "large", "small", "whole", "ripe", "mini", "extra", "jumbo", "baby",
            // Things counted rather than measured
            "clove", "cloves", "slice", "slices", "head", "heads", "stalk", "stalks",
            "stick", "sticks", "package", "packages", "packet", "packets", "can", "cans",
            "container", "containers", "bunch", "bunches", "sprig", "sprigs", "ear", "ears",
            "leaf", "leaves", "piece", "pieces", "fillet", "fillets", "breast", "breasts",
            "loaf", "loaves", "bag", "bags", "box", "boxes", "jar", "jars", "bottle", "bottles",
            // Unsized measures. Not quantities of anything, but the line does count them, so the
            // count is what survives — "1 dash salt" is one thing of salt, not 1/8 of a teaspoon
            // this project would have invented.
            "dash", "dashes", "pinch", "pinches", "spray", "sprays", "splash", "splashes",
            "drop", "drops", "handful", "handfuls", "sprinkle", "sprinkles",
        };

    /// <summary>
    /// The rules the extraction schema cannot express. Most exist because of what this corpus
    /// measures rather than what recipes generally look like:
    /// <list type="bullet">
    /// <item>Container sizes — 329 lines read <c>1 can (14.5 ounces) …</c>, where the useful
    /// quantity is the parenthesised net weight, not the can.</item>
    /// <item>Counted items — 356 <c>medium</c>, 122 <c>large</c>, 97 garlic <c>clove(s)</c> and
    /// 45 <c>slice(s)</c>. None is a unit of measurement; all of them are <c>pcs</c>.</item>
    /// <item>Unquantified lines — 432 lines state no quantity at all, and the model must say so
    /// rather than invent one (Phase 9.1). The gate rejects it either way, but a rejected recipe
    /// is a lost recipe, so the prompt teaches the rule the gate enforces.</item>
    /// <item>Quantities that sit late in the line — 109 lines put theirs in the trailing note
    /// (<c>orange peel, dried (1 teaspoon, optional)</c>), so "starts with a number" is the wrong
    /// test and the rule is stated as "contains a number".</item>
    /// </list>
    ///
    /// <para><b>What is deliberately not here.</b> A rule for <c>divided</c> (76 lines across 65
    /// recipes, where the model halves the amount because the line says the cook will) and a table
    /// of fraction conversions. Both were written against measured failures and both were removed
    /// after measuring: together they took quantity rejections from 9 in 44 recipes to 14 in 45. The
    /// prompt is already long, and every rule added to it competes with the ones that work.</para>
    ///
    /// <para>The worked example is not decoration, and every line in it was chosen by a measured
    /// failure rather than by taste. A rules-only version of this prompt lost the first seven
    /// recipes outright, because a 7B model reliably wrote the size word into the unit —
    /// <c>large pcs</c>, <c>medium, chopped</c>, <c>cloves, minced</c> — which no amount of
    /// restating the rule in prose fixed. Then the example itself caused a failure: with a bare
    /// <c>salt</c> as its null case, the model learned <i>salt</i> was the exemption rather than
    /// <i>no number</i>, and started nulling <c>1 teaspoon salt</c>. That is why the null case here
    /// is cheese and salt appears with a quantity.</para>
    /// </summary>
    private static readonly string NormaliseSystemPrompt =
        "You are a recipe data extraction assistant. The user gives you one recipe's ingredient " +
        "lines and cooking steps, already separated. Each ingredient line carries a label in " +
        "square brackets — [0], [1], [2] and so on. Read them; do not rewrite them.\n" +
        "\n" +
        "Return exactly one ingredient object per labelled ingredient line, in the same order. " +
        "Never merge two lines, never split one line, never add a line and never drop one.\n" +
        "\n" +
        "name: the food itself, lowercase and singular, without the quantity, the container or the " +
        "preparation words.\n" +
        "\n" +
        $"unit: MUST be exactly one value from this list, or null: {AllowedUnits}.\n" +
        "Nothing may be added to it — no commas, no second word, no parentheses. A size word " +
        "('large', 'medium'), an item word ('clove', 'slice', 'head', 'can', 'dash') and a " +
        "preparation word ('chopped', 'minced', 'optional') are NEVER units; every one of them " +
        "goes in notes. When the line counts whole items instead of measuring them, the unit is " +
        "exactly 'pcs'.\n" +
        "\n" +
        "amount: the number the line states, as a decimal — '1 1/2' is 1.5, '3/4' is 0.75.\n" +
        "Keep the unit the line states; never convert one unit into another.\n" +
        "If the line gives a container size, use that size times the number of containers: " +
        "'2 cans (15 ounces each) black beans' is 30 ounces.\n" +
        "A line containing a number ALWAYS states a quantity, wherever on the line it appears and " +
        "whatever the steps say about it later. Emit null for amount and unit only when the line " +
        "has no number at all. Never invent a quantity, and never discard one.\n" +
        "A line that is a section heading rather than a food — 'For the Dressing', 'Topping' — " +
        "still gets its own object, with null amount and unit. Never drop a line.\n" +
        "\n" +
        "notes: preparation and descriptive detail — 'chopped', 'low-sodium', 'optional', " +
        "'canned', the size word, the item word. Null if the line gives none.\n" +
        "\n" +
        "Worked example. For the lines:\n" +
        "[0] 1 can (14.5 ounces) diced tomatoes, no salt added\n" +
        "[1] 2 cloves garlic, minced\n" +
        "[2] 1 medium onion (chopped)\n" +
        "[3] 1/4 teaspoon salt\n" +
        "[4] orange peel, dried (1 teaspoon, optional)\n" +
        "[5] shredded cheddar cheese\n" +
        "the ingredients are:\n" +
        """
        [{"name":"diced tomatoes","display_name":"Diced Tomatoes","amount":14.5,"unit":"ounces","notes":"canned, no salt added"},
        {"name":"garlic clove","display_name":"Garlic Clove","amount":2,"unit":"pcs","notes":"minced"},
        {"name":"onion","display_name":"Onion","amount":1,"unit":"pcs","notes":"medium, chopped"},
        {"name":"salt","display_name":"Salt","amount":0.25,"unit":"teaspoon","notes":null},
        {"name":"orange peel","display_name":"Orange Peel","amount":1,"unit":"teaspoon","notes":"dried, optional"},
        {"name":"cheddar cheese","display_name":"Cheddar Cheese","amount":null,"unit":null,"notes":"shredded"}]
        """ + "\n" +
        "\n" +
        "Steps: return exactly one object per numbered step, in the same order, with step_number " +
        "matching the number given and the instruction copied as written.\n" +
        "ingredient_indexes holds the bracket labels of the ingredients that step uses, copied " +
        "exactly as they appear. Do not count the lines and do not adjust the label. For the lines " +
        "above, 'Soften the onion and garlic, then add the tomatoes' is [0, 1, 2] — onion is [2] " +
        "because its line is labelled [2]. Use an empty list when a step uses no ingredient, as " +
        "'Wash hands with soap and water' does.\n" +
        "\n" +
        "Respond with a single JSON object conforming to the schema and nothing else.";

    /// <summary>Exposed so a test can assert the vocabulary comes from the real unit tables.</summary>
    internal static string SystemPrompt => NormaliseSystemPrompt;

    /// <summary>
    /// Runs the LLM pass for one parsed recipe and returns output the §11.3 gate has admitted.
    /// </summary>
    /// <param name="parsed">Stage 3 output for this slug.</param>
    /// <param name="parsedFingerprint">Hash of that output, recorded so a re-parse invalidates this one.</param>
    /// <exception cref="SeedNormaliseException">
    /// The recipe could not be normalised within <see cref="RecipeSeedingOptions.MaxLlmRetries"/>
    /// retries. Never partially applied — a suspect recipe is excluded, not persisted (§11.3).
    /// </exception>
    public async Task<NormalisedSeedRecipe> NormaliseAsync(
        ParsedSeedRecipe parsed, string parsedFingerprint, CancellationToken ct = default)
    {
        // Servings comes from the page, not the model, so no retry could change it. Checked once,
        // up front, rather than after burning inference on a recipe that cannot be stored.
        if (parsed.Servings <= 0)
            throw new SeedNormaliseException(SeedNormaliseFailure.NonPositiveServings,
                $"the parsed page yielded {parsed.Servings} servings");

        var schema      = JsonNode.Parse(RecipeScrapeService.RecipeSchemaJson)!;
        var userContent = BuildUserContent(parsed);
        var attemptCap  = 1 + Math.Max(0, _options.MaxLlmRetries);

        SeedNormaliseException? lastFailure = null;
        var attemptsUsed = 0;

        for (var attempt = 1; attempt <= attemptCap; attempt++)
        {
            attemptsUsed = attempt;

            JsonNode? answer = null;
            try
            {
                answer = await CompleteAsync(schema, userContent, ct);
                return Accept(parsed, parsedFingerprint, Read(answer), attempt);
            }
            catch (SeedNormaliseException ex)
            {
                lastFailure = ex;

                // The rejected answer, verbatim: §13 makes prompt iteration the intended loop, and
                // iterating on a rejection reason without the output that caused it is guesswork.
                logger.LogDebug(
                    "{Slug}: attempt {Attempt} of {Cap} rejected — {Reason}. Answer: {Answer}",
                    parsed.Slug, attempt, attemptCap, ex.Message,
                    answer?.ToJsonString() ?? "(none)");
            }
        }

        lastFailure!.Attempts = attemptsUsed;
        throw lastFailure;
    }

    // ── The model call ────────────────────────────────────────────────────────

    private async Task<JsonNode> CompleteAsync(
        JsonNode schema, string userContent, CancellationToken ct)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.LlmTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        JsonNode json;
        try
        {
            json = await llm.CompleteStructuredAsync(
                NormaliseSystemPrompt, userContent, schema, _options.MaxLlmOutputTokens, linked.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new SeedNormaliseException(SeedNormaliseFailure.LlmTimeout,
                $"no answer within {_options.LlmTimeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            // The run itself was cancelled — Ctrl+C, not a bad recipe. Let it unwind.
            throw;
        }
        catch (Exception ex)
        {
            // Includes an unloaded model and a decoding failure. Which of the two it is cannot be
            // told apart reliably here, so both are reported per recipe and the runner's
            // consecutive-failure guard catches the systemic case.
            throw new SeedNormaliseException(SeedNormaliseFailure.InvalidLlmOutput, ex.Message);
        }

        return json;
    }

    /// <summary>Reads one answer into the shape <c>RecipeSchemaJson</c> describes.</summary>
    private static RecipeScrapeService.ExtractedRecipe Read(JsonNode json)
    {
        RecipeScrapeService.ExtractedRecipe? extracted;
        try
        {
            extracted = json.Deserialize<RecipeScrapeService.ExtractedRecipe>(
                RecipeScrapeService.LlmJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SeedNormaliseException(SeedNormaliseFailure.InvalidLlmOutput, ex.Message);
        }

        // The grammar makes these arrays mandatory, so their absence means the answer did not come
        // from a grammar-constrained decode at all. Checked rather than assumed, because the
        // alternative is a NullReferenceException unwinding a nine-hour run.
        if (extracted is null || extracted.Ingredients is null || extracted.Steps is null)
            throw new SeedNormaliseException(SeedNormaliseFailure.InvalidLlmOutput,
                "the answer is missing the ingredients or steps array");

        return extracted;
    }

    /// <summary>
    /// The recipe as the model sees it. Labelled, because every structural rule in the prompt — one
    /// object per line, same order, matching step numbers — is stated against these labels.
    ///
    /// <para><b>The ingredient labels count from zero and the step labels count from one, because
    /// that is what each is asked for.</b> They used to both count from one, which meant the only
    /// 0-based number in the exchange was one the model had to work out for itself, and it did not:
    /// asked which ingredients a step used, a single answer mixed the two bases — <c>[0]</c> for the
    /// first ingredient but <c>[8]</c> and <c>[9]</c> for the eighth and ninth of nine. Only the
    /// overrun past the end was detectable; the rest was silently off by one. Stating the range in
    /// words did not help, because the conflict was between two things the model could both read.
    /// A label it can copy is not a rule it has to apply.</para>
    /// </summary>
    internal static string BuildUserContent(ParsedSeedRecipe parsed)
    {
        var content = new StringBuilder();

        content.Append("Recipe: ").AppendLine(parsed.Name);
        content.Append("Servings: ").Append(parsed.Servings).AppendLine();

        content.AppendLine()
               .Append("Ingredient lines — return exactly ").Append(parsed.Ingredients.Count)
               .AppendLine(" ingredients, one per labelled line, in this order:");

        for (var index = 0; index < parsed.Ingredients.Count; index++)
        {
            var line = parsed.Ingredients[index];
            content.Append('[').Append(index).Append("] ");

            // The whole published line, exactly as the gate will read it back. The model and the
            // gate must be looking at the same text, or one of them is judging a different recipe.
            content.AppendLine(line.FullText);
        }

        content.AppendLine()
               .Append("Steps — return exactly ").Append(parsed.Steps.Count)
               .AppendLine(" steps, one per step number, in this order:");

        for (var index = 0; index < parsed.Steps.Count; index++)
            content.Append(index + 1).Append(". ").AppendLine(parsed.Steps[index]);

        // Restated where the steps are, so the answer to "which ingredients does this step use" is
        // in view at the point the question is being answered.
        content.AppendLine()
               .Append("For each step, ingredient_indexes holds the labels in square brackets " +
                       "above, copied exactly. This recipe's labels run from [0] to [")
               .Append(parsed.Ingredients.Count - 1)
               .AppendLine("]. Use no other number.");

        return content.ToString();
    }

    // ── The validation gate (§11.3) ───────────────────────────────────────────

    /// <summary>
    /// Applies every §11.3 rule and, if they all hold, assembles the cached artefact. Validation
    /// and assembly are one pass because the gate's verdict already carries the measurement as it
    /// should be stored — canonicalising a second time risks disagreeing with the check that
    /// admitted the row (Phase 9.1).
    /// </summary>
    private NormalisedSeedRecipe Accept(
        ParsedSeedRecipe parsed,
        string parsedFingerprint,
        RecipeScrapeService.ExtractedRecipe extracted,
        int attempt)
    {
        var ingredients = BuildIngredients(parsed, extracted);
        var steps       = BuildSteps(parsed, extracted, ingredients.Count);

        return new NormalisedSeedRecipe
        {
            Slug              = parsed.Slug,
            SourceUrl         = parsed.SourceUrl,
            ParsedFingerprint = parsedFingerprint,
            NormalisedAt      = DateTime.UtcNow,
            Attempts          = attempt,

            Name              = parsed.Name,
            Description       = parsed.Description,
            Servings          = parsed.Servings,
            ImageUrl          = parsed.ImageUrl,
            Notes             = parsed.Notes,
            SourceCredit      = parsed.SourceCredit,

            Ingredients       = ingredients,
            Steps             = steps,
        };
    }

    /// <summary>
    /// Pairs each returned ingredient with the source line it came from and runs the quantity gate
    /// against that line.
    ///
    /// <para>Pairing is positional, which is why the count must match exactly rather than within
    /// §11.3's tolerance of two. That tolerance predates Phase 9.1: the gate now decides each row
    /// against what its own source line said, so a row whose source line is a guess would be judged
    /// against a guess. Exact pairing is the stricter reading and the one the gate needs.</para>
    /// </summary>
    private List<NormalisedSeedIngredient> BuildIngredients(
        ParsedSeedRecipe parsed, RecipeScrapeService.ExtractedRecipe extracted)
    {
        if (extracted.Ingredients.Count != parsed.Ingredients.Count)
            throw new SeedNormaliseException(SeedNormaliseFailure.IngredientCountMismatch,
                $"the page lists {parsed.Ingredients.Count} ingredient lines and the model " +
                $"returned {extracted.Ingredients.Count}");

        var rows = new List<NormalisedSeedIngredient>(extracted.Ingredients.Count);

        for (var index = 0; index < extracted.Ingredients.Count; index++)
        {
            var returned = extracted.Ingredients[index];

            // The whole published line, both spans — see ParsedIngredientLine.FullText. Reading the
            // item text alone would call 109 corpus lines unquantified whose amount lives only in
            // the note, and then reject the model for having read them correctly.
            var sourceText = parsed.Ingredients[index].FullText;

            if (string.IsNullOrWhiteSpace(returned.Name))
                throw new SeedNormaliseException(SeedNormaliseFailure.MissingIngredientName,
                    $"ingredient {index + 1} ('{sourceText}') came back with no name");

            // The raw amount this row will be built from: what the model read, then repaired against
            // the line itself where the line settles the question on its own. Both repairs below
            // take their value from the source text, never from the answer (Phase 9.1).
            var rawAmount = (decimal?)returned.Amount;
            var rawUnit   = returned.Unit;

            // Repair 1 — a count the model declined to read. "1 dash black pepper" resembles an
            // unquantified seasoning and the model reads it as one, but the line does state a count,
            // and Phase 9 already decided an unsized measure counts its things as pcs. The unit
            // follows from ResolveCountUnit below, which sees a line naming no real unit.
            if (rawAmount is null &&
                SeedQuantityGate.TryReadCountedQuantity(sourceText, CountWords) is { } counted)
            {
                logger.LogDebug(
                    "{Slug}: ingredient {Position} ('{Line}') — the model read no quantity; the " +
                    "line counts {Count}, taken as pcs.",
                    parsed.Slug, index + 1, sourceText, counted);

                rawAmount = counted;
                rawUnit   = null;
            }

            // Repair 2 — an amount the model left out of a line that states exactly one number.
            // Closing an inconsistency the next repair creates rather than a new licence: on such a
            // line the gate already *requires* the stored amount to equal the line's own number, so a
            // wrong one is corrected to it and only a null was treated differently. Filling it
            // produces the one value the gate would have accepted anyway.
            //
            // The class this was measured against is MyPlate's optional ingredient, which states its
            // quantity in a trailing note — "salt (optional, 1/4 teaspoon)" — and whose "optional"
            // leads the model to report no quantity at all. 146 lines on 119 recipes. Phase 9.1's rule
            // survives intact: the model still cannot opt itself out by returning null, because the
            // source decides either way. HasStatedQuantity gates it, so a genuinely unquantified line
            // and a line whose only number is a cut size both stay unquantified.
            if (rawAmount is null &&
                SeedQuantityGate.HasStatedQuantity(sourceText) &&
                SeedQuantityGate.TryReadSoleNumber(sourceText) is { } statedSole &&
                statedSole > 0m)
            {
                logger.LogInformation(
                    "{Slug}: ingredient {Position} ('{Line}') — the model read no quantity; the " +
                    "line states {Stated}. Using the line's own number.",
                    parsed.Slug, index + 1, sourceText, statedSole);

                rawAmount = statedSole;
            }

            // Repair 3 — a misread number on a line that states exactly one. The gate already
            // trusts this arithmetic enough to reject a recipe on it, so it is trusted to supply
            // the answer instead: the number is parsed off the source by the same reader, and
            // there is no ambiguity about which number is meant when the line states one. The
            // model keeps the unit, which it reads reliably. Lines stating several numbers —
            // container sizes, pack counts — are not checkable and are left untouched.
            //
            // Restricted to a positive amount on purpose. A zero or a negative is not a digit read
            // wrongly, it is an answer that is not a quantity at all, and NonPositiveQuantity
            // rejects it deliberately — repairing over that would hide a row the model failed on
            // rather than misread. Every one of the 15 contradictions measured on the corpus was a
            // positive number, so nothing observed is lost by being strict here.
            if (rawAmount > 0m &&
                SeedQuantityGate.CheckAgainstStatedNumber(sourceText, rawAmount) is { } mismatch &&
                SeedQuantityGate.TryReadSoleNumber(sourceText) is { } sole)
            {
                logger.LogInformation(
                    "{Slug}: ingredient {Position} ('{Line}') — {Rejection}: the model returned " +
                    "{Returned}, the line states {Stated}. Using the line's own number.",
                    parsed.Slug, index + 1, sourceText, mismatch, rawAmount, sole);

                rawAmount = sole;
            }

            // Repair 4 — the unit, on a line that states one number and names its unit immediately
            // after it. Correcting only the amount is not enough and makes this worse: rescuing a
            // recipe whose number was wrong can carry a wrong unit in with it, which is how
            // "1 tablespoon cinnamon" first came back as "1 cup" — sixteen times too much, positive,
            // storable, and in agreement with the line's only number. Measured, the model's unit
            // matches the line's on 1,702 of the 1,703 rows this rule applies to, so it corrects
            // almost nothing and what it does correct is wrong.
            if (SeedQuantityGate.TryReadSoleStatedUnit(sourceText) is { } lineUnit &&
                !IsSameMeasurement(ResolveCountUnit(rawUnit, sourceText), lineUnit))
            {
                logger.LogInformation(
                    "{Slug}: ingredient {Position} ('{Line}') — the model's unit was {Returned}, " +
                    "the line states {Stated}. Using the line's own unit.",
                    parsed.Slug, index + 1, sourceText, rawUnit ?? "(none)", lineUnit);

                rawUnit = lineUnit;
            }

            // Stage A: the model preserves the unit as stated (§11.1), so 'ounces' has to become
            // grams before the gate can ask whether the unit is storable. Skipped entirely when
            // there is no quantity — there is nothing to convert, and a unit measuring nothing is
            // as invented as an amount would be.
            decimal? statedAmount = null;
            string?  statedUnit   = rawUnit;

            if (rawAmount is { } resolvedAmount)
                (statedAmount, statedUnit) = MeasurementConverter.ToCanonical(
                    (double)resolvedAmount, ResolveCountUnit(rawUnit, sourceText) ?? string.Empty);

            var verdict = SeedQuantityGate.Check(sourceText, statedAmount, statedUnit);

            if (!verdict.IsAccepted)
                throw new SeedNormaliseException(SeedNormaliseFailure.QuantityRejected,
                    $"ingredient {index + 1} ('{sourceText}') → {verdict.Rejection} " +
                    $"(model said {Describe(returned.Amount, returned.Unit)})");

            rows.Add(new NormalisedSeedIngredient(
                Name:         returned.Name.Trim().ToLowerInvariant(),
                DisplayName:  string.IsNullOrWhiteSpace(returned.DisplayName)
                                  ? returned.Name.Trim()
                                  : returned.DisplayName.Trim(),
                Amount:       verdict.Amount,
                Unit:         verdict.Unit,
                // Provenance: the measurement this row was read from, before Stage A touched it.
                // Never summed and never consolidated — it exists so a conversion can be audited,
                // which is why it records the repaired number rather than a discarded misread: a
                // SourceAmount that does not convert to Amount audits nothing. The run log names
                // every repair, and SourceText keeps the line itself. An accepted row has a source
                // amount exactly when it has a stored one.
                SourceAmount: rawAmount is { } raw ? Math.Round(raw, 3) : null,
                SourceUnit:   rawAmount is null ? null : rawUnit?.Trim(),
                Notes:        string.IsNullOrWhiteSpace(returned.Notes) ? null : returned.Notes.Trim(),
                SourceText:   sourceText));
        }

        return rows;
    }

    /// <summary>
    /// Checks the step structure and rebuilds the steps from the parsed page, keeping only the
    /// model's ingredient linkage. Stage 3 already segmented the directions off the page's own
    /// <c>&lt;ol&gt;</c> (§22.1), so a paraphrase, a truncation or a leaked footnote cannot enter
    /// the library through here.
    /// </summary>
    private static List<NormalisedSeedStep> BuildSteps(
        ParsedSeedRecipe parsed, RecipeScrapeService.ExtractedRecipe extracted, int ingredientCount)
    {
        if (extracted.Steps.Count != parsed.Steps.Count)
            throw new SeedNormaliseException(SeedNormaliseFailure.StepCountMismatch,
                $"the page lists {parsed.Steps.Count} steps and the model returned " +
                $"{extracted.Steps.Count}");

        var ordered = extracted.Steps.OrderBy(step => step.StepNumber).ToList();

        for (var index = 0; index < ordered.Count; index++)
            if (ordered[index].StepNumber != index + 1)
                throw new SeedNormaliseException(SeedNormaliseFailure.StepNumbersNotContiguous,
                    $"step numbers were [{string.Join(", ", extracted.Steps.Select(s => s.StepNumber))}]");

        var steps = new List<NormalisedSeedStep>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var indexes = (ordered[index].IngredientIndexes ?? [])
                .Distinct().OrderBy(value => value).ToList();

            foreach (var ingredientIndex in indexes)
                if (ingredientIndex < 0 || ingredientIndex >= ingredientCount)
                    throw new SeedNormaliseException(SeedNormaliseFailure.IngredientIndexOutOfRange,
                        $"step {index + 1} references ingredient {ingredientIndex} of " +
                        $"{ingredientCount}");

            steps.Add(new NormalisedSeedStep(index + 1, parsed.Steps[index], indexes));
        }

        return steps;
    }

    /// <summary>
    /// Resolves what the model put in the unit column to something storable, where the answer is
    /// fixed rather than a judgement. Three cases, in order:
    ///
    /// <list type="number">
    /// <item><b>No unit, and the line names none either.</b> <c>1 granny smith apple</c>,
    /// <c>7 apples</c>, <c>16 lettuce leaves</c> — the line counts whole things, so the count is
    /// <c>pcs</c>. Decided from the source, never from the answer: <c>2 cups flour</c> names a unit,
    /// so the same bare number there is a dropped <c>cup</c> and stays a rejection.</item>
    /// <item><b>A leading real unit.</b> The model writes <c>"pound, chunks"</c> when it has
    /// nowhere else to put the preparation; the measurement is the leading phrase and Stage A
    /// converts it. Checked before the count words so <c>pound</c> never becomes <c>pcs</c>.</item>
    /// <item><b>A leading count word.</b> <c>"ripe, fresh"</c>, <c>"medium"</c>, <c>"cloves"</c>,
    /// <c>"dash"</c> → <c>pcs</c>.</item>
    /// </list>
    ///
    /// <para><b>The leading phrase is tried longest-first, not one word at a time.</b> Half the
    /// customary table is multi-word — <c>fl oz</c>, <c>fluid ounce</c>, <c>us fluid ounces</c> —
    /// and a single-word test reads <c>us</c> or <c>fluid</c> off those and matches nothing, so a
    /// correctly-read measurement fell through to the gate and was rejected as unstorable. Joining
    /// the words back together also normalises the model's own spacing and punctuation, so
    /// <c>"fl.  oz"</c> resolves too.</para>
    ///
    /// <para>Anything else is returned untouched, for Stage A and the gate to reject.</para>
    /// </summary>
    private static string? ResolveCountUnit(string? unit, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return SeedQuantityGate.StatesAUnitOfMeasurement(sourceText) ? unit : MeasurementUnit.Piece;

        var words = UnitWords.Matches(unit).Select(match => match.Value).ToList();
        if (words.Count == 0) return unit;

        for (var take = words.Count; take > 0; take--)
        {
            var phrase = string.Join(' ', words.Take(take));

            if (MeasurementUnit.TryCanonicalise(phrase, out _)) return phrase;
            if (MeasurementConverter.ConvertibleUnits.Contains(phrase, StringComparer.OrdinalIgnoreCase))
                return phrase;
        }

        return CountWords.Contains(words[0]) ? MeasurementUnit.Piece : unit;
    }

    /// <summary>Runs of letters in a value the model may have written as a punctuated phrase.</summary>
    private static readonly Regex UnitWords = new(@"[A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Whether two unit spellings measure the same thing, compared through Stage A so a difference in
    /// spelling is not mistaken for a difference in measurement. One unit of each is converted and
    /// both halves of the result are compared: <c>ounce</c> and <c>pound</c> both canonicalise to
    /// <c>g</c> and are emphatically not the same unit, so comparing the unit alone would miss it.
    /// </summary>
    private static bool IsSameMeasurement(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);

        return MeasurementConverter.ToCanonical(1d, left) == MeasurementConverter.ToCanonical(1d, right);
    }

    private static string Describe(double? amount, string? unit) =>
        amount is null ? "no quantity" : $"{amount} {unit ?? "(no unit)"}".Trim();
}
