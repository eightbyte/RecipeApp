using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>
/// The parts of the local client that can be exercised without weights.
///
/// <para>All of this exists because of one measured failure: a decode came back unparseable several
/// thousand bytes in, and the only thing recorded was the parser's complaint about a character.
/// Which character, in what context, and therefore which grammar rule had a hole in it, were all
/// discarded at the moment they mattered.</para>
/// </summary>
public class LLamaSharpStructuredClientTests
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

        var excerpt = LLamaSharpStructuredClient.Excerpt(json, FailureFor(json));

        excerpt.Should().Contain("1x", "the excerpt exists to name the text that broke the parse");
    }

    /// <summary>
    /// A real answer is thousands of bytes long and the useful part is a few characters wide, so the
    /// excerpt is a window rather than the whole thing.
    /// </summary>
    [Fact]
    public void Excerpt_WindowsALongAnswerRatherThanReturningAllOfIt()
    {
        var padding = new string('a', 4000);
        var json    = $$"""{"note":"{{padding}}","amount":1x}""";

        var excerpt = LLamaSharpStructuredClient.Excerpt(json, FailureFor(json));

        excerpt.Length.Should().BeLessThan(json.Length / 4);
        excerpt.Should().Contain("1x");
    }

    /// <summary>
    /// The parser counts bytes and the excerpt indexes characters. Any non-ASCII text ahead of the
    /// failure puts the reported position past the end of the string, and a diagnostic that throws
    /// while reporting a failure replaces a useful error with a useless one.
    /// </summary>
    [Fact]
    public void Excerpt_DoesNotThrowWhenTheReportedPositionRunsPastTheText()
    {
        var json = $$"""{"name":"{{new string('é', 40)}}","amount":1x}""";

        var act = () => LLamaSharpStructuredClient.Excerpt(json, FailureFor(json));

        act.Should().NotThrow();
    }

    [Fact]
    public void Excerpt_SaysSoWhenTheModelReturnedNothingAtAll()
    {
        LLamaSharpStructuredClient.Excerpt(string.Empty, FailureFor("")).Should()
            .Be("(empty answer)");
    }
}
