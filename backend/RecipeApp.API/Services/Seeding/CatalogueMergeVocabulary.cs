using System.Text;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The closed vocabulary of ways two corpus names may differ and still be one catalogue entry, applied as a veto on a
/// model-proposed merge because the model alone reliably over-merged names that share a final word.
/// </summary>
public static class CatalogueMergeVocabulary
{
    /// <summary>Phrases rewritten before anything else, where a word is a merge word only in context, such as <c>whole milk</c> but not <c>whole chicken</c>.</summary>
    public static readonly IReadOnlyList<(string Phrase, string Replacement)> Rewrites =
    [
        ("whole milk", "milk"),
        ("head of",    ""),
    ];

    /// <summary>Multi-word fat, sodium and sugar variants. Hyphens are spaces by the time these are matched.</summary>
    public static readonly IReadOnlyList<string> Phrases =
    [
        "no salt added", "no sugar added",
        "low sodium", "reduced sodium", "salt free",
        "low fat", "reduced fat", "fat free", "non fat", "part skim", "extra lean",
        "low sugar", "reduced sugar", "sugar free",
    ];

    /// <summary>Single words that never change what is bought.</summary>
    public static readonly IReadOnlySet<string> Words = new HashSet<string>(StringComparer.Ordinal)
    {
        // Fat, sodium and sugar variants.
        "nonfat", "skim", "light", "lite", "lean", "unsalted", "unsweetened", "1%", "2%", "100%",
        // Size.
        "large", "medium", "small",
        // Cut and trim.
        "chopped", "diced", "sliced", "shredded", "minced", "grated", "cubed", "crushed", "halved",
        "boneless", "skinless",
        // Redundant freshness qualifiers.
        "fresh", "ripe",
    };

    /// <summary>Spellings of one word the corpus uses interchangeably.</summary>
    public static readonly IReadOnlyDictionary<string, string> Spellings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filet"] = "fillet",
            ["chile"] = "chili",
            ["purée"] = "puree",
        };

    /// <summary>Whether two names differ only by merge vocabulary, the precondition for a model-proposed merge to stand.</summary>
    public static bool DifferOnlyByMergeVocabulary(string first, string second) =>
        Core(first).SetEquals(Core(second));

    /// <summary>What a name is once bracketed asides, punctuation, merge vocabulary and plural endings are set aside, so <c>boneless, skinless chicken breasts</c> and <c>chicken breast</c> share a core.</summary>
    internal static IReadOnlySet<string> Core(string name)
    {
        var words = Tokenise(name);
        var core  = words.Where(word => !Words.Contains(word)).Select(Singular).ToHashSet(StringComparer.Ordinal);

        // A name made only of merge words keeps them, or every such name would share the empty core and any two could merge.
        return core.Count > 0 ? core : words.Select(Singular).ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> Tokenise(string name)
    {
        var text  = new StringBuilder(" ");
        var depth = 0;

        foreach (var character in name.ToLowerInvariant())
        {
            if (character == '(') { depth++; continue; }
            if (character == ')') { depth = Math.Max(0, depth - 1); text.Append(' '); continue; }
            if (depth > 0) continue;

            text.Append(character is '-' or ',' or '/' || char.IsWhiteSpace(character) ? ' ' : character);
        }

        var spaced = " " + string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";

        foreach (var (phrase, replacement) in Rewrites)
            spaced = spaced.Replace($" {phrase} ", $" {replacement} ", StringComparison.Ordinal);

        foreach (var phrase in Phrases)
            spaced = spaced.Replace($" {phrase} ", " ", StringComparison.Ordinal);

        return spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>A comparison form rather than a dictionary singular, which only has to map a word and its plural to the same string.</summary>
    private static string Singular(string word)
    {
        word = Spellings.GetValueOrDefault(word, word);

        if (word.Length > 3 && word.EndsWith("ies", StringComparison.Ordinal))      word = word[..^3] + "y";
        else if (word.Length > 3 && word.EndsWith("ves", StringComparison.Ordinal)) word = word[..^3] + "f";
        else if (word.Length > 3 && word.EndsWith("oes", StringComparison.Ordinal)) word = word[..^2];
        else if (word.Length > 3 && (word.EndsWith("ches", StringComparison.Ordinal)
                                     || word.EndsWith("shes", StringComparison.Ordinal)
                                     || word.EndsWith("sses", StringComparison.Ordinal)
                                     || word.EndsWith("xes", StringComparison.Ordinal)))  word = word[..^2];
        else if (word.Length > 2 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)) word = word[..^1];

        word = Spellings.GetValueOrDefault(word, word);
        return word.EndsWith('i') ? word[..^1] + "y" : word;
    }
}
