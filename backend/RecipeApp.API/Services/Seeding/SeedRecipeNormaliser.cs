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
/// Stage 4, one recipe at a time: the grammar-constrained LLM pass that measures each ingredient line and links steps
/// to ingredients, then holds the result to the validation gate.
/// </summary>
public class SeedRecipeNormaliser(
    ILlmStructuredClient llm,
    IOptions<RecipeSeedingOptions> options,
    ILogger<SeedRecipeNormaliser> logger)
{
    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>Every unit the pipeline accepts, derived from the two authoritative tables so the prompt and the gate cannot drift apart.</summary>
    private static readonly string AllowedUnits = string.Join(", ",
        MeasurementUnit.AcceptedSpellings
            .Concat(MeasurementConverter.ConvertibleUnits)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(unit => unit, StringComparer.OrdinalIgnoreCase));

    // Do not add a fraction conversion table: handing the model the conversions made its quantity errors about three times worse.

    /// <summary>Words the corpus writes where a unit would go that measure nothing (sizes and counted things), mapped to <c>pcs</c> with the word kept in notes.</summary>
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
            // Unsized measures count their things, so "1 dash salt" is one thing of salt rather than an invented fraction of a teaspoon.
            "dash", "dashes", "pinch", "pinches", "spray", "sprays", "splash", "splashes",
            "drop", "drops", "handful", "handfuls", "sprinkle", "sprinkles",
        };

    /// <summary>The rules the extraction schema cannot express, kept short because every rule added competes with the ones that work.</summary>
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

    /// <summary>Runs the LLM pass for one parsed recipe and returns output the validation gate has admitted.</summary>
    /// <exception cref="SeedNormaliseException">The recipe could not be normalised within <see cref="RecipeSeedingOptions.MaxLlmRetries"/> retries.</exception>
    public async Task<NormalisedSeedRecipe> NormaliseAsync(
        ParsedSeedRecipe parsed, string parsedFingerprint, CancellationToken ct = default)
    {
        // Servings comes from the page, so check it once up front rather than after spending inference on an unstorable recipe.
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

                // Log the rejected answer verbatim, because iterating on a rejection reason without its output is guesswork.
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
            // Covers an unloaded model and a decoding failure alike; the runner's consecutive-failure guard catches the systemic case.
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

        // The grammar makes these arrays mandatory, so their absence means the answer was not grammar-constrained.
        if (extracted is null || extracted.Ingredients is null || extracted.Steps is null)
            throw new SeedNormaliseException(SeedNormaliseFailure.InvalidLlmOutput,
                "the answer is missing the ingredients or steps array");

        return extracted;
    }

    /// <summary>The recipe as the model sees it, with ingredient labels counting from zero and step labels from one because that is what each is asked for.</summary>
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

            // The whole published line, exactly as the gate will read it back.
            content.AppendLine(line.FullText);
        }

        content.AppendLine()
               .Append("Steps — return exactly ").Append(parsed.Steps.Count)
               .AppendLine(" steps, one per step number, in this order:");

        for (var index = 0; index < parsed.Steps.Count; index++)
            content.Append(index + 1).Append(". ").AppendLine(parsed.Steps[index]);

        // Restated beside the steps so the ingredients are in view when the question is answered.
        content.AppendLine()
               .Append("For each step, ingredient_indexes holds the labels in square brackets " +
                       "above, copied exactly. This recipe's labels run from [0] to [")
               .Append(parsed.Ingredients.Count - 1)
               .AppendLine("]. Use no other number.");

        return content.ToString();
    }

    // ── The validation gate ───────────────────────────────────────────────────

    /// <summary>Applies every gate rule and, if they all hold, assembles the cached artefact in one pass so the measurement is canonicalised only once.</summary>
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

    /// <summary>Pairs each returned ingredient with its source line by position, which is why the count must match exactly, and runs the quantity gate against that line.</summary>
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

            // Use the whole published line, since some amounts live only in the trailing note.
            var sourceText = parsed.Ingredients[index].FullText;

            if (string.IsNullOrWhiteSpace(returned.Name))
                throw new SeedNormaliseException(SeedNormaliseFailure.MissingIngredientName,
                    $"ingredient {index + 1} ('{sourceText}') came back with no name");

            // The raw amount this row is built from, repaired against the source line where the line settles the question on its own.
            var rawAmount = (decimal?)returned.Amount;
            var rawUnit   = returned.Unit;

            // Repair 1: a count the model declined to read, as in "1 dash black pepper", is recovered from the line.
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

            // Repair 2: fill an amount the model left out of a line that states exactly one number, since the source decides either way.
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

            // Repair 3: correct a misread positive number on a line that states exactly one, while zero and negatives stay rejected.
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

            // Repair 4: correct the unit when the line names one immediately after its single number, because a rescued amount can carry a wrong unit with it.
            if (SeedQuantityGate.TryReadSoleStatedUnit(sourceText) is { } lineUnit &&
                !IsSameMeasurement(ResolveCountUnit(rawUnit, sourceText), lineUnit))
            {
                logger.LogInformation(
                    "{Slug}: ingredient {Position} ('{Line}') — the model's unit was {Returned}, " +
                    "the line states {Stated}. Using the line's own unit.",
                    parsed.Slug, index + 1, sourceText, rawUnit ?? "(none)", lineUnit);

                rawUnit = lineUnit;
            }

            // Stage A: convert the unit as stated before the gate asks whether it is storable, and skip it when there is no quantity.
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
                // Provenance: the measurement this row was read from, after repairs but before Stage A, and never summed.
                SourceAmount: rawAmount is { } raw ? Math.Round(raw, 3) : null,
                SourceUnit:   rawAmount is null ? null : rawUnit?.Trim(),
                Notes:        string.IsNullOrWhiteSpace(returned.Notes) ? null : returned.Notes.Trim(),
                SourceText:   sourceText));
        }

        return rows;
    }

    /// <summary>Checks the step structure and rebuilds the steps from the parsed page, keeping only the model's ingredient linkage.</summary>
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
    /// Resolves the model's unit column to something storable where the answer is fixed rather than a judgement: a bare count,
    /// a leading real unit (tried longest-first), or a leading count word.
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

    /// <summary>Whether two unit spellings measure the same thing, compared after Stage A so both the unit and the converted amount are checked.</summary>
    private static bool IsSameMeasurement(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);

        return MeasurementConverter.ToCanonical(1d, left) == MeasurementConverter.ToCanonical(1d, right);
    }

    private static string Describe(double? amount, string? unit) =>
        amount is null ? "no quantity" : $"{amount} {unit ?? "(no unit)"}".Trim();
}
