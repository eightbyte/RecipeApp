using System.Text.Json.Nodes;
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
        var schema = JsonNode.Parse(RecipeSchemaJson)!;
        var gbnf   = JsonSchemaGrammar.ToGbnf(schema);

        gbnf.Should().Contain("\\\"name\\\"");
        gbnf.Should().Contain("\\\"servings\\\"");
        gbnf.Should().Contain("\\\"ingredients\\\"");
        gbnf.Should().Contain("\\\"steps\\\"");
        gbnf.Should().NotBeNullOrWhiteSpace();
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

    private const string RecipeSchemaJson = """
        {
          "type": "object",
          "properties": {
            "name":        { "type": "string" },
            "description": { "type": ["string","null"] },
            "servings":    { "type": "integer" },
            "ingredients": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name":         { "type": "string" },
                  "display_name": { "type": "string" },
                  "amount":       { "type": "number" },
                  "unit":         { "type": "string" },
                  "notes":        { "type": ["string","null"] }
                },
                "required": ["name","display_name","amount","unit"]
              }
            },
            "steps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "step_number":        { "type": "integer" },
                  "instruction":        { "type": "string" },
                  "ingredient_indexes": { "type": "array", "items": { "type": "integer" } }
                },
                "required": ["step_number","instruction","ingredient_indexes"]
              }
            }
          },
          "required": ["name","servings","ingredients","steps"]
        }
        """;

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
