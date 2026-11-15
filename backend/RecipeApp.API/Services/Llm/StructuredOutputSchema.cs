using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

/// <summary>Fits a schema written for <see cref="JsonSchemaGrammar"/> to the subset Claude's structured outputs accept, which closes every object and has no type arrays.</summary>
public static class StructuredOutputSchema
{
    /// <summary>A fitted copy, leaving <paramref name="schema"/> as it was.</summary>
    public static JsonNode Fit(JsonNode schema) => FitNode(schema.DeepClone());

    private static JsonNode FitNode(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is { } item) array[i] = FitNode(item.DeepClone());
                return array;

            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                    if (obj[name] is { } child) obj[name] = FitNode(child.DeepClone());

                if (obj["type"] is JsonArray types)
                    return WithTypesAsAnyOf(obj, types);

                if (obj["type"]?.GetValue<string>() == "object")
                    obj["additionalProperties"] = false;

                return obj;

            default:
                return node;
        }
    }

    private static JsonObject WithTypesAsAnyOf(JsonObject obj, JsonArray types)
    {
        var fitted = new JsonObject();
        foreach (var (name, value) in obj)
            if (name != "type") fitted[name] = value?.DeepClone();

        fitted["anyOf"] = new JsonArray(types
            .Select(type => (JsonNode)new JsonObject { ["type"] = type?.DeepClone() })
            .ToArray());

        return fitted;
    }
}
