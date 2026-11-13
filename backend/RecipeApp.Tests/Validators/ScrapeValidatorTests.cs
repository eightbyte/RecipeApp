using FluentValidation.TestHelper;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Validators;

namespace RecipeApp.Tests.Validators;

/// <summary>The scrape validators guard the LLM import path, where measurements and ingredient names arrive from outside the app, including the unit whitelist.</summary>
public class ScrapeValidatorTests
{
    private readonly ScrapeRecipeRequestValidator _urlValidator = new();
    private readonly ScrapeConfirmIngredientRequestValidator _ingredientValidator = new();
    private readonly ScrapeConfirmRequestValidator _confirmValidator = new();

    // ── ScrapeRecipeRequestValidator ──────────────────────────────────────────

    [Theory]
    [InlineData("http://example.com/recipe")]
    [InlineData("https://example.com/recipe")]
    [InlineData("https://example.com/recipe?serves=4#method")]
    public void Url_HttpOrHttps_HasNoErrors(string url)
    {
        var result = _urlValidator.TestValidate(new ScrapeRecipeRequest(url));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Url_Empty_HasErrorOnUrl(string url)
    {
        var result = _urlValidator.TestValidate(new ScrapeRecipeRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("example.com/recipe")]
    [InlineData("/recipes/12")]
    public void Url_NotAbsolute_HasErrorOnUrl(string url)
    {
        var result = _urlValidator.TestValidate(new ScrapeRecipeRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("ftp://example.com/recipe")]
    [InlineData("file:///C:/recipes/pasta.html")]
    [InlineData("javascript:alert(1)")]
    public void Url_NonHttpScheme_HasErrorOnUrl(string url)
    {
        // The scraper fetches whatever it is handed, so the scheme gate is a boundary check and not a formatting preference.
        var result = _urlValidator.TestValidate(new ScrapeRecipeRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    // ── ScrapeConfirmIngredientRequestValidator: the new-ingredient branch ────────────

    [Fact]
    public void Ingredient_ExistingId_NeedsNoNewIngredientFields()
    {
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { IngredientId = Guid.NewGuid(), NewIngredientName = null, NewIngredientDisplayName = null, Category = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_NoIdAndNoNewIngredientFields_HasErrorOnEach()
    {
        // Without an IngredientId the confirm step has to create the ingredient, so all three fields it needs become required together.
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { NewIngredientName = null, NewIngredientDisplayName = null, Category = null });

        result.ShouldHaveValidationErrorFor(x => x.NewIngredientName);
        result.ShouldHaveValidationErrorFor(x => x.NewIngredientDisplayName);
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void Ingredient_NoIdWithBlankNewIngredientName_HasErrorOnNewIngredientName()
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { NewIngredientName = "   " });
        result.ShouldHaveValidationErrorFor(x => x.NewIngredientName);
    }

    [Fact]
    public void Ingredient_NoIdWithAllNewIngredientFields_HasNoErrors()
    {
        var result = _ingredientValidator.TestValidate(Ingredient());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_InvalidCategory_HasErrorOnCategory()
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Category = "SNACKS" });
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Theory]
    [MemberData(nameof(AllCategories))]
    public void Ingredient_EachValidCategory_HasNoErrorOnCategory(string category)
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Category = category });
        result.ShouldNotHaveValidationErrorFor(x => x.Category);
    }

    public static IEnumerable<object[]> AllCategories =>
        IngredientCategory.All.Select(c => new object[] { c });

    // ── ScrapeConfirmIngredientRequestValidator: the measurement whitelist ────────────

    [Theory]
    [MemberData(nameof(AllUnits))]
    public void Ingredient_EachStorableUnit_HasNoErrorOnUnit(string unit)
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Unit = unit });
        result.ShouldNotHaveValidationErrorFor(x => x.Unit);
    }

    public static IEnumerable<object[]> AllUnits =>
        MeasurementUnit.All.Select(u => new object[] { u });

    [Theory]
    [InlineData("oz")]
    [InlineData("lb")]
    [InlineData("fl oz")]
    [InlineData("pint")]
    [InlineData("quart")]
    public void Ingredient_EachEntryOnlyUnit_HasNoErrorOnUnit(string unit)
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Unit = unit });
        result.ShouldNotHaveValidationErrorFor(x => x.Unit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cups")]        // a known alias — lenient enough for TryCanonicalise, not for storage
    [InlineData("Tbsp")]        // right unit, wrong case
    [InlineData("ML")]
    [InlineData("clove")]       // no fixed size; cannot be canonicalised at all
    [InlineData("pinch")]
    [InlineData("ounces")]      // an entry-only unit is accepted only in its canonical spelling
    [InlineData("OZ")]
    [InlineData("gallon")]      // Stage A converts it, but the picker does not offer it
    public void Ingredient_NonCanonicalUnit_HasErrorOnUnit(string unit)
    {
        // The whitelist is strict because canonicalisation happens upstream, so an uncanonicalised unit reaching the validator is a bug to surface.
        var result = _ingredientValidator.TestValidate(Ingredient() with { Unit = unit });
        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ingredient_NonPositiveAmount_HasErrorOnAmount(decimal amount)
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Amount = amount });
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    // ── Unquantified ingredients ──────────────────────────────────────────────

    [Fact]
    public void Ingredient_NullAmountAndNullUnit_HasNoErrors()
    {
        // A scraped page reading "salt to taste" has always had this problem, and the preview used to show a number the model invented.
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { Amount = null, Unit = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_NullAmountWithUnit_HasErrorOnUnit()
    {
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { Amount = null, Unit = MeasurementUnit.Gram });

        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void Ingredient_AmountWithNullUnit_HasNoErrors()
    {
        // "2 eggs" is a count, which is stored as pcs.
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { Amount = 2m, Unit = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_ZeroAmountWithNullUnit_HasErrorOnAmount()
    {
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { Amount = 0m, Unit = null });

        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    // ── ScrapeConfirmIngredientRequestValidator: provenance bounds ───────────────────

    [Fact]
    public void Ingredient_NoSourceMeasurement_HasNoErrors()
    {
        // Provenance is optional: a recipe entered in canonical units has nothing to record.
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { SourceAmount = null, SourceUnit = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Ingredient_SourceMeasurement_RoundTripsUnvalidatedAgainstTheWhitelist()
    {
        // SourceUnit records what the page said verbatim and is provenance rather than arithmetic, so the storable-unit whitelist must not apply.
        var result = _ingredientValidator.TestValidate(
            Ingredient() with { Amount = 240m, Unit = MeasurementUnit.Millilitre, SourceAmount = 1m, SourceUnit = "cups" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void Ingredient_NonPositiveSourceAmount_HasErrorOnSourceAmount(decimal sourceAmount)
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { SourceAmount = sourceAmount });
        result.ShouldHaveValidationErrorFor(x => x.SourceAmount);
    }

    [Fact]
    public void Ingredient_EmptySourceUnit_HasErrorOnSourceUnit()
    {
        // Present-but-empty is a scraper bug; absent is the way to say "no provenance".
        var result = _ingredientValidator.TestValidate(Ingredient() with { SourceUnit = "" });
        result.ShouldHaveValidationErrorFor(x => x.SourceUnit);
    }

    [Fact]
    public void Ingredient_OverlongSourceUnit_HasErrorOnSourceUnit()
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { SourceUnit = new string('x', 33) });
        result.ShouldHaveValidationErrorFor(x => x.SourceUnit);
    }

    [Fact]
    public void Ingredient_NotesAtTheRecipeLimit_HasNoErrorOnNotes()
    {
        // Edit allows 500, so a scrape must too, or the preview would refuse notes the recipe form accepts.
        var result = _ingredientValidator.TestValidate(Ingredient() with { Notes = new string('x', 500) });
        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Ingredient_NotesOverTheRecipeLimit_HasErrorOnNotes()
    {
        var result = _ingredientValidator.TestValidate(Ingredient() with { Notes = new string('x', 501) });
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    // ── ScrapeConfirmRequestValidator ─────────────────────────────────────────

    [Fact]
    public void Confirm_Valid_HasNoErrors()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Confirm_EmptyName_HasErrorOnName()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Confirm_NameOverTheRecipeLimit_HasErrorOnName()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Name = new string('x', 201) });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Confirm_DescriptionAtTheRecipeLimit_HasNoErrorOnDescription()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Description = new string('x', 2000) });
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Confirm_DescriptionOverTheRecipeLimit_HasErrorOnDescription()
    {
        // A scrape that saved a description Edit refuses would leave a recipe that can never be saved again.
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Description = new string('x', 2001) });
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Confirm_NoDescription_HasNoErrorOnDescription()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Description = null });
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Confirm_StepInstructionAtTheRecipeLimit_HasNoErrorOnThatStep()
    {
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, new string('x', 2000), [0])] });

        result.ShouldNotHaveValidationErrorFor("Steps[0].Instruction");
    }

    [Fact]
    public void Confirm_StepInstructionOverTheRecipeLimit_HasErrorOnThatStep()
    {
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, new string('x', 2001), [0])] });

        result.ShouldHaveValidationErrorFor("Steps[0].Instruction");
    }

    [Fact]
    public void Confirm_NoSourceUrl_IsValid()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { SourceUrl = null });
        result.ShouldNotHaveValidationErrorFor(x => x.SourceUrl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Confirm_ServingsOutOfRange_HasErrorOnServings(int servings)
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Servings = servings });
        result.ShouldHaveValidationErrorFor(x => x.Servings);
    }

    [Fact]
    public void Confirm_NoIngredients_HasErrorOnIngredients()
    {
        var result = _confirmValidator.TestValidate(ConfirmRequest() with { Ingredients = [] });
        result.ShouldHaveValidationErrorFor(x => x.Ingredients);
    }

    [Fact]
    public void Confirm_InvalidChildIngredient_HasErrorOnThatIngredient()
    {
        // Proves RuleForEach runs the child validator, since the whitelist only guards the import path if it reaches the ingredients.
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Ingredients = [Ingredient() with { Unit = "clove" }] });

        result.ShouldHaveValidationErrorFor("Ingredients[0].Unit");
    }

    [Fact]
    public void Confirm_StepIngredientIndexOutOfRange_HasErrorOnThatStep()
    {
        // One ingredient means index 0 is the only valid reference; 1 points past the end.
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, "Combine everything.", [1])] });

        result.ShouldHaveValidationErrorFor("Steps[0].IngredientIndexes");
    }

    [Fact]
    public void Confirm_NegativeStepIngredientIndex_HasErrorOnThatStep()
    {
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, "Combine everything.", [-1])] });

        result.ShouldHaveValidationErrorFor("Steps[0].IngredientIndexes");
    }

    [Fact]
    public void Confirm_EmptyStepIngredientIndexes_HasNoErrors()
    {
        // A step need not reference any ingredient ("Preheat the oven").
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, "Preheat the oven.", [])] });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Confirm_StepNumberBelowOne_HasErrorOnThatStep(int stepNumber)
    {
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(stepNumber, "Combine everything.", [0])] });

        result.ShouldHaveValidationErrorFor("Steps[0].StepNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Confirm_EmptyStepInstruction_HasErrorOnThatStep(string instruction)
    {
        var result = _confirmValidator.TestValidate(
            ConfirmRequest() with { Steps = [new ScrapeConfirmStepRequest(1, instruction, [0])] });

        result.ShouldHaveValidationErrorFor("Steps[0].Instruction");
    }

    [Fact]
    public void Confirm_SecondStepInvalid_ErrorIsKeyedToItsOwnIndex()
    {
        // The failure keys are built by hand, so the index they carry is pinned: a reviewer fixing step 2 should not be sent to step 1.
        var result = _confirmValidator.TestValidate(ConfirmRequest() with
        {
            Steps =
            [
                new ScrapeConfirmStepRequest(1, "Combine everything.", [0]),
                new ScrapeConfirmStepRequest(2, "", [0]),
            ],
        });

        result.ShouldHaveValidationErrorFor("Steps[1].Instruction");
        result.ShouldNotHaveValidationErrorFor("Steps[0].Instruction");
    }

    // ── Photo ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("https://cdn.example.com/chili.jpg")]
    [InlineData("http://cdn.example.com/chili.jpg?w=1200")]
    public void Confirm_NoPhotoOrAnHttpPhoto_HasNoErrors(string? imageUrl) =>
        _confirmValidator.TestValidate(ConfirmRequest() with { ImageUrl = imageUrl })
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("/img/chili.jpg")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("file:///C:/photos/chili.jpg")]
    [InlineData("ftp://cdn.example.com/chili.jpg")]
    public void Confirm_PhotoNotAnAbsoluteHttpUrl_HasErrorOnImageUrl(string imageUrl)
    {
        // The server downloads whatever it is handed, so the scheme gate is a boundary check.
        _confirmValidator.TestValidate(ConfirmRequest() with { ImageUrl = imageUrl })
            .ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void Confirm_PhotoAtTheLengthLimit_HasNoErrors()
    {
        var prefix = "https://cdn.example.com/";
        var url    = prefix + new string('a', RecipeLimits.ImageUrlMaxLength - prefix.Length);

        _confirmValidator.TestValidate(ConfirmRequest() with { ImageUrl = url })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Confirm_PhotoOverTheLengthLimit_HasErrorOnImageUrl()
    {
        var prefix = "https://cdn.example.com/";
        var url    = prefix + new string('a', RecipeLimits.ImageUrlMaxLength - prefix.Length + 1);

        _confirmValidator.TestValidate(ConfirmRequest() with { ImageUrl = url })
            .ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>A valid new-ingredient row: no IngredientId, so the create branch applies.</summary>
    private static ScrapeConfirmIngredientRequest Ingredient() =>
        new(
            IngredientId: null,
            NewIngredientName: "plain flour",
            NewIngredientDisplayName: "Plain Flour",
            Category: IngredientCategory.DryGoods,
            Amount: 250m,
            Unit: MeasurementUnit.Gram,
            Notes: null,
            DisplayOrder: 0);

    private static ScrapeConfirmRequest ConfirmRequest() =>
        new(
            Name: "Pasta",
            Description: "A quick weeknight pasta.",
            SourceUrl: "https://example.com/recipe",
            Servings: 4,
            Ingredients: [Ingredient()],
            Steps: [new ScrapeConfirmStepRequest(1, "Combine everything.", [0])]);
}
