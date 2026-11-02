using RecipeApp.Tests.Infrastructure;
using System.Text.RegularExpressions;

namespace RecipeApp.Tests.Conventions;

/// <summary>Enforces the CODING_STANDARDS.md rule that comments never cite a phase, spec section or decision number, across backend and frontend source.</summary>
public partial class CommentCitationTests
{
    private static readonly string[] SourceExtensions = [".cs", ".js", ".vue"];

    private static readonly string[] SkippedDirectories = ["bin", "obj", "Migrations", "node_modules", "dist"];

    private static readonly string[] ScannedRoots =
    [
        Path.Combine("backend", "RecipeApp.API"),
        Path.Combine("backend", "RecipeApp.Tests"),
        Path.Combine("frontend", "src"),
    ];

    // The lookbehind skips "//" inside URLs such as https://.
    [GeneratedRegex(@"(?<![:""'\w])//+(?<text>.*)$|/\*(?<text>[\s\S]*?)\*/|<!--(?<text>[\s\S]*?)-->", RegexOptions.Multiline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\b[Pp]hase\s*\d|\bADR[\s-]*\d|§|\b[Ss]pec(ification)?\s+section\b|\b[Dd]ecision\s+#?\d")]
    private static partial Regex Citation();

    private static IEnumerable<string> CitingComments(string source) =>
        Comment().Matches(source)
            .Select(match => match.Groups["text"].Value.Trim())
            .Where(text => Citation().IsMatch(text));

    [Theory]
    [InlineData("// Phase 3 added this")]
    [InlineData("/// <summary>Chosen in ADR 0001.</summary>")]
    [InlineData("/* see §4.2 */")]
    [InlineData("<!-- per spec section 5 -->")]
    [InlineData("var x = 1; // decision #12")]
    public void Detector_FlagsACitingComment(string source)
    {
        CitingComments(source).Should().ContainSingle();
    }

    [Theory]
    [InlineData("var url = \"https://example.com/phase-3\";")]
    [InlineData("// Stage B resolves cups against density.")]
    [InlineData("const phase = 3")]
    public void Detector_IgnoresCodeAndOrdinaryComments(string source)
    {
        CitingComments(source).Should().BeEmpty();
    }

    [Fact]
    public void NoSourceComment_CitesAPhaseSpecSectionOrDecision()
    {
        var thisFile = $"{nameof(CommentCitationTests)}.cs";

        var sourceFiles = ScannedRoots
            .Select(root => Path.Combine(RepositoryPaths.RepositoryRoot, root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(SkippedDirectories.Contains))
            .Where(path => Path.GetFileName(path) != thisFile)
            .ToList();

        sourceFiles.Should().Contain(path => path.EndsWith(".vue"), "the frontend source is scanned too");

        var citations =
            from path in sourceFiles
            from comment in CitingComments(File.ReadAllText(path))
            select $"{Path.GetRelativePath(RepositoryPaths.RepositoryRoot, path)}: {comment}";

        citations.Should().BeEmpty("comments say why, and a phase or decision number goes stale");
    }
}
