using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// Stage 3 of the seed import (Phase 9 §10). Pure parsing — no database, no network, no inference.
///
/// <para>The fixtures are archived MyPlate pages copied verbatim out of the harvest, including the
/// Wayback toolbar the archive injects. That matters: a tidied excerpt would pass a scoping bug
/// that a real page catches. Each one was chosen for a specific hazard, named on its constant.</para>
/// </summary>
public class MyPlateRecipeParserTests
{
    /// <summary>
    /// Primary template. Carries a JSON-LD description truncated at ~150 characters (§10.3
    /// hazard 1), a footnoted <c>*</c> aside after the step list (hazard 5), an adapted-source
    /// credit (hazard 6), <c>&amp;nbsp;</c> inside a step, and parenthetical prep notes.
    /// </summary>
    private const string ChickenCreole = "20-minute-chicken-creole";

    /// <summary>
    /// Legacy template — ingredients under <c>field--name-field-ingredients</c>, which 65 of the
    /// 1,089 harvested pages use and which §10.1 originally did not account for.
    /// </summary>
    private const string AppleTunaSandwiches = "apple-tuna-sandwiches";

    /// <summary>
    /// Two step lists separated by a section label, plus emboldened ingredient group headings
    /// ("For the Dressing:") that are not ingredients.
    /// </summary>
    private const string CubanSalad = "cuban-salad";

    /// <summary>A step containing a nested sub-list, whose text must be counted once.</summary>
    private const string BlackBeanCouscous = "black-bean-and-couscous-salad";

    private const string CanonicalUrlPrefix = "https://www.myplate.gov/recipes/";

    private static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Seeding", "Fixtures", "myplate");

    private static MyPlateRecipeParser Build(RecipeSeedingOptions? options = null) =>
        new(Options.Create(options ?? new RecipeSeedingOptions()));

    private static ParsedSeedRecipe ParseFixture(string slug, RecipeSeedingOptions? options = null)
    {
        var html = File.ReadAllText(Path.Combine(FixtureDirectory, slug + ".html"));
        return Build(options).Parse(html, slug, CanonicalUrlPrefix + slug);
    }

    // ── JSON-LD scalars ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsNameFromJsonLd()
    {
        ParseFixture(ChickenCreole).Name.Should().Be("20-Minute Chicken Creole");
    }

    [Fact]
    public void Parse_ReadsServingsFromRecipeYield()
    {
        var recipe = ParseFixture(ChickenCreole);

        recipe.Servings.Should().Be(8);
        recipe.SourceYield.Should().Be("8 servings");
    }

    [Fact]
    public void Parse_KeepsTheSlugAndTheCanonicalSourceUrl()
    {
        var recipe = ParseFixture(ChickenCreole);

        recipe.Slug.Should().Be(ChickenCreole);
        recipe.SourceUrl.Should().Be(CanonicalUrlPrefix + ChickenCreole);
        recipe.SourceUrl.Should().NotContain("web.archive.org");
    }

    // ── Hazard 1: truncated descriptions ──────────────────────────────────────

    [Fact]
    public void Parse_PrefersTheFullHtmlDescriptionOverTheTruncatedJsonLdOne()
    {
        var recipe = ParseFixture(ChickenCreole);

        // The JSON-LD copy stops at "…"; the page's own field carries the whole thing.
        recipe.Description.Should().NotBeNull();
        recipe.Description.Should().NotEndWith("…");
        recipe.Description.Should().EndWith("cooked on the stovetop or with an electric skillet.");
        recipe.Description!.Length.Should().BeGreaterThan(150);
    }

    // ── Ingredients (§10.2) ───────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsIngredientLinesVerbatimWithoutParsingQuantities()
    {
        var recipe = ParseFixture(ChickenCreole);

        recipe.Ingredients.Should().HaveCount(11);
        recipe.Ingredients[0].Text.Should().Be("1 tablespoon vegetable oil");
        recipe.Ingredients[2].Text.Should().Be("1 can (14.5 ounces) no salt added diced tomatoes");
        recipe.Ingredients[^1].Text.Should().Be("1/4 teaspoon cayenne pepper");
    }

    [Fact]
    public void Parse_SplitsTheParentheticalPrepNoteOffTheIngredientText()
    {
        var celery = ParseFixture(ChickenCreole).Ingredients
            .Single(ingredient => ingredient.Text.Contains("celery"));

        celery.Text.Should().Be("2 medium celery stalks");
        celery.Note.Should().Be("(chopped)");
    }

    [Fact]
    public void Parse_LeavesTheNoteNullWhenAnIngredientHasNone()
    {
        ParseFixture(ChickenCreole).Ingredients
            .Single(ingredient => ingredient.Text.Contains("cayenne"))
            .Note.Should().BeNull();
    }

    [Fact]
    public void Parse_DropsEmboldenedIngredientGroupHeadings()
    {
        var recipe = ParseFixture(CubanSalad);

        // "For the Dressing:" and "For the Salad:" are layout, not shopping.
        recipe.Ingredients.Should().NotContain(ingredient => ingredient.Text.EndsWith(":"));
        recipe.Ingredients.Should().HaveCount(9);
        recipe.Ingredients[0].Text.Should().Be("3 tablespoons vegetable oil");
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsOrderedStepsFromTheMarkupsOwnList()
    {
        var recipe = ParseFixture(ChickenCreole);

        recipe.Steps.Should().HaveCount(6);
        recipe.Steps[0].Should().Be("Wash hands with soap and water.");
        recipe.Steps[^1].Should().Be("Serve over hot, cooked rice or whole wheat pasta.");
    }

    [Fact]
    public void Parse_CollectsEveryStepListAndFoldsSectionLabelsOntoTheStepTheyIntroduce()
    {
        var recipe = ParseFixture(CubanSalad);

        // Two lists, split by "To make the salad:". Taking only the first would drop the rest of
        // the recipe; emitting the label as its own step would put a contentless step in Cooking
        // Mode. One step comes from the first list and two from the second, so the count is
        // asserted exactly — a dropped list fails here rather than slipping past a lower bound.
        recipe.Steps.Should().HaveCount(3);
        recipe.Steps[0].Should().StartWith("To make the dressing:");
        recipe.Steps[1].Should().StartWith("To make the salad:");
        recipe.Steps[2].Should().StartWith("Pour the dressing over");
        recipe.Steps.Should().NotContain(step => step.EndsWith(":"));
    }

    [Fact]
    public void Parse_CountsANestedSubListOnceAsPartOfItsParentStep()
    {
        var recipe = ParseFixture(BlackBeanCouscous);

        var preparation = recipe.Steps
            .Single(step => step.StartsWith("Before starting to prepare the recipe:"));

        preparation.Should().Contain("Collect, mince, and measure all ingredients.");

        // The sub-list's items belong to that step and must not also stand alone.
        recipe.Steps.Should().NotContain("Collect, mince, and measure all ingredients.");
        recipe.Steps.Should().OnlyHaveUniqueItems();
    }

    // ── Hazard 5: notes bleeding into instructions ────────────────────────────

    [Fact]
    public void Parse_RoutesTheAsideThatFollowsTheStepListToNotesRatherThanToAStep()
    {
        var recipe = ParseFixture(ChickenCreole);

        recipe.Steps.Should().NotContain(step => step.Contains("Store bought chili sauce"));
        recipe.Notes.Should().Contain("Store bought chili sauce can be high in sodium");
    }

    [Fact]
    public void Parse_KeepsTheRecipesOwnNotesFieldAlongsideTheTrailingAside()
    {
        var notes = ParseFixture(ChickenCreole).Notes;

        notes.Should().Contain("Learn more about:");
        notes.Should().Contain("Store bought chili sauce");
    }

    // ── Hazard 6: adapted-source credits ──────────────────────────────────────

    [Fact]
    public void Parse_CapturesTheSourceCreditWithoutItsLabel()
    {
        var credit = ParseFixture(ChickenCreole).SourceCredit;

        credit.Should().Be(
            "Recipe Adapted from: Food Hero Oregon State University Cooperative Extension Service");
        credit.Should().NotStartWith("Source:");
    }

    // ── Hazard 3: Wayback URL rewriting ───────────────────────────────────────

    [Fact]
    public void Parse_UnwrapsTheArchiveSnapshotPrefixFromTheImageUrl()
    {
        var imageUrl = ParseFixture(ChickenCreole).ImageUrl;

        imageUrl.Should().NotBeNull();
        imageUrl.Should().NotContain("web.archive.org");
        imageUrl.Should().StartWith("https://myplate-prod.azureedge.us/");
    }

    [Theory]
    [InlineData("http://web.archive.org/web/20251231013807/https://example.test/a.jpg",
                "https://example.test/a.jpg")]
    [InlineData("https://web.archive.org/web/20251231013807im_/https://example.test/a.jpg",
                "https://example.test/a.jpg")]
    [InlineData("/web/20251231013807/https://example.test/a.jpg", "https://example.test/a.jpg")]
    [InlineData("https://example.test/a.jpg", "https://example.test/a.jpg")]
    public void UnwrapArchiveUrl_StripsAnySnapshotPrefixAndLeavesAPlainUrlAlone(
        string archived, string expected)
    {
        MyPlateRecipeParser.UnwrapArchiveUrl(archived).Should().Be(expected);
    }

    [Fact]
    public void UnwrapArchiveUrl_PassesNullThrough()
    {
        MyPlateRecipeParser.UnwrapArchiveUrl(null).Should().BeNull();
    }

    // ── Hazard 4: Wayback toolbar injection ───────────────────────────────────

    [Fact]
    public void Parse_IgnoresTheToolbarMarkupTheArchiveInjectsIntoEveryPage()
    {
        var recipe = ParseFixture(ChickenCreole);

        var everything = string.Join(
            "\n",
            [recipe.Name, recipe.Description, recipe.Notes, recipe.SourceCredit,
             .. recipe.Steps, .. recipe.Ingredients.Select(ingredient => ingredient.Text)]);

        everything.Should().NotContain("web.archive.org");
        everything.Should().NotContain("Wayback");
        everything.Should().NotContain("INTERNET ARCHIVE");
    }

    // ── Text handling ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NormalisesNonBreakingSpacesAndLeavesNoUndecodedEntities()
    {
        var recipe = ParseFixture(ChickenCreole);

        var step = recipe.Steps.Single(s => s.Contains("internal temperature"));

        // The page has "165 degrees F&nbsp;(3-5 minutes)" — the artefact §10.3 hazard 2 read as
        // mojibake. It is an entity, and it becomes an ordinary space.
        step.Should().Contain("165 degrees F (3-5 minutes)");
        step.Should().NotContain("\u00A0");
        step.Should().NotContain("&nbsp;");
        step.Should().NotContain("\uFFFD");
    }

    [Fact]
    public void Parse_LeavesNoMarkupInAnyExtractedText()
    {
        var recipe = ParseFixture(CubanSalad);

        foreach (var text in recipe.Steps.Concat(recipe.Ingredients.Select(i => i.Text)))
            text.Should().NotContainAny("<", ">");
    }

    // ── Legacy template (§10.1) ───────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsTheLegacyTemplateWhoseIngredientFieldUsesTheOlderClass()
    {
        var recipe = ParseFixture(AppleTunaSandwiches);

        recipe.Template.Should().Be(MyPlateTemplate.Legacy);
        recipe.Name.Should().Be("Apple Tuna Sandwiches");
        recipe.Servings.Should().Be(3);
        recipe.Ingredients.Should().HaveCount(7);
        recipe.Ingredients[0].Text.Should().Be("1 can (6.5 ounces) tuna, packed in water, drained");
        recipe.Steps.Should().HaveCount(6);
        recipe.SourceCredit.Should().Be("Pennsylvania Nutrition Education Network");
    }

    [Fact]
    public void Parse_ReportsThePrimaryTemplateForThePagesThatUseTheNewerClass()
    {
        ParseFixture(ChickenCreole).Template.Should().Be(MyPlateTemplate.Primary);
    }

    // ── Servings fallback ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("8 servings", 8)]
    [InlineData("4 Servings", 4)]
    [InlineData("12", 12)]
    [InlineData("6 Muffins", 6)]
    [InlineData("1 Serving", 1)]
    public void Parse_TakesTheLeadingIntegerOfWhateverFormTheYieldIsPublishedIn(
        string recipeYield, int expected)
    {
        var recipe = Build().Parse(PageWith(recipeYield: recipeYield), "slug", "https://example.test");

        recipe.Servings.Should().Be(expected);
        recipe.SourceYield.Should().Be(recipeYield);
    }

    [Fact]
    public void Parse_FallsBackToTheConfiguredServingsWhenThePageStatesNoYield()
    {
        var options = new RecipeSeedingOptions { DefaultServings = 6 };

        var recipe = Build(options).Parse(PageWith(recipeYield: null), "slug", "https://example.test");

        recipe.Servings.Should().Be(6);
        recipe.SourceYield.Should().BeNull();
    }

    [Fact]
    public void Parse_FallsBackToTheConfiguredServingsWhenTheYieldCarriesNoNumber()
    {
        var recipe = Build(new RecipeSeedingOptions { DefaultServings = 4 })
            .Parse(PageWith(recipeYield: "Makes a batch"), "slug", "https://example.test");

        recipe.Servings.Should().Be(4);
    }

    // ── Parse failure (§10.4) ─────────────────────────────────────────────────

    [Fact]
    public void Parse_FailsWhenThePageHasNoRecipeContentRoot()
    {
        var act = () => Build().Parse("<html><body><p>Not a recipe.</p></body></html>",
            "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoContentRoot);
    }

    [Fact]
    public void Parse_FailsWhenNeitherIngredientFieldClassIsPresent()
    {
        var act = () => Build().Parse(PageWith(ingredientClass: null), "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoIngredientBlock);
    }

    [Fact]
    public void Parse_FailsWhenTheIngredientBlockHoldsNoItems()
    {
        var act = () => Build().Parse(PageWith(ingredients: ""), "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoIngredients);
    }

    [Fact]
    public void Parse_FailsWhenThereIsNoInstructionBlock()
    {
        var act = () => Build().Parse(PageWith(includeInstructions: false),
            "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoInstructionBlock);
    }

    [Fact]
    public void Parse_FailsWhenTheInstructionBlockYieldsNoSteps()
    {
        var act = () => Build().Parse(PageWith(steps: ""), "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoSteps);
    }

    [Fact]
    public void Parse_FailsWhenThePageCarriesNoNameAtAll()
    {
        var act = () => Build().Parse(PageWith(name: null, includeHeading: false),
            "slug", "https://example.test");

        act.Should().Throw<SeedParseException>()
            .Which.Failure.Should().Be(SeedParseFailure.NoName);
    }

    [Fact]
    public void Parse_FallsBackToTheHeadingWhenJsonLdCarriesNoName()
    {
        var recipe = Build().Parse(PageWith(name: null), "slug", "https://example.test");

        recipe.Name.Should().Be("Heading Name");
    }

    [Fact]
    public void SeedParseException_ReportsAStateReasonNamingTheFailure()
    {
        new SeedParseException(SeedParseFailure.NoIngredientBlock, "…")
            .StateReason.Should().Be("ParseFailed: NoIngredientBlock");
    }

    // ── Minimal synthetic page ────────────────────────────────────────────────

    /// <summary>
    /// The smallest page shaped like a MyPlate one, for cases a real fixture cannot express —
    /// a missing field, an unusual yield. Real markup covers everything else.
    /// </summary>
    private static string PageWith(
        string? name = "JSON-LD Name",
        string? recipeYield = "4 servings",
        string? ingredientClass = "field--name-field-mp-ingredients",
        string ingredients = "<li>1 cup rice</li>",
        string steps = "<li>Cook the rice.</li>",
        bool includeInstructions = true,
        bool includeHeading = true)
    {
        var jsonLd = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@type"] = "Recipe",
            ["name"] = name,
            ["recipeYield"] = recipeYield,
        });

        var ingredientBlock = ingredientClass is null
            ? string.Empty
            : $"<div class=\"field {ingredientClass}\"><h2>Ingredients</h2>"
              + $"<ul>{ingredients}</ul></div>";

        var instructionBlock = includeInstructions
            ? "<div class=\"field field--name-field-instructions\"><h2>Directions</h2>"
              + $"<div class=\"field__item\"><ol>{steps}</ol></div></div>"
            : string.Empty;

        var heading = includeHeading ? "<h1>Heading Name</h1>" : string.Empty;

        return "<html><head><script type=\"application/ld+json\">" + jsonLd + "</script></head>"
             + "<body>"
             + "<div id=\"wm-ipp-base\">INTERNET ARCHIVE Wayback Machine toolbar</div>"
             + "<article class=\"mp-recipe-full\">"
             + heading + ingredientBlock + instructionBlock
             + "</article></body></html>";
    }
}
