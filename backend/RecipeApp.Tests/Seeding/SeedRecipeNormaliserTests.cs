using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 4 (Phase 9 §11) against a scripted model: the §11.3 validation gate, the retry loop, and
/// the narrow slice of the answer the model is actually allowed to keep. No GPU, no database, no
/// network — the whole point of <see cref="StubLlmStructuredClient"/> (§17.3).
/// </summary>
public class SeedRecipeNormaliserTests
{
    private RecipeSeedingOptions _options = new();

    private const string Fingerprint = "0f1e2d3c";

    private SeedRecipeNormaliser BuildNormaliser(StubLlmStructuredClient llm) =>
        new(llm, Options.Create(_options), NullLogger<SeedRecipeNormaliser>.Instance);

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static ParsedSeedRecipe ParsedRecipe(
        string[]? ingredientLines = null,
        string[]? steps = null,
        int servings = 8,
        string? ingredientNote = null) => new()
    {
        Slug         = "20-minute-chicken-creole",
        SourceUrl    = "https://www.myplate.gov/recipes/20-minute-chicken-creole",
        Template     = MyPlateTemplate.Primary,
        Name         = "20-Minute Chicken Creole",
        Description  = "A Creole-inspired dish.",
        Servings     = servings,
        ImageUrl     = "https://example.test/creole.jpg",
        SourceYield  = $"{servings} servings",
        Notes        = "* Store bought chili sauce can be high in sodium.",
        SourceCredit = "Recipe Adapted from: Food Hero",
        Ingredients  = [.. (ingredientLines ?? ["1 pound skinless chicken breasts", "salt"])
            .Select(line => new ParsedIngredientLine(line, ingredientNote))],
        Steps        = steps ?? ["Wash hands with soap and water.", "Cook the chicken."],
    };

    /// <summary>Builds one answer in the shape <c>RecipeSchemaJson</c> constrains the model to.</summary>
    private static JsonNode Answer(
        (string Name, double? Amount, string? Unit, string? Notes)[] ingredients,
        (int Number, string Instruction, int[] Indexes)[] steps,
        string name = "Whatever The Model Called It",
        int servings = 99) => new JsonObject
    {
        ["name"]        = name,
        ["description"] = "Whatever the model wrote.",
        ["servings"]    = servings,
        ["ingredients"] = new JsonArray([.. ingredients.Select(ingredient => (JsonNode)new JsonObject
        {
            ["name"]         = ingredient.Name,
            ["display_name"] = ingredient.Name,
            ["amount"]       = ingredient.Amount is { } amount ? JsonValue.Create(amount) : null,
            ["unit"]         = ingredient.Unit,
            ["notes"]        = ingredient.Notes,
        })]),
        ["steps"] = new JsonArray([.. steps.Select(step => (JsonNode)new JsonObject
        {
            ["step_number"]        = step.Number,
            ["instruction"]        = step.Instruction,
            ["ingredient_indexes"] = new JsonArray([.. step.Indexes.Select(i => (JsonNode)JsonValue.Create(i))]),
        })]),
    };

    /// <summary>The answer a well-behaved model gives for <see cref="ParsedRecipe"/>'s defaults.</summary>
    private static JsonNode GoodAnswer() => Answer(
        [("chicken breast", 1, "pound", "skinless"), ("salt", null, null, null)],
        [(1, "Wash hands with soap and water.", []), (2, "Cook the chicken.", [0])]);

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_ConvertsTheStatedUnitAndKeepsWhatTheModelReadAsProvenance()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        var chicken = recipe.Ingredients[0];
        chicken.Amount.Should().Be(453.592m, "Stage A turns a pound into grams");
        chicken.Unit.Should().Be("g");
        chicken.SourceAmount.Should().Be(1m);
        chicken.SourceUnit.Should().Be("pound");
        chicken.SourceText.Should().Be("1 pound skinless chicken breasts");
    }

    [Theory]
    [InlineData("teaspoons", 2, "tsp", 2)]
    [InlineData("Cups", 1.5, "cup", 1.5)]
    [InlineData("ounces", 14.5, "g", 411.068)]
    [InlineData("pcs", 2, "pcs", 2)]
    public async Task NormaliseAsync_AcceptsAnyUnitThatCanonicalisesOrConverts(
        string statedUnit, double statedAmount, string expectedUnit, decimal expectedAmount)
    {
        var llm = new StubLlmStructuredClient(Answer(
            [("diced tomatoes", statedAmount, statedUnit, null)],
            [(1, "Add the tomatoes.", [0])]));

        var parsed = ParsedRecipe(
            ingredientLines: ["1 can (14.5 ounces) diced tomatoes"],
            steps: ["Add the tomatoes."]);

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Unit.Should().Be(expectedUnit);
        recipe.Ingredients[0].Amount.Should().Be(expectedAmount);
    }

    /// <summary>
    /// Phase 9.1: a line stating no quantity stores null, never zero — <c>0</c> renders as
    /// <c>0 g Salt</c> and sums into shopping lists.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_StoresAnUnquantifiedLineWithNoMeasurementAtAll()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        var salt = recipe.Ingredients[1];
        salt.Amount.Should().BeNull();
        salt.Unit.Should().BeNull();
        salt.SourceAmount.Should().BeNull();
        salt.SourceUnit.Should().BeNull();
    }

    /// <summary>
    /// §22.1: the page already publishes an ordered step list, so the model's retelling of a step
    /// is discarded. A paraphrase, a truncation or a leaked footnote cannot enter through here.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_KeepsThePagesStepWordingRatherThanTheModels()
    {
        var llm = new StubLlmStructuredClient(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null)],
            [(1, "Wash your hands.", []), (2, "Fry it up.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        recipe.Steps.Select(step => step.Instruction).Should()
            .Equal("Wash hands with soap and water.", "Cook the chicken.");
    }

    [Fact]
    public async Task NormaliseAsync_TakesTheRecipesIdentityFromThePageNotFromTheModel()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(servings: 8), Fingerprint);

        recipe.Name.Should().Be("20-Minute Chicken Creole");
        recipe.Servings.Should().Be(8, "the model said 99");
        recipe.Description.Should().Be("A Creole-inspired dish.");
        recipe.SourceCredit.Should().Be("Recipe Adapted from: Food Hero");
        recipe.Notes.Should().StartWith("* Store bought chili sauce");
        recipe.ImageUrl.Should().Be("https://example.test/creole.jpg");
        recipe.SourceUrl.Should().Be("https://www.myplate.gov/recipes/20-minute-chicken-creole");
    }

    [Fact]
    public async Task NormaliseAsync_RecordsTheFingerprintAndAttemptCountForTheCache()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        recipe.ParsedFingerprint.Should().Be(Fingerprint);
        recipe.Attempts.Should().Be(1);
        recipe.Version.Should().Be(NormalisedSeedRecipe.CurrentVersion);
    }

    [Fact]
    public async Task NormaliseAsync_DeduplicatesAndOrdersEachStepsIngredientIndexes()
    {
        var llm = new StubLlmStructuredClient(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null)],
            [(1, "Wash hands.", []), (2, "Cook.", [1, 0, 1])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        recipe.Steps[1].IngredientIndexes.Should().Equal(0, 1);
    }

    // ── The validation gate (§11.3) ───────────────────────────────────────────

    private async Task<SeedNormaliseException> RejectionOf(
        JsonNode answer, ParsedSeedRecipe? parsed = null)
    {
        var llm = new StubLlmStructuredClient(answer);

        var act = async () => await BuildNormaliser(llm)
            .NormaliseAsync(parsed ?? ParsedRecipe(), Fingerprint);

        return (await act.Should().ThrowAsync<SeedNormaliseException>()).Which;
    }

    [Fact]
    public async Task NormaliseAsync_RejectsARowCountThatDoesNotMatchTheSourceLines()
    {
        // The model split "salt and pepper" — plausible, and exactly what makes positional pairing
        // unsafe, so the recipe is retried rather than paired against a guess.
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null),
             ("pepper", null, null, null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.IngredientCountMismatch);
        rejection.Detail.Should().Contain("2").And.Contain("3");
    }

    [Fact]
    public async Task NormaliseAsync_RejectsAStepCountThatDoesNotMatchThePage()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null)],
            [(1, "Wash hands, then cook.", [0])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.StepCountMismatch);
    }

    [Fact]
    public async Task NormaliseAsync_RejectsStepNumbersThatAreNotContiguousFromOne()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null)],
            [(1, "Wash hands.", []), (3, "Cook.", [0])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.StepNumbersNotContiguous);
    }

    [Fact]
    public async Task NormaliseAsync_RejectsAnIngredientIndexOutsideTheIngredientsArray()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, null, null)],
            [(1, "Wash hands.", []), (2, "Cook.", [2])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.IngredientIndexOutOfRange);
    }

    [Fact]
    public async Task NormaliseAsync_RejectsAnIngredientWithNoName()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("   ", null, null, null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.MissingIngredientName);
    }

    /// <summary>
    /// The half of the gate that has no other coverage (§14). An invented <c>1 tsp</c> for a line
    /// reading <c>salt</c> looks exactly like a correct extraction, which is why it is checked
    /// against the source line rather than against plausibility.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RejectsAQuantityInventedForAnUnquantifiedLine()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", 1, "teaspoon", null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        rejection.Failure.Should().Be(SeedNormaliseFailure.QuantityRejected);
        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.InventedQuantity))
            .And.Contain("salt");
    }

    [Fact]
    public async Task NormaliseAsync_RejectsAUnitAttachedToAnUnquantifiedLine()
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", 1, "pound", null), ("salt", null, "tsp", null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.InventedUnit));
    }

    /// <summary>
    /// A container line states several numbers, so nothing in the line settles which one is the
    /// quantity and it cannot be filled in. A model that reports none still loses the recipe.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RejectsAQuantifiedLineTheModelReportedAsHavingNoQuantity()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 can (14.5 ounces) diced tomatoes"], steps: ["Add the tomatoes."]);

        var rejection = await RejectionOf(
            Answer([("diced tomatoes", null, null, null)], [(1, "Add the tomatoes.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.MissingQuantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NormaliseAsync_RejectsANonPositiveAmountOnAQuantifiedLine(double amount)
    {
        var rejection = await RejectionOf(Answer(
            [("chicken breast", amount, "pound", null), ("salt", null, null, null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.NonPositiveQuantity));
    }

    /// <summary>
    /// A unit that is neither a measurement nor a counted thing cannot be stored. Checked after
    /// canonicalisation and after the count-word rename, so <c>teaspoons</c> and <c>cloves</c> both
    /// pass while a word the pipeline has no reading for does not.
    /// </summary>
    [Theory]
    [InlineData("knob")]
    [InlineData("glug")]
    [InlineData("sprinkling")]
    [InlineData("to taste")]
    public async Task NormaliseAsync_RejectsAUnitThatIsNotStorable(string unit)
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["2 cloves garlic"], steps: ["Mince the garlic."]);

        var rejection = await RejectionOf(
            Answer([("garlic", 2, unit, null)], [(1, "Mince the garlic.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.UnstorableUnit));
    }

    /// <summary>
    /// The misread that every other rule admits. Observed live: the model read
    /// <c>3/4 cup unsalted dry roasted peanuts</c> as <c>3.75 cup</c> — positive, storable, and five
    /// times too much.
    ///
    /// <para>The line settles this on its own, so the line answers it. The gate already trusts this
    /// arithmetic enough to reject a whole recipe on it, which is the same thing as trusting it to
    /// supply the number; rejecting instead threw away 15 recipes over a digit the source states
    /// unambiguously. The model keeps the unit, which it reads reliably.</para>
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_CorrectsAnAmountThatContradictsTheOneNumberTheLineStates()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["3/4 cup unsalted dry roasted peanuts"], steps: ["Scatter the peanuts."]);
        var llm = new StubLlmStructuredClient(
            Answer([("peanuts", 3.75, "cup", null)], [(1, "Scatter the peanuts.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(0.75m, "the line states three quarters");
        recipe.Ingredients[0].Unit.Should().Be("cup");
        recipe.Ingredients[0].SourceAmount.Should().Be(0.75m,
            "provenance must convert to the stored amount, or it audits nothing");
    }

    /// <summary>
    /// Every quantity misread measured on the corpus was a plausible positive number, and a zero is
    /// not one of those — it is an answer that is not a quantity. The substitution deliberately
    /// stops short of it so a row the model failed on outright is still excluded.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task NormaliseAsync_DoesNotCorrectANonPositiveAmountIntoTheLinesNumber(double amount)
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["3/4 cup unsalted dry roasted peanuts"], steps: ["Scatter the peanuts."]);

        var rejection = await RejectionOf(
            Answer([("peanuts", amount, "cup", null)], [(1, "Scatter the peanuts.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.NonPositiveQuantity));
    }

    /// <summary>
    /// A line stating several numbers does not settle which one is the quantity, so the substitution
    /// has nothing to substitute and must leave a container line alone.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_LeavesAnAmountAloneOnALineStatingSeveralNumbers()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["2 cans (15 ounces each) black beans"], steps: ["Drain the beans."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("black beans", 30, "ounces", "canned")], [(1, "Drain the beans.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].SourceAmount.Should().Be(30m, "neither 2 nor 15 is the quantity");
    }

    /// <summary>
    /// A container line's numbers disagree by design — one can, 14.5 ounces — so the arithmetic
    /// check stands aside rather than rejecting the right answer.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_DoesNotContradictAContainerQuantityResolvedToItsNetWeight()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 can (14.5 ounces) diced tomatoes"], steps: ["Add the tomatoes."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("diced tomatoes", 14.5, "ounces", "canned")], [(1, "Add the tomatoes.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(411.068m);
        recipe.Ingredients[0].SourceAmount.Should().Be(14.5m);
    }

    /// <summary>
    /// §11.1: a descriptive size is not a unit, and <c>"1 medium onion"</c> is <c>1 pcs</c> with the
    /// size in notes. Applied deterministically because the model's compliance was the largest
    /// single source of rejections in a 37-recipe sample.
    /// </summary>
    [Theory]
    [InlineData("medium")]
    [InlineData("Large")]
    [InlineData("small")]
    [InlineData("whole")]
    [InlineData("ripe")]
    [InlineData("cloves")]
    [InlineData("slices")]
    [InlineData("dash")]
    [InlineData("packages")]
    public async Task NormaliseAsync_ReadsADescriptiveSizeStandingInForAUnitAsPieces(string stated)
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["2 medium apples, pared, cored, sliced"], steps: ["Slice the apples."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("apple", 2, stated, "pared, cored, sliced")], [(1, "Slice the apples.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(2m);
        recipe.Ingredients[0].Unit.Should().Be("pcs");
        recipe.Ingredients[0].SourceUnit.Should().Be(stated, "provenance records what the model said");
    }

    /// <summary>
    /// A bare count on a line that names no unit is pieces — <c>7 apples</c> counts whole things.
    /// Decided from the source line, not from the model omitting a field.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_ReadsABareCountAsPiecesWhenTheLineNamesNoUnit()
    {
        var parsed = ParsedRecipe(ingredientLines: ["7 apples"], steps: ["Peel the apples."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("apple", 7, null, null)], [(1, "Peel the apples.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(7m);
        recipe.Ingredients[0].Unit.Should().Be("pcs");
    }

    /// <summary>
    /// The mirror case, and the reason the rule reads the line rather than the answer. A container
    /// line states several numbers, so the line does not settle which unit belongs to the quantity
    /// and a dropped unit cannot be recovered from it — which keeps this a rejection.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RejectsABareCountWhenTheLineNamesAUnitItCannotRecover()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 can (14.5 ounces) diced tomatoes"], steps: ["Add the tomatoes."]);

        var rejection = await RejectionOf(
            Answer([("diced tomatoes", 14.5, null, null)], [(1, "Add the tomatoes.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.UnstorableUnit));
    }

    /// <summary>
    /// The model writes the preparation into the unit when it has nowhere else to put it. The
    /// measurement is the leading phrase, and a real unit is resolved before any count word. Each
    /// case pairs with a source line that states the same unit, so what is under test is the phrase
    /// parsing rather than the line overriding it.
    /// </summary>
    [Theory]
    [InlineData("1 pound lean pork, cut into chunks", "pound, chunks",  "g",   453.592)]
    [InlineData("1 cup flour, sifted",                "cups, chopped",  "cup", 1)]
    [InlineData("1 ripe banana, mashed",              "ripe, fresh",    "pcs", 1)]
    [InlineData("1 clove garlic, minced",             "cloves, minced", "pcs", 1)]
    public async Task NormaliseAsync_ReadsTheLeadingPhraseOfAUnitTheModelWroteAsAPhrase(
        string line, string stated, string expectedUnit, decimal expectedAmount)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Cook it."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("pork", 1, stated, null)], [(1, "Cook it.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Unit.Should().Be(expectedUnit);
        recipe.Ingredients[0].Amount.Should().Be(expectedAmount);
    }

    /// <summary>
    /// Correcting the amount alone is not enough, and rescuing a recipe can carry a wrong unit in
    /// with it. Observed live the first time the substitution ran: <c>1 tablespoon cinnamon</c> came
    /// back as <c>1 cup</c> — sixteen times too much, positive, storable, and in agreement with the
    /// line's only number, so every other rule in the gate admitted it.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_CorrectsAUnitThatContradictsTheOneTheLineNames()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 tablespoon cinnamon"], steps: ["Stir in the cinnamon."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("cinnamon", 1, "cup", null)], [(1, "Stir in the cinnamon.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Unit.Should().Be("tbsp");
        recipe.Ingredients[0].Amount.Should().Be(1m);
    }

    /// <summary>
    /// The same rule recovers a unit the model dropped entirely, where the line names one after its
    /// only number. This was a rejection, which threw away a recipe over a unit the line states
    /// plainly.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_RecoversAUnitTheModelDroppedFromALineThatNamesOne()
    {
        var parsed = ParsedRecipe(ingredientLines: ["2 cups flour"], steps: ["Add the flour."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("flour", 2, null, null)], [(1, "Add the flour.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(2m);
        recipe.Ingredients[0].Unit.Should().Be("cup");
    }

    /// <summary>
    /// MyPlate states an optional ingredient's quantity in a trailing note, and the word
    /// <c>optional</c> leads the model to report no quantity at all. 146 lines on 119 recipes. On a
    /// line stating exactly one number the gate already requires the stored amount to equal it, so
    /// filling the null produces the one value the gate would have accepted — and rejecting instead
    /// threw the recipe away. The unit comes off the line by the same rule.
    /// </summary>
    [Theory]
    [InlineData("salt (optional, 1/4 teaspoon)", 0.25, "tsp")]
    [InlineData("powdered sugar, optional (1/3 cup, for glaze)", 0.333, "cup")]
    [InlineData("orange peel, dried (1 teaspoon, optional)", 1, "tsp")]
    [InlineData("nuts (1 cup, optional)", 1, "cup")]
    public async Task NormaliseAsync_FillsAnAmountTheModelLeftOutOfASoleNumberLine(
        string line, decimal expectedAmount, string expectedUnit)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Add it if you like."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("the food", null, null, "optional")], [(1, "Add it if you like.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(expectedAmount);
        recipe.Ingredients[0].Unit.Should().Be(expectedUnit);
    }

    /// <summary>
    /// A bare count gets the same treatment, and the unit still comes from the line naming none.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_FillsABareCountTheModelLeftOutAsPieces()
    {
        var parsed = ParsedRecipe(ingredientLines: ["7 apples"], steps: ["Peel the apples."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("apple", null, null, null)], [(1, "Peel the apples.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(7m);
        recipe.Ingredients[0].Unit.Should().Be("pcs");
    }

    /// <summary>
    /// The limits of the fill. A line stating no quantity, or whose only number is a cut size, stays
    /// unquantified (Phase 9.1); a line stating several numbers does not settle which one is meant.
    /// </summary>
    [Theory]
    [InlineData("nonstick cooking spray")]
    [InlineData("salt and pepper, to taste")]
    [InlineData("carrot, sliced into 3 inch pieces")]
    public async Task NormaliseAsync_DoesNotFillAnAmountForALineStatingNoQuantity(string line)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Prepare it."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("the food", null, null, null)], [(1, "Prepare it.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().BeNull();
        recipe.Ingredients[0].Unit.Should().BeNull();
    }

    /// <summary>
    /// The guard that keeps the unit rule off a parenthetical equivalence. 37 corpus rows name a unit
    /// only inside one — <c>12 large egg whites (about 1 1/2 cups)</c> is 12 pieces, not cups — and
    /// every one states more than one number, which is what stands the rule down.
    /// </summary>
    [Theory]
    [InlineData("12 large egg whites (about 1 1/2 cups)")]
    [InlineData("1 medium head of lettuce (about 10 cups)")]
    [InlineData("2 large potatoes (about 2 pounds)")]
    public async Task NormaliseAsync_DoesNotTakeAUnitFromAParentheticalEquivalence(string line)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Prepare it."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("egg white", 12, "pcs", "large")], [(1, "Prepare it.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Unit.Should().Be("pcs", "the count is of whole things, not of cups");
    }

    /// <summary>
    /// Half the customary table is multi-word, and a single-word test reads <c>us</c> or
    /// <c>fluid</c> off these and matches nothing. Observed live on
    /// <c>10 3/4 us fluid ounces 1% low fat milk</c>: the model read the measurement correctly and
    /// the recipe was lost to an unstorable unit. 15 lines on 14 recipes spell fluid ounces out.
    /// </summary>
    [Theory]
    [InlineData("fluid ounces")]
    [InlineData("us fluid ounces")]
    [InlineData("fluid ounce")]
    [InlineData("fl oz")]
    [InlineData("FLUID OUNCES")]
    public async Task NormaliseAsync_ResolvesAMultiWordCustomaryUnit(string stated)
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["10 3/4 us fluid ounces 1% low fat milk"], steps: ["Warm the milk."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("low fat milk", 10.75, stated, null)], [(1, "Warm the milk.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Unit.Should().Be("ml");
        recipe.Ingredients[0].Amount.Should().Be(317.915m);
    }

    // ── Recovering a count the model declined to read ─────────────────────────

    /// <summary>
    /// <c>1 dash black pepper</c> resembles an unquantified seasoning and the model reads it as one,
    /// but the line states a count and Phase 9 already decided an unsized measure counts its things
    /// as <c>pcs</c>. That decision was unreachable: the count-word rename only ever ran on an answer
    /// that already had an amount, so a null lost the recipe. 44 lines on 43 recipes are this shape.
    /// </summary>
    [Theory]
    [InlineData("1 dash black pepper", 1)]
    [InlineData("1 dash salt and black pepper", 1)]
    [InlineData("2 sprays of nonstick cooking spray", 2)]
    [InlineData("4 sprays of cooking spray", 4)]
    [InlineData("3 pinches of cayenne", 3)]
    public async Task NormaliseAsync_RecoversACountTheModelReadAsUnquantified(
        string line, decimal expected)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Season to finish."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("black pepper", null, null, null)], [(1, "Season to finish.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(expected);
        recipe.Ingredients[0].Unit.Should().Be("pcs");
    }

    /// <summary>A range yields its low end: under-buying corn shucks is the recoverable direction.</summary>
    [Fact]
    public async Task NormaliseAsync_RecoversTheLowEndOfACountedRange()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["4-6 handfuls corn shucks"], steps: ["Soak the shucks."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("corn shuck", null, null, null)], [(1, "Soak the shucks.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(4m);
        recipe.Ingredients[0].Unit.Should().Be("pcs");
    }

    /// <summary>
    /// The guard that keeps the recovery from inventing anything. A line naming a real unit is
    /// measuring, not counting, so its amount belongs to that unit and a missing one stays a
    /// rejection rather than being read off a later <c>slices</c>.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_DoesNotRecoverACountFromALineThatNamesARealUnit()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1/2 cup onion, chopped into 4 slices"], steps: ["Prepare it."]);

        var rejection = await RejectionOf(
            Answer([("onion", null, null, null)], [(1, "Prepare it.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.MissingQuantity),
            "four slices is not the quantity of half a cup of onion");
    }

    /// <summary>
    /// The same guard from the other side. A line naming a real unit is measured in that unit, so
    /// filling its amount must not also turn it into a count of pieces.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_FillsALineNamingARealUnitInThatUnitRatherThanAsPieces()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 pound skinless chicken breasts"], steps: ["Cook the chicken."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("chicken breast", null, null, "skinless")], [(1, "Cook the chicken.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(453.592m);
        recipe.Ingredients[0].Unit.Should().Be("g");
    }

    /// <summary>A line that genuinely states no quantity must stay unquantified (Phase 9.1).</summary>
    [Theory]
    [InlineData("nonstick cooking spray")]
    [InlineData("salt and pepper, to taste")]
    public async Task NormaliseAsync_DoesNotRecoverACountFromAnUnquantifiedLine(string line)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Prepare it."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("cooking spray", null, null, null)], [(1, "Prepare it.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().BeNull();
        recipe.Ingredients[0].Unit.Should().BeNull();
    }

    /// <summary>
    /// A cut size is not a quantity. Two corpus lines state nothing but one, and counting it made
    /// the gate demand an amount the line never gave.
    /// </summary>
    [Theory]
    [InlineData("carrot, sliced into 3 inch pieces")]
    [InlineData("aluminum foil (10x12 inches square)")]
    public async Task NormaliseAsync_TreatsALineWhoseOnlyNumberIsACutSizeAsUnquantified(string line)
    {
        var parsed = ParsedRecipe(ingredientLines: [line], steps: ["Prepare it."]);
        var llm = new StubLlmStructuredClient(Answer(
            [("carrot", null, null, "sliced")], [(1, "Prepare it.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().BeNull();
        recipe.Ingredients[0].Unit.Should().BeNull();
    }

    /// <summary>No retry can change what the page said, so this fails before any inference runs.</summary>
    [Fact]
    public async Task NormaliseAsync_RejectsANonPositiveServingCountWithoutCallingTheModel()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var act = async () => await BuildNormaliser(llm)
            .NormaliseAsync(ParsedRecipe(servings: 0), Fingerprint);

        (await act.Should().ThrowAsync<SeedNormaliseException>())
            .Which.Failure.Should().Be(SeedNormaliseFailure.NonPositiveServings);
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_RejectsAnAnswerMissingTheArraysTheGrammarMakesMandatory()
    {
        var rejection = await RejectionOf(new JsonObject { ["name"] = "Creole", ["servings"] = 4 });

        rejection.Failure.Should().Be(SeedNormaliseFailure.InvalidLlmOutput);
    }

    [Fact]
    public async Task NormaliseAsync_ReportsAModelThatThrowsAsThisRecipesFailure()
    {
        var llm = StubLlmStructuredClient.AlwaysFailing("Local LLM model is not loaded.");

        var act = async () => await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        var rejection = (await act.Should().ThrowAsync<SeedNormaliseException>()).Which;
        rejection.Failure.Should().Be(SeedNormaliseFailure.InvalidLlmOutput);
        rejection.Detail.Should().Contain("not loaded");
    }

    // ── Retries ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_RetriesARejectedAnswerAndAcceptsTheCorrectedOne()
    {
        var invented = Answer(
            [("chicken breast", 1, "pound", null), ("salt", 1, "teaspoon", null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]);

        var llm = new StubLlmStructuredClient(call => call == 1 ? invented : GoodAnswer());

        var recipe = await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        recipe.Attempts.Should().Be(2);
        recipe.Ingredients[1].Amount.Should().BeNull();
        llm.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task NormaliseAsync_StopsAfterMaxLlmRetriesAndReportsTheLastRejection()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 2 };
        var llm = new StubLlmStructuredClient(Answer(
            [("chicken breast", 1, "pound", null), ("salt", 1, "teaspoon", null)],
            [(1, "Wash hands.", []), (2, "Cook.", [0])]));

        var act = async () => await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        (await act.Should().ThrowAsync<SeedNormaliseException>())
            .Which.Failure.Should().Be(SeedNormaliseFailure.QuantityRejected);
        llm.CallCount.Should().Be(3, "the first attempt plus MaxLlmRetries");
    }

    [Fact]
    public async Task NormaliseAsync_MakesExactlyOneAttemptWhenRetriesAreTurnedOff()
    {
        _options = new RecipeSeedingOptions { MaxLlmRetries = 0 };
        var llm = new StubLlmStructuredClient(Answer([], []));

        var act = async () => await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        await act.Should().ThrowAsync<SeedNormaliseException>();
        llm.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_LetsRunCancellationUnwindRatherThanRetryingIt()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var llm = new StubLlmStructuredClient(GoodAnswer());

        var act = async () => await BuildNormaliser(llm)
            .NormaliseAsync(ParsedRecipe(), Fingerprint, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        llm.CallCount.Should().Be(1, "cancellation is not a rejected answer");
    }

    // ── What the model is asked ───────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_SendsTheSameExtractionSchemaThePhase3ScraperUses()
    {
        var llm = new StubLlmStructuredClient(GoodAnswer());

        await BuildNormaliser(llm).NormaliseAsync(ParsedRecipe(), Fingerprint);

        llm.Calls[0].Schema.ToJsonString().Should()
            .Be(JsonNode.Parse(RecipeScrapeService.RecipeSchemaJson)!.ToJsonString());
        llm.Calls[0].MaxTokens.Should().Be(_options.MaxLlmOutputTokens);
    }

    /// <summary>
    /// The prompt promises the model a closed unit vocabulary, and the gate then enforces one. If
    /// those two lists are ever written down separately they drift, and the symptom is a corpus-wide
    /// rejection for a unit the prompt said was fine — so the prompt derives its list from the same
    /// tables the gate consults (CLAUDE.md).
    /// </summary>
    [Fact]
    public void SystemPrompt_StatesExactlyTheUnitVocabularyThePipelineAccepts()
    {
        var line = SeedRecipeNormaliser.SystemPrompt
            .Split('\n')
            .Single(candidate => candidate.StartsWith("unit: MUST be"));

        var stated = line[(line.IndexOf(':', "unit:".Length) + 1)..].TrimEnd('.', ' ')
            .Split(", ", StringSplitOptions.TrimEntries);

        stated.Should().Contain(RecipeApp.API.Enums.MeasurementUnit.AcceptedSpellings);
        stated.Should().Contain(MeasurementConverter.ConvertibleUnits);

        // The worked example teaches units too, and a unit it teaches that the list forbids is a
        // prompt arguing with itself.
        foreach (var taught in System.Text.RegularExpressions.Regex
                     .Matches(SeedRecipeNormaliser.SystemPrompt, "\"unit\":\"(?<unit>[^\"]+)\"")
                     .Select(match => match.Groups["unit"].Value))
            stated.Should().Contain(taught);
    }

    /// <summary>
    /// Guards a removal, not a feature. A table of fraction conversions was added to the prompt and
    /// measured worse: the model stopped reading the line and started picking from the list, so
    /// <c>1 teaspoon salt</c> — which states no fraction at all — came back as 0.25. Anything that
    /// hands the model a menu of plausible amounts belongs nowhere near this prompt.
    /// </summary>
    [Fact]
    public void SystemPrompt_OffersNoMenuOfCandidateAmounts()
    {
        var prompt = SeedRecipeNormaliser.SystemPrompt;

        prompt.Should().NotContain("copy its value from this table");

        // The worked example states amounts, and that is the point of it. Outside the example, a
        // couple of decimals demonstrate a rule and a run of them is a menu — the table that failed
        // listed ten on one line.
        var exampleStart = prompt.IndexOf("Worked example", StringComparison.Ordinal);
        exampleStart.Should().BePositive();

        prompt[..exampleStart]
            .Split('\n')
            .Where(line => System.Text.RegularExpressions.Regex.Matches(line, @"\d+\.\d+").Count > 2)
            .Should().BeEmpty("a list of decimals reads as a set of answers to choose from");
    }

    [Fact]
    public void BuildUserContent_LabelsEveryLineAndStatesTheCountExpectedBack()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 pound chicken", "salt"],
            steps: ["Wash hands.", "Cook.", "Serve."]);

        var content = SeedRecipeNormaliser.BuildUserContent(parsed);

        content.Should().Contain("return exactly 2 ingredients");
        content.Should().Contain("[0] 1 pound chicken");
        content.Should().Contain("[1] salt");
        content.Should().Contain("return exactly 3 steps");
        content.Should().Contain("3. Serve.");
    }

    /// <summary>
    /// The ingredient labels are the indexes the answer is asked for, so the model copies a number
    /// instead of deriving one. Steps keep counting from one, because <c>step_number</c> does.
    /// </summary>
    [Fact]
    public void BuildUserContent_LabelsIngredientsFromZeroAndStepsFromOne()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["1 pound chicken", "salt", "1 onion"],
            steps: ["Wash hands.", "Cook."]);

        var content = SeedRecipeNormaliser.BuildUserContent(parsed);

        content.Should().Contain("[0] 1 pound chicken");
        content.Should().Contain("[2] 1 onion");
        content.Should().NotContain("[3]");

        content.Should().Contain("1. Wash hands.");
        content.Should().Contain("2. Cook.");

        content.Should().Contain("labels run from [0] to [2]");
    }

    /// <summary>
    /// Drupal splits one printed line across two spans, and for an optional ingredient the amount
    /// is in the second one — 109 corpus lines on 87 recipes. The model is shown the whole line and
    /// the gate reads the whole line, so extracting that amount is accepted rather than punished as
    /// an invention.
    /// </summary>
    [Fact]
    public async Task NormaliseAsync_AcceptsAQuantityStatedOnlyInTheNoteSpan()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["orange peel, dried"],
            steps: ["Add the orange peel."],
            ingredientNote: "(1 teaspoon, optional)");

        SeedRecipeNormaliser.BuildUserContent(parsed).Should()
            .Contain("[0] orange peel, dried (1 teaspoon, optional)");

        var llm = new StubLlmStructuredClient(Answer(
            [("orange peel", 1, "teaspoon", "dried, optional")],
            [(1, "Add the orange peel.", [0])]));

        var recipe = await BuildNormaliser(llm).NormaliseAsync(parsed, Fingerprint);

        recipe.Ingredients[0].Amount.Should().Be(1m);
        recipe.Ingredients[0].Unit.Should().Be("tsp");
        recipe.Ingredients[0].Notes.Should().Be("dried, optional");
        recipe.Ingredients[0].SourceText.Should()
            .Be("orange peel, dried (1 teaspoon, optional)", "the gate must be re-checkable");
    }

    /// <summary>The mirror case: with no number anywhere on the line, a quantity is still an invention.</summary>
    [Fact]
    public async Task NormaliseAsync_StillRejectsAnInventionWhenNeitherSpanStatesAQuantity()
    {
        var parsed = ParsedRecipe(
            ingredientLines: ["black pepper"],
            steps: ["Season."],
            ingredientNote: "(freshly ground)");

        var rejection = await RejectionOf(
            Answer([("black pepper", 1, "teaspoon", null)], [(1, "Season.", [0])]), parsed);

        rejection.Detail.Should().Contain(nameof(SeedQuantityRejection.InventedQuantity));
    }
}
