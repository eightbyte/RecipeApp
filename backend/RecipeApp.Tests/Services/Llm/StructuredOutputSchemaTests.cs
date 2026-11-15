using System.Text.Json.Nodes;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The schemas were written for the GBNF converter, so each one is fitted to the JSON Schema subset Claude's structured outputs accept before it is sent.</summary>
public class StructuredOutputSchemaTests
{
    [Fact]
    public void EveryObject_GetsAdditionalPropertiesFalse_BecauseStructuredOutputsRejectsAnObjectWithout()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "items": { "type": "array", "items": { "type": "object", "properties": { "a": { "type": "string" } } } }
              }
            }
            """)!;

        var fitted = StructuredOutputSchema.Fit(schema);

        fitted["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        fitted["properties"]!["items"]!["items"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void ANullableTypeArray_BecomesAnyOf_KeepingItsDescriptionBesideIt()
    {
        var schema = JsonNode.Parse("""
            { "type": ["number", "null"], "description": "Null when unstated." }
            """)!;

        var fitted = StructuredOutputSchema.Fit(schema);

        fitted.ToJsonString().Should().Be(
            """{"description":"Null when unstated.","anyOf":[{"type":"number"},{"type":"null"}]}""");
    }

    [Fact]
    public void TheCallersSchema_IsLeftUnchanged()
    {
        var schema = JsonNode.Parse("""{ "type": "object", "properties": { "a": { "type": ["string", "null"] } } }""")!;
        var before = schema.ToJsonString();

        StructuredOutputSchema.Fit(schema);

        schema.ToJsonString().Should().Be(before);
    }

    /// <summary>The real Scrape schema, walked node by node, because one missed object anywhere is a 400 on every Scrape.</summary>
    [Fact]
    public void TheScrapeSchema_FitsTheSubset_WithNoTypeArrayAndNoOpenObjectLeft()
    {
        var fitted = StructuredOutputSchema.Fit(JsonNode.Parse(RecipeScrapeService.ScrapeSchemaJson)!);

        foreach (var node in Descendants(fitted).OfType<JsonObject>())
        {
            (node["type"] is JsonArray).Should().BeFalse("type arrays are rewritten as anyOf");
            if (node["type"]?.GetValue<string>() == "object")
                node["additionalProperties"]?.GetValue<bool>().Should().BeFalse();
        }

        fitted["properties"]!["steps"]!["items"]!["properties"]!["stated_amounts"]!["items"]!["additionalProperties"]!
            .GetValue<bool>().Should().BeFalse();
    }

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject obj => obj.Select(p => p.Value),
            JsonArray arr  => arr.AsEnumerable(),
            _              => [],
        };
        foreach (var child in children.OfType<JsonNode>())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }
}
