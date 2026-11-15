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

    public static TheoryData<string> ScrapingSchemas =>
        new(RecipeScrapeService.ScrapeSchemaJson, RecipeScrapeService.MatchingSchemaJson);

    /// <summary>Each real Scraping schema, walked node by node, because one missed object anywhere is a 400 on every Scrape.</summary>
    [Theory]
    [MemberData(nameof(ScrapingSchemas))]
    public void EveryScrapingSchema_FitsTheSubset_WithNoTypeArrayAndNoOpenObjectLeft(string schemaJson)
    {
        var fitted = StructuredOutputSchema.Fit(JsonNode.Parse(schemaJson)!);

        var objects = Descendants(fitted).OfType<JsonObject>().ToList();
        objects.Should().NotContain(node => node["type"] is JsonArray, "type arrays are rewritten as anyOf");
        objects.Where(node => node["type"] is JsonValue && node["type"]!.GetValue<string>() == "object")
            .Should().OnlyContain(node => node["additionalProperties"] is JsonValue
                                          && node["additionalProperties"]!.GetValue<bool>() == false);
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
