using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Stage 3 (Phase 9 §10): one cached MyPlate page becomes one <see cref="ParsedSeedRecipe"/>.
/// Deterministic, offline, and cheap — this is the stage boundary that lets the expensive LLM pass
/// be re-run without re-fetching, and re-tuned without re-parsing.
///
/// <para>The selectors below are structural facts about a retired Drupal site whose markup is
/// frozen in the archive, so they are constants rather than configuration: there is no operational
/// scenario in which someone should be re-pointing them, and the counts in each comment are
/// measurements taken across the harvested corpus rather than guesses.</para>
/// </summary>
public class MyPlateRecipeParser(IOptions<RecipeSeedingOptions> options)
{
    /// <summary>
    /// The page's own content root. Everything is scoped beneath it, so the Wayback toolbar the
    /// archive injects into every page can never satisfy a selector (§10.3 hazard 4). Measured:
    /// present on all 1,089 pages, and no recipe field class occurs outside it or more than once.
    /// </summary>
    private const string ContentRootSelector = ".mp-recipe-full";

    /// <summary>
    /// Full description, in preference to the JSON-LD one. Measured: present on all 1,089 pages,
    /// and on 286 of them the JSON-LD description is a truncated prefix of this (§10.3 hazard 1).
    /// </summary>
    private const string DescriptionSelector = ".mp-recipe-full__description";

    /// <summary>
    /// The two Drupal ingredient field classes in the corpus — 1,024 primary, 65 legacy. Selecting
    /// only the first, as §10.1 originally did, would fail the legacy pages for no reason. Neither
    /// class name is a substring of the other, and no page carries both.
    /// </summary>
    private const string PrimaryIngredientSelector = ".field--name-field-mp-ingredients";
    private const string LegacyIngredientSelector  = ".field--name-field-ingredients";

    /// <summary>Common to both templates.</summary>
    private const string InstructionsSelector = ".field--name-field-instructions";
    private const string NotesSelector        = ".field--name-field-notes";
    private const string SourceSelector       = ".field--name-field-source";

    /// <summary>
    /// Drupal's wrapper around a field's value, as opposed to its <c>&lt;h2&gt;</c> label or, for
    /// the source field, its inline "Source:" caption. Selecting it is what keeps those labels out
    /// of the extracted text without string-stripping them afterwards.
    /// </summary>
    private const string FieldValueSelector = ".field__item";

    /// <summary>The parenthetical prep note Drupal renders beside an ingredient, e.g. "(chopped)".</summary>
    private const string IngredientNoteSelector = "span.notes";

    /// <summary>Leading integer of a <c>recipeYield</c> string — <c>"4 Servings"</c>, <c>"12"</c>,
    /// <c>"6 Muffins"</c>. Measured: matches all 1,089 pages.</summary>
    private static readonly Regex ServingsPattern = new(@"^\s*(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// The archive's snapshot prefix. Every absolute URL on a captured page is rewritten through
    /// it, so a URL treated as canonical without unwrapping points at web.archive.org rather than
    /// at the resource (§10.3 hazard 3).
    /// </summary>
    private static readonly Regex SnapshotPrefixPattern = new(
        @"^(?:https?://web\.archive\.org)?/web/\d{14}[a-z_]*/(?<target>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Drupal emits these throughout; every one becomes an ordinary space (§10.3 hazard 2).</summary>
    private const char NonBreakingSpace = '\u00A0';

    private static readonly Regex WhitespaceRunPattern = new(@"\s+", RegexOptions.Compiled);

    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>
    /// Parses one cached page.
    /// </summary>
    /// <param name="html">The archived page, verbatim from <c>raw/{slug}.html</c>.</param>
    /// <param name="slug">Cache key, used as the recipe's identity.</param>
    /// <param name="originalUrl">
    /// The canonical <c>myplate.gov</c> URL from the manifest — never the archive's.
    /// </param>
    /// <exception cref="SeedParseException">
    /// The page has no content root, no name, no ingredient block or no instruction block (§10.4).
    /// The stage runner catches this per slug; it never aborts a run.
    /// </exception>
    public ParsedSeedRecipe Parse(string html, string slug, string originalUrl)
    {
        var document = new HtmlParser().ParseDocument(html);

        var root = document.QuerySelector(ContentRootSelector)
            ?? throw new SeedParseException(SeedParseFailure.NoContentRoot,
                $"'{slug}' has no {ContentRootSelector} element — it is not a recipe page.");

        // JSON-LD lives in <head>, outside the content root, so it is read from the document.
        // It carries name, description, recipeYield and image, but never recipeIngredient or
        // recipeInstructions on either template — those only exist as markup.
        var jsonLd = RecipeJsonLdExtractor.TryFindRecipeNode(document);

        var name = ReadJsonLdText(jsonLd?["name"])
                   ?? Clean(root.QuerySelector("h1")?.TextContent)
                   ?? throw new SeedParseException(SeedParseFailure.NoName,
                       $"'{slug}' has neither a JSON-LD recipe name nor a heading.");

        var (ingredientField, template) = FindIngredientField(root, slug);
        var ingredients = ReadIngredients(ingredientField);
        if (ingredients.Count == 0)
            throw new SeedParseException(SeedParseFailure.NoIngredients,
                $"'{slug}' has an ingredient block containing no usable items.");

        var instructionField = root.QuerySelector(InstructionsSelector)
            ?? throw new SeedParseException(SeedParseFailure.NoInstructionBlock,
                $"'{slug}' has no {InstructionsSelector} element.");

        var (steps, trailingAside) = ReadInstructions(instructionField);
        if (steps.Count == 0)
            throw new SeedParseException(SeedParseFailure.NoSteps,
                $"'{slug}' has an instruction block containing no steps.");

        var sourceYield = ReadJsonLdText(jsonLd?["recipeYield"]);

        return new ParsedSeedRecipe
        {
            Slug         = slug,
            SourceUrl    = originalUrl,
            Template     = template,
            Name         = name,
            Description  = ReadDescription(root, jsonLd),
            Servings     = ParseServings(sourceYield),
            SourceYield  = sourceYield,
            ImageUrl     = UnwrapArchiveUrl(ReadImageUrl(jsonLd)),
            Notes        = JoinParagraphs(ReadFieldValue(root, NotesSelector), trailingAside),
            SourceCredit = ReadFieldValue(root, SourceSelector),
            Ingredients  = ingredients,
            Steps        = steps,
        };
    }

    // ── Ingredients (§10.2) ───────────────────────────────────────────────────

    private static (IElement Field, MyPlateTemplate Template) FindIngredientField(IElement root, string slug)
    {
        if (root.QuerySelector(PrimaryIngredientSelector) is { } primary)
            return (primary, MyPlateTemplate.Primary);

        if (root.QuerySelector(LegacyIngredientSelector) is { } legacy)
            return (legacy, MyPlateTemplate.Legacy);

        throw new SeedParseException(SeedParseFailure.NoIngredientBlock,
            $"'{slug}' has neither {PrimaryIngredientSelector} nor {LegacyIngredientSelector}.");
    }

    /// <summary>
    /// One entry per list item, with the prep note split off the way the markup already splits it.
    /// Quantities are left in the text on purpose — Stage 4 extracts them.
    /// </summary>
    private static List<ParsedIngredientLine> ReadIngredients(IElement field)
    {
        // Selecting list items rather than walking the field skips Drupal's <h2>Ingredients</h2>
        // label without having to recognise and remove it.
        var lines = new List<ParsedIngredientLine>();

        foreach (var item in TopLevelListItems(field))
        {
            var noteElement = item.QuerySelector(IngredientNoteSelector);
            var note        = Clean(noteElement?.TextContent);

            // Removing the note from the item is what stops it being read twice. The document is
            // discarded once parsing finishes, so mutating it costs nothing.
            noteElement?.Remove();

            var text = Clean(item.TextContent);
            if (text is null) continue;

            // A wholly emboldened item ending in a colon is a group heading — "For the Dressing:",
            // "Topping:" — not something to buy. Measured: 86 such items on 80 pages. The rule is
            // deliberately narrow; bolded items that are not colon-terminated ("aluminum foil
            // (10x12 inches square)") are real listed items and are kept.
            if (IsGroupHeading(item, text)) continue;

            lines.Add(new ParsedIngredientLine(text, note));
        }

        return lines;
    }

    private static bool IsGroupHeading(IElement item, string text)
    {
        if (!text.EndsWith(':')) return false;

        var children = item.Children;
        return children.Length == 1
               && children[0].LocalName is "b" or "strong"
               && Clean(children[0].TextContent) == text;
    }

    // ── Instructions (§10.3 hazard 5) ─────────────────────────────────────────

    /// <summary>
    /// Ordered steps, plus any aside that trailed them.
    ///
    /// <para>Measured across the corpus: every one of the 1,089 pages renders its directions as an
    /// <c>&lt;ol&gt;</c>, so the steps are already segmented and there is nothing to guess at. Two
    /// shapes complicate that:</para>
    ///
    /// <list type="bullet">
    /// <item>28 pages have more than one list, split by a section label ("Icing:", "Make
    /// Dumplings:"). Taking only the first list would silently drop the rest of the recipe, so
    /// every list contributes, and a label is folded onto the step it introduces rather than
    /// becoming a contentless step of its own.</item>
    /// <item>30 pages carry content after the last list — a footnoted <c>*</c> aside, a "Storage:"
    /// paragraph, a variation. It is a note, not a step. §10.3 predicted a <c>*</c> marker to split
    /// on, but only 11 of the 30 have one; the structural rule (nothing after the last list is a
    /// step) covers all of them and needs no marker.</item>
    /// </list>
    /// </summary>
    private static (List<string> Steps, string? TrailingAside) ReadInstructions(IElement field)
    {
        var body = field.QuerySelector(FieldValueSelector) ?? field;

        // On every page in the corpus the step lists are direct children of the field's value
        // wrapper, which is what makes the before/between/after walk below meaningful. If a page
        // ever nests them a level deeper, re-scope to the list's own parent rather than silently
        // falling through to the single-blob path.
        if (!body.Children.Any(child => child.LocalName == "ol")
            && body.QuerySelector("ol")?.ParentElement is { } listParent)
        {
            body = listParent;
        }

        var steps    = new List<string>();
        var asides   = new List<string>();
        var lastList = body.Children.LastOrDefault(child => child.LocalName == "ol");

        // No list at all: the whole field is one block of prose. Not seen in this corpus, but the
        // parser should degrade rather than lose a recipe if the archive ever serves one.
        if (lastList is null)
        {
            var prose = Clean(body.TextContent);
            return (prose is null ? [] : [prose], null);
        }

        var pendingLabel = default(string);
        var afterSteps   = false;

        foreach (var child in body.Children)
        {
            if (child.LocalName == "ol")
            {
                foreach (var item in DirectListItems(child))
                {
                    var step = Clean(item.TextContent);
                    if (step is null) continue;

                    steps.Add(pendingLabel is null ? step : $"{pendingLabel} {step}");
                    pendingLabel = null;
                }

                if (child == lastList) afterSteps = true;
                continue;
            }

            // Drupal's <h2>Directions</h2> label, when the field has no .field__item wrapper.
            if (child.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6") continue;

            var text = Clean(child.TextContent);
            if (text is null) continue;

            if (afterSteps)
                asides.Add(text);
            else if (text.EndsWith(':'))
                pendingLabel = pendingLabel is null ? text : $"{pendingLabel} {text}";
            else
                steps.Add(text);
        }

        return (steps, asides.Count == 0 ? null : string.Join("\n\n", asides));
    }

    // ── Scalar fields ─────────────────────────────────────────────────────────

    /// <summary>
    /// The page's own description, falling back to the JSON-LD one. HTML first because JSON-LD
    /// truncates: on 286 pages it is a "…"-suffixed prefix of this, and on the rest the two agree.
    /// </summary>
    private static string? ReadDescription(IElement root, JsonNode? jsonLd) =>
        Clean(root.QuerySelector(DescriptionSelector)?.TextContent)
        ?? ReadJsonLdText(jsonLd?["description"]);

    /// <summary>The value of a Drupal field, without its label.</summary>
    private static string? ReadFieldValue(IElement root, string fieldSelector)
    {
        var field = root.QuerySelector(fieldSelector);
        if (field is null) return null;

        var values = field.QuerySelectorAll(FieldValueSelector);
        return values.Length == 0
            ? Clean(field.TextContent)
            : JoinParagraphs(values.Select(value => Clean(value.TextContent)).ToArray());
    }

    /// <summary>
    /// Serving count from the published yield, or the configured default when the page states none.
    /// The yield is a count of servings, not a serving size — the legacy template's
    /// <c>field-recipe-serving-size</c> ("1/4 of recipe") is a different quantity and is not a
    /// fallback for this.
    /// </summary>
    private int ParseServings(string? recipeYield)
    {
        if (recipeYield is not null
            && ServingsPattern.Match(recipeYield) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, out var servings)
            && servings > 0)
        {
            return servings;
        }

        return _options.DefaultServings;
    }

    /// <summary>Reads <c>image.url</c> from the JSON-LD Recipe node, in any of the shapes it takes.</summary>
    private static string? ReadImageUrl(JsonNode? jsonLd) => jsonLd?["image"] switch
    {
        JsonValue value when value.TryGetValue<string>(out var url) => url,
        JsonObject image => ReadJsonLdText(image["url"]),
        JsonArray images => images
            .Select(image => image switch
            {
                JsonValue value when value.TryGetValue<string>(out var url) => url,
                JsonObject candidate => ReadJsonLdText(candidate["url"]),
                _ => null,
            })
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
        _ => null,
    };

    // ── List walking ──────────────────────────────────────────────────────────

    /// <summary>
    /// The list's own items, ignoring any belonging to a list nested inside one of them.
    ///
    /// <para>This is not a tidiness measure. A nested list's text is already part of its parent
    /// item's text, so collecting descendants would emit that content twice — measured on
    /// <c>black-bean-and-couscous-salad</c>, whose sub-list of preparation tasks appeared once
    /// inside step 2 and again as steps 3 to 6.</para>
    /// </summary>
    private static IEnumerable<IElement> DirectListItems(IElement list) =>
        list.Children.Where(child => child.LocalName == "li");

    /// <summary>
    /// Items of every list under <paramref name="container"/> that is not itself nested inside a
    /// list item, in document order. Used where the lists sit below a wrapper rather than being
    /// walked directly — an ingredient field holds its <c>&lt;ul&gt;</c> beneath the field label,
    /// and a few pages split ingredients across more than one list.
    /// </summary>
    private static IEnumerable<IElement> TopLevelListItems(IElement container) =>
        container.QuerySelectorAll("ul, ol")
            .Where(list => !IsInsideListItem(list, container))
            .SelectMany(DirectListItems);

    private static bool IsInsideListItem(IElement element, IElement container)
    {
        for (var ancestor = element.ParentElement;
             ancestor is not null && ancestor != container;
             ancestor = ancestor.ParentElement)
        {
            if (ancestor.LocalName == "li") return true;
        }

        return false;
    }

    // ── Text handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Strips the archive's snapshot prefix from a URL, leaving the resource it captured
    /// (§10.3 hazard 3). A URL that carries no prefix is returned unchanged.
    /// </summary>
    internal static string? UnwrapArchiveUrl(string? url)
    {
        if (url is null) return null;

        var match = SnapshotPrefixPattern.Match(url);
        return match.Success ? match.Groups["target"].Value : url;
    }

    private static string? ReadJsonLdText(JsonNode? node)
    {
        var raw = node switch
        {
            null => null,
            // recipeYield is occasionally published as ["8", "8 servings"].
            JsonArray array => array.Count > 0 ? ReadJsonLdText(array[0]) : null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonValue => node.ToString(),
            _ => null,
        };

        return Clean(raw);
    }

    /// <summary>
    /// Collapses whitespace and normalises the non-breaking spaces Drupal emits, returning null
    /// for anything that holds no text.
    ///
    /// <para>There is deliberately no <c>U+FFFD</c> stripping here. §10.3 hazard 2 predicted
    /// replacement characters from mis-decoded archival, but the whole corpus is clean UTF-8: zero
    /// U+FFFD across all 1,089 pages, and zero undecoded entities. The observation behind the
    /// hazard — <c>"165 degrees F&lt;?&gt;(3-5 minutes)"</c> — is a literal <c>&amp;nbsp;</c> in
    /// the markup, which this handles. Stripping logic for a hazard the corpus does not contain
    /// would be untestable against real input and is not written.</para>
    /// </summary>
    private static string? Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        var normalised = WhitespaceRunPattern.Replace(raw.Replace(NonBreakingSpace, ' '), " ").Trim();
        return normalised.Length == 0 ? null : normalised;
    }

    private static string? JoinParagraphs(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return present.Length == 0 ? null : string.Join("\n\n", present);
    }
}
