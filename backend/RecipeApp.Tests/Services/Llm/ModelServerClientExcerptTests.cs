using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>The excerpt of an unparseable answer, which exists because an unparseable decode once discarded the context that mattered.</summary>
public class ModelServerClientExcerptTests
{
    /// <summary>Provokes a real parser failure, so the offsets under test are the parser's own.</summary>
    private static JsonException FailureFor(string json)
    {
        var act = () => JsonNode.Parse(json);
        return act.Should().Throw<JsonException>().Which;
    }

    [Fact]
    public void Excerpt_ShowsTheTextAroundTheCharacterTheParserRejected()
    {
        const string json = """{"amount":1x,"unit":"cup"}""";

        var excerpt = ModelServerClient.Excerpt(json, FailureFor(json));

        excerpt.Should().Contain("1x", "the excerpt exists to name the text that broke the parse");
    }

    /// <summary>A real answer is thousands of bytes long and the useful part is a few characters wide, so the excerpt is a window rather than the whole thing.</summary>
    [Fact]
    public void Excerpt_WindowsALongAnswerRatherThanReturningAllOfIt()
    {
        var padding = new string('a', 4000);
        var json    = $$"""{"note":"{{padding}}","amount":1x}""";

        var excerpt = ModelServerClient.Excerpt(json, FailureFor(json));

        excerpt.Length.Should().BeLessThan(json.Length / 4);
        excerpt.Should().Contain("1x");
    }

    /// <summary>The parser counts bytes and the excerpt indexes characters, so non-ASCII text ahead of a failure must not push the position past the end of the string.</summary>
    [Fact]
    public void Excerpt_DoesNotThrowWhenTheReportedPositionRunsPastTheText()
    {
        var json = $$"""{"name":"{{new string('é', 40)}}","amount":1x}""";

        var act = () => ModelServerClient.Excerpt(json, FailureFor(json));

        act.Should().NotThrow();
    }

    [Fact]
    public void Excerpt_SaysSoWhenTheModelReturnedNothingAtAll()
    {
        ModelServerClient.Excerpt(string.Empty, FailureFor("")).Should()
            .Be("(empty answer)");
    }
}
