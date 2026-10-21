using System.Text;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Phase 9.3 §5's MERGE list as a closed vocabulary: the only ways two corpus names may differ and
/// still be one catalogue entry. The grouping pass keeps a merge the model proposed only where the
/// names differ by nothing else.
///
/// <para><b>Why a table and not a better prompt.</b> §5's MERGE list is already closed — singular
/// and plural, fat/sodium/sugar variants, cut and size descriptors, redundant freshness qualifiers,
/// and regional spellings (<see cref="RegionalIngredientSpellings"/>). The model was told all of it,
/// twice over, and still reliably read "shares a final word" as "same purchase": on the benchmark
/// batches it put lasagna, fettuccine and ramen into <c>egg noodles</c>, dijon and spicy brown into
/// <c>mustard</c>, and fourteen breakfast cereals into <c>cereal</c>, even with a rule naming that
/// exact shape. Measured across 245 hand-labelled names: the best prompt wrongly merged 57; the same
/// answers refined by this table wrongly merged none (§12.1).</para>
///
/// <para><b>It refines, it never proposes.</b> Two names that differ only by these words are kept
/// together only if the model <i>also</i> put them together, so the judgement §2.3 said no mechanical
/// rule can make — <c>diced tomatoes</c> is canned, <c>tomatoes</c> may not be — is still the
/// model's. The mechanical fold of §4.3 still only routes. What the table adds is a veto, and every
/// veto is in the over-split direction §5's tie-break already chose.</para>
///
/// <para><b>What is deliberately absent</b>, each measured as a real false merge it would admit:
/// <c>ground</c> (<c>ginger</c> ← <c>ground ginger</c>), <c>dried</c> (<c>cranberries</c> ←
/// <c>dried cranberries</c>), <c>white</c> (<c>beans</c> ← <c>white beans</c>), <c>clove</c> (the
/// spice), <c>cooked</c> (§5 keeps cooked and raw apart) and <c>canned</c>/<c>frozen</c>
/// (preservation state). The cost is visible and correctable — <c>ground cinnamon</c> is its own
/// entry beside <c>cinnamon</c> — which is the trade §5 makes on purpose.</para>
/// </summary>
public static class CatalogueMergeVocabulary
{
    /// <summary>Phrases rewritten before anything else, where a word is a merge word only in context.</summary>
    /// <remarks><c>whole milk</c> is a fat variant of milk (Phase 9.3 §11 Q1); <c>whole</c> alone is
    /// not — <c>whole chicken</c>, <c>whole tomatoes</c>. <c>head of</c> is a unit of sale.</remarks>
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

    /// <summary>
    /// Whether two names differ only by merge vocabulary — the precondition for a model-proposed
    /// merge to stand.
    /// </summary>
    public static bool DifferOnlyByMergeVocabulary(string first, string second) =>
        Core(first).SetEquals(Core(second));

    /// <summary>
    /// What a name is once bracketed asides, punctuation, merge vocabulary and plural endings are set
    /// aside. <c>boneless, skinless chicken breasts</c> and <c>chicken breast</c> share a core;
    /// <c>lasagna noodles</c> and <c>egg noodles</c> do not.
    /// </summary>
    internal static IReadOnlySet<string> Core(string name)
    {
        var words = Tokenise(name);
        var core  = words.Where(word => !Words.Contains(word)).Select(Singular).ToHashSet(StringComparer.Ordinal);

        // A name made only of merge words — 'chopped' — keeps them, or every such name would share
        // the empty core and any two of them could merge.
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

    /// <summary>
    /// A comparison form, not a dictionary singular: it only has to map a word and its plural to the
    /// same string. <c>chili</c> and <c>chilies</c> both become <c>chily</c>, which is wrong English and
    /// right for equality.
    /// </summary>
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
