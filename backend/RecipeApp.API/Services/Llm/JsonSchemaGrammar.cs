using System.Text;
using System.Text.Json.Nodes;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Converts a JSON Schema subset to a GBNF grammar string for grammar-constrained decoding.
/// Supports objects, arrays, string/integer/number/boolean primitives, nullable unions, enums, and required fields.
/// </summary>
public static class JsonSchemaGrammar
{
    /// <summary>
    /// The JSON primitives, as JSON actually defines them.
    ///
    /// <para><b>The leading-zero alternation is load-bearing.</b> A digit run written as
    /// <c>[0-9]+</c> admits <c>00</c> and <c>012</c>, which are not JSON — every parser rejects
    /// them, and <c>JsonNode.Parse</c> reports "'0' is an invalid end of a number" from somewhere
    /// deep in the answer. The whole point of constrained decoding is that unparseable output is
    /// unreachable, so a grammar looser than the format it is guarding is a defect in the guard.
    /// Phase 9 Stage 4 lost a recipe to it: the model emitted a leading zero several thousand bytes
    /// into an otherwise well-formed answer, and the decode was thrown away as invalid.</para>
    /// </summary>
    private const string PrimitiveRules = """
        ws      ::= " "?
        string  ::= "\"" ([^"\\\x7F\x00-\x1F] | "\\" (["\\/bfnrt] | "u" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]))* "\""
        integer ::= "-"? ("0" | [1-9] [0-9]*)
        number  ::= "-"? ("0" | [1-9] [0-9]*) ("." [0-9]+)? ([eE] [+-]? [0-9]+)?
        boolean ::= "true" | "false"
        null    ::= "null"
        """;

    public static string ToGbnf(JsonNode schema)
    {
        var ruleList = new List<(string Name, string Body)>();
        var ruleSet  = new HashSet<string>(StringComparer.Ordinal);

        BuildRule(schema, "root", ruleList, ruleSet);

        // Ensure root appears first; primitives last
        var rootEntry = ruleList.FirstOrDefault(r => r.Name == "root");
        var others    = ruleList.Where(r => r.Name != "root").ToList();

        var sb = new StringBuilder();
        if (rootEntry != default)
            sb.AppendLine($"root ::= {rootEntry.Body}");
        foreach (var (name, body) in others)
            sb.AppendLine($"{name} ::= {body}");
        sb.AppendLine();
        sb.AppendLine(PrimitiveRules);
        return sb.ToString();
    }

    private static string BuildRule(
        JsonNode schema,
        string ruleName,
        List<(string, string)> rules,
        HashSet<string> ruleSet)
    {
        if (ruleSet.Contains(ruleName)) return ruleName;

        // A closed set of values: exactly one of the listed JSON literals, and nothing else.
        //
        // Phase 9.3 added this because a prompt instruction is not a constraint. Told that category
        // "MUST be exactly one value from this list", the model answered SPICES and CONDIMENT — each
        // valid JSON, each a string, so the grammar admitted it and a keyword-table guess replaced
        // the judgement the call existed to get. Constrained decoding exists to make invalid output
        // unreachable; a rule the grammar could state but does not is a hole in the guard.
        if (schema["enum"] is JsonArray allowed && allowed.Count > 0)
        {
            var literals = allowed.Select(value => GbnfLiteral(value?.ToJsonString() ?? "null"));
            AddRule(ruleName, "(" + string.Join(" | ", literals) + ")", rules, ruleSet);
            return ruleName;
        }

        var typeNode = schema["type"];

        // Nullable union: "type": ["T", "null"]
        if (typeNode is JsonArray typeArr)
        {
            var types = typeArr.Select(t => t!.GetValue<string>()).ToList();
            var baseType = types.FirstOrDefault(t => t != "null");
            var hasNull  = types.Contains("null");

            var baseRef = baseType != null ? ResolvePrimitive(baseType) : "string";
            var body    = hasNull ? $"({baseRef} | null)" : baseRef;
            AddRule(ruleName, body, rules, ruleSet);
            return ruleName;
        }

        var type = typeNode?.GetValue<string>() ?? "object";

        switch (type)
        {
            case "object":
                BuildObjectRule(schema, ruleName, rules, ruleSet);
                break;
            case "array":
                BuildArrayRule(schema, ruleName, rules, ruleSet);
                break;
            default:
                AddRule(ruleName, ResolvePrimitive(type), rules, ruleSet);
                break;
        }

        return ruleName;
    }

    private static void BuildObjectRule(
        JsonNode schema,
        string ruleName,
        List<(string, string)> rules,
        HashSet<string> ruleSet)
    {
        var props    = schema["properties"]?.AsObject();
        var reqArray = schema["required"]?.AsArray();
        var required = reqArray?
            .Select(r => r!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal) ?? [];

        if (props == null || props.Count == 0)
        {
            AddRule(ruleName, "\"{}\"", rules, ruleSet);
            return;
        }

        // Reserve the name before recursing to break cycles
        ruleSet.Add(ruleName);

        var parts = new List<string>();
        foreach (var (key, propSchema) in props)
        {
            if (!required.Contains(key)) continue;

            var childName = $"{ruleName}-{Slug(key)}";
            BuildRule(propSchema!, childName, rules, ruleSet);
            parts.Add($"{GbnfJsonKey(key)} ws \":\" ws {childName}");
        }

        var body = parts.Count == 0
            ? "\"{}\""
            : "\"{\" ws " + string.Join(" \",\" ws ", parts) + " ws \"}\"";

        // Add after children so their rules appear first
        rules.Add((ruleName, body));
    }

    private static void BuildArrayRule(
        JsonNode schema,
        string ruleName,
        List<(string, string)> rules,
        HashSet<string> ruleSet)
    {
        ruleSet.Add(ruleName);

        var items    = schema["items"];
        var itemName = $"{ruleName}-item";

        if (items != null)
            BuildRule(items, itemName, rules, ruleSet);
        else
            AddRule(itemName, "string", rules, ruleSet);

        rules.Add((ruleName, $"\"[\" ws ({itemName} (\",\" ws {itemName})*)? ws \"]\""));
    }

    private static string ResolvePrimitive(string type) => type switch
    {
        "string"  => "string",
        "integer" => "integer",
        "number"  => "number",
        "boolean" => "boolean",
        "null"    => "null",
        _         => "string",
    };

    // Produces a GBNF string literal that matches the JSON key "key" (with surrounding quotes)
    private static string GbnfJsonKey(string key)
    {
        var escaped = key.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "\"\\\"" + escaped + "\\\"\"";
    }

    // Produces a GBNF string literal that matches the given text verbatim — for an enum value, its
    // JSON form, so "PRODUCE" matches with its quotes and 3 matches without any
    private static string GbnfLiteral(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Slug(string key) =>
        key.Replace("_", "-").ToLowerInvariant();

    private static void AddRule(
        string name, string body,
        List<(string, string)> rules,
        HashSet<string> ruleSet)
    {
        if (ruleSet.Add(name))
            rules.Add((name, body));
    }
}
