using AngleSharp.Html.Parser;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

/// <summary>
/// Unit tests for RecipeJsonLdExtractor — the schema.org Recipe JSON-LD detector that runs
/// before visible-text stripping in RecipeScrapeService.
/// </summary>
public class RecipeJsonLdExtractorTests
{
    private static async Task<string?> ExtractAsync(string html)
    {
        var parser = new HtmlParser();
        var doc    = await parser.ParseDocumentAsync(html);
        return RecipeJsonLdExtractor.TryBuildRecipeText(doc);
    }

    [Fact]
    public async Task PlainRecipeObject_WithStringIngredientsAndInstructions_ExtractsAllFields()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@context": "https://schema.org/",
              "@type": "Recipe",
              "name": "Lasagna",
              "description": "A classic layered pasta bake.",
              "recipeYield": "8",
              "recipeIngredient": ["500g beef mince", "2 sticks celery"],
              "recipeInstructions": ["Brown the mince.", "Layer with pasta sheets."]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Lasagna");
        text.Should().Contain("Description: A classic layered pasta bake.");
        text.Should().Contain("Servings: 8");
        text.Should().Contain("- 500g beef mince");
        text.Should().Contain("- 2 sticks celery");
        text.Should().Contain("1. Brown the mince.");
        text.Should().Contain("2. Layer with pasta sheets.");
    }

    [Fact]
    public async Task GraphWrappedRecipe_IsFound()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "WebPage", "name": "Some blog post" },
                {
                  "@type": "Recipe",
                  "name": "Banana Bread",
                  "recipeIngredient": ["2 bananas", "300g flour"],
                  "recipeInstructions": ["Mash bananas.", "Mix and bake."]
                }
              ]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Banana Bread");
        text.Should().Contain("- 2 bananas");
    }

    [Fact]
    public async Task ArrayOfTopLevelObjects_FindsRecipeAmongOthers()
    {
        var html = """
            <html><body><script type="application/ld+json">
            [
              { "@type": "BreadcrumbList", "name": "Breadcrumbs" },
              {
                "@type": "Recipe",
                "name": "Tomato Soup",
                "recipeIngredient": ["1kg tomatoes"],
                "recipeInstructions": ["Simmer tomatoes.", "Blend until smooth."]
              }
            ]
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Tomato Soup");
    }

    [Fact]
    public async Task RecipeTypeAsArray_IsRecognised()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": ["Recipe", "Article"],
              "name": "Hybrid Type Recipe",
              "recipeIngredient": ["1 egg"],
              "recipeInstructions": ["Boil the egg."]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Hybrid Type Recipe");
    }

    [Fact]
    public async Task HowToStepInstructions_UsesTextField()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Steps Recipe",
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": [
                { "@type": "HowToStep", "text": "Dice the onion." },
                { "@type": "HowToStep", "text": "Fry until golden." }
              ]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("1. Dice the onion.");
        text.Should().Contain("2. Fry until golden.");
    }

    [Fact]
    public async Task HowToSection_FlattensNestedSteps()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Sectioned Recipe",
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": [
                {
                  "@type": "HowToSection",
                  "name": "For the sauce",
                  "itemListElement": [
                    { "@type": "HowToStep", "text": "Dice the onion." },
                    { "@type": "HowToStep", "text": "Simmer for 20 minutes." }
                  ]
                }
              ]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("[For the sauce]");
        text.Should().Contain("Dice the onion.");
        text.Should().Contain("Simmer for 20 minutes.");
    }

    [Fact]
    public async Task RecipeInstructionsAsPlainString_IsUsedAsSingleStep()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Blob Recipe",
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": "Dice the onion, then fry until golden."
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("1. Dice the onion, then fry until golden.");
    }

    [Fact]
    public async Task HtmlEntitiesAndTags_AreDecodedAndStripped()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Tagged Recipe",
              "description": "&lt;p&gt;Won&#39;t ever get sick of this&lt;/p&gt;",
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": ["Dice it."]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Description: Won't ever get sick of this");
        text.Should().NotContain("&lt;");
        text.Should().NotContain("<p>");
    }

    [Fact]
    public async Task RecipeYieldAsArray_TakesFirstEntry()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Yield Array Recipe",
              "recipeYield": ["8", "8 servings"],
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": ["Dice it."]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Servings: 8");
    }

    [Fact]
    public async Task MissingIngredients_ReturnsNull()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Incomplete Recipe",
              "recipeInstructions": ["Do the thing."]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().BeNull();
    }

    [Fact]
    public async Task MissingInstructions_ReturnsNull()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Incomplete Recipe",
              "recipeIngredient": ["1 onion"]
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().BeNull();
    }

    [Fact]
    public async Task NonRecipeType_ReturnsNull()
    {
        var html = """
            <html><body><script type="application/ld+json">
            {
              "@type": "Article",
              "name": "Just an article",
              "articleBody": "Some text."
            }
            </script></body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().BeNull();
    }

    [Fact]
    public async Task NoJsonLdScripts_ReturnsNull()
    {
        var html = "<html><body><p>No structured data here.</p></body></html>";

        var text = await ExtractAsync(html);

        text.Should().BeNull();
    }

    [Fact]
    public async Task MalformedJson_IsSkippedWithoutThrowing()
    {
        var html = """
            <html><body>
            <script type="application/ld+json">{ this is not valid json </script>
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Recovered Recipe",
              "recipeIngredient": ["1 onion"],
              "recipeInstructions": ["Dice it."]
            }
            </script>
            </body></html>
            """;

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Recovered Recipe");
    }

    [Fact]
    public async Task UnescapedLineBreakInsideStringValue_IsRecovered()
    {
        // Real-world bug found on andy-cooks.com: their JSON-LD wraps long "text" values across
        // source lines without escaping the line break, which is invalid per strict JSON (raw
        // control characters aren't allowed inside string literals). Confirmed via System.Text.Json
        // throwing "'0x0A' is invalid within a JSON string" on the site's actual markup.
        var html = "<html><body><script type=\"application/ld+json\">\n" +
                    "{\n" +
                    "\"@type\": \"Recipe\",\n" +
                    "\"name\": \"Wrapped Text Recipe\",\n" +
                    "\"recipeIngredient\": [\"1 onion\"],\n" +
                    "\"recipeInstructions\": [{\n" +
                    "  \"@type\": \"HowToStep\",\n" +
                    "  \"text\": \"Finely dice the celery, carrot and onion, then finely grate the\n" +
                    "     garlic.\"\n" +
                    "}]\n" +
                    "}\n" +
                    "</script></body></html>";

        var text = await ExtractAsync(html);

        text.Should().NotBeNull();
        text.Should().Contain("Recipe Name: Wrapped Text Recipe");
        text.Should().Contain("Finely dice the celery, carrot and onion, then finely grate the garlic.");
    }
}
