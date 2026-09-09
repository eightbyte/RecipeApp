using System.Text.Json.Nodes;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

public class JsonSchemaGrammarTests
{
    // ── Basic type schemas ────────────────────────────────────────────────────

    [Fact]
    public void ToGbnf_ObjectWithRequiredStringField_ContainsFieldKey()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              },
              "required": ["name"]
            }
            """)!;

        var gbnf = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("root");
        gbnf.Should().Contain("\\\"name\\\"");
        gbnf.Should().Contain("string");
    }

    [Fact]
    public void ToGbnf_ObjectWithIntegerField_EmitsIntegerRef()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "count": { "type": "integer" }
              },
              "required": ["count"]
            }
            """)!;

        var gbnf = JsonSchemaGrammar.ToGbnf(schema);
        gbnf.Should().Contain("integer");
    }

    [Fact]
    public void ToGbnf_NullableStringField_EmitsNullableExpression()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "notes": { "type": ["string", "null"] }
              },
              "required": ["notes"]
            }
            """)!;

        var gbnf = JsonSchemaGrammar.ToGbnf(schema);
        gbnf.Should().Contain("null");
        gbnf.Should().Contain("string");
    }

    [Fact]
    public void ToGbnf_ArrayOfStrings_EmitsArrayRule()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "array",
              "items": { "type": "string" }
            }
            """)!;

        var gbnf = JsonSchemaGrammar.ToGbnf(schema);
        gbnf.Should().Contain("\"[\"");
        gbnf.Should().Contain("\"]\"");
        gbnf.Should().Contain("string");
    }

    [Fact]
    public void ToGbnf_ContainsPrimitiveRuleDefinitions()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{},"required":[]}""")!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("ws");
        gbnf.Should().Contain("string");
        gbnf.Should().Contain("integer");
        gbnf.Should().Contain("number");
        gbnf.Should().Contain("boolean");
        gbnf.Should().Contain("null");
    }

    // ── Realistic schemas used by the app ────────────────────────────────────

    [Fact]
    public void ToGbnf_RecipeExtractionSchema_ContainsTopLevelFields()
    {
        var schema = JsonNode.Parse(RecipeScrapeService.RecipeSchemaJson)!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("\\\"name\\\"");
        gbnf.Should().Contain("\\\"servings\\\"");
        gbnf.Should().Contain("\\\"ingredients\\\"");
        gbnf.Should().Contain("\\\"steps\\\"");
        gbnf.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Read from the live constant, not a copy: decoding is grammar-constrained, so if the
    /// service's schema ever declares <c>amount</c> as a bare number again the grammar will
    /// <i>force</i> the model to invent one for a line reading <c>salt</c> (Phase 9.1 §1.3).
    /// A copy in this file could drift back without anything noticing.
    /// </summary>
    [Fact]
    public void ToGbnf_LiveRecipeExtractionSchema_LetsTheModelStateAnAbsentQuantity()
    {
        var schema = JsonNode.Parse(RecipeScrapeService.RecipeSchemaJson)!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("(number | null)");
        gbnf.Should().Contain("(string | null)");
    }

    [Fact]
    public void LiveRecipeExtractionSchema_DeclaresAmountAndUnitNullableAndStillRequired()
    {
        var schema = JsonNode.Parse(RecipeScrapeService.RecipeSchemaJson)!;
        var ingredient = schema["properties"]!["ingredients"]!["items"]!;

        // Nullable, so "no quantity" is expressible...
        ingredient["properties"]!["amount"]!["type"]!.AsArray()
            .Select(t => t!.GetValue<string>()).Should().BeEquivalentTo("number", "null");
        ingredient["properties"]!["unit"]!["type"]!.AsArray()
            .Select(t => t!.GetValue<string>()).Should().BeEquivalentTo("string", "null");

        // ...and still required, so an absent quantity is an explicit assertion rather than a
        // key the model quietly omitted (Phase 9.1 §3.3).
        ingredient["required"]!.AsArray()
            .Select(t => t!.GetValue<string>()).Should().Contain(["amount", "unit"]);
    }

    [Fact]
    public void ToGbnf_MatchingSchema_ContainsResultsField()
    {
        var schema = JsonNode.Parse(MatchingSchemaJson)!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("\\\"results\\\"");
        gbnf.Should().Contain("\\\"scraped_index\\\"");
    }

    [Fact]
    public void ToGbnf_OutputIsNonEmpty_AndContainsRootRule()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"x":{"type":"string"}},"required":["x"]}""")!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().StartWith("root ::=");
    }

    // ── Schemas referenced from RecipeScrapeService ───────────────────────────

    // The recipe extraction schema is read from RecipeScrapeService.RecipeSchemaJson directly.
    // A local copy used to live here and had already drifted from the live constant by the time
    // Phase 9.1 made amount nullable — the copy still declared it a bare number, so these tests
    // would have kept passing while the grammar forced the model to invent a quantity.

    private const string MatchingSchemaJson = """
        {
          "type": "object",
          "properties": {
            "results": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "scraped_index":          { "type": "integer" },
                  "candidate_index":        { "type": ["integer","null"] },
                  "confidence":             { "type": "number" },
                  "suggested_category":     { "type": "string" },
                  "suggested_default_unit": { "type": ["string","null"] }
                },
                "required": ["scraped_index","candidate_index","confidence","suggested_category"]
              }
            }
          },
          "required": ["results"]
        }
        """;
}
