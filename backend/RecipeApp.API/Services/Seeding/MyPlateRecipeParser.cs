using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Stage 3: one cached MyPlate page becomes one <see cref="ParsedSeedRecipe"/>, deterministically and offline so the LLM pass can be re-run without re-parsing.</summary>
public class MyPlateRecipeParser(IOptions<RecipeSeedingOptions> options)
{
    /// <summary>The page's own content root, scoping every selector beneath it so the Wayback toolbar can never satisfy one.</summary>
    private const string ContentRootSelector = ".mp-recipe-full";

    /// <summary>Full description, preferred over the JSON-LD one because that is sometimes a truncated prefix of it.</summary>
    private const string DescriptionSelector = ".mp-recipe-full__description";

    /// <summary>The two Drupal ingredient field classes in the corpus, a primary and a legacy one, neither a substring of the other.</summary>
    private const string PrimaryIngredientSelector = ".field--name-field-mp-ingredients";
    private const string LegacyIngredientSelector  = ".field--name-field-ingredients";

    /// <summary>Common to both templates.</summary>
    private const string InstructionsSelector = ".field--name-field-instructions";
    private const string NotesSelector        = ".field--name-field-notes";
    private const string SourceSelector       = ".field--name-field-source";

    /// <summary>Drupal's wrapper around a field's value, which keeps the <c>&lt;h2&gt;</c> label and "Source:" caption out of the extracted text.</summary>
    private const string FieldValueSelector = ".field__item";

    /// <summary>The parenthetical prep note Drupal renders beside an ingredient, e.g. "(chopped)".</summary>
    private const string IngredientNoteSelector = "span.notes";

    /// <summary>Leading integer of a <c>recipeYield</c> string such as <c>"4 Servings"</c> or <c>"12"</c>.</summary>
    private static readonly Regex ServingsPattern = new(@"^\s*(\d+)", RegexOptions.Compiled);

    /// <summary>The archive's snapshot prefix, which rewrites every absolute URL on a captured page and so must be unwrapped to reach the resource.</summary>
    private static readonly Regex SnapshotPrefixPattern = new(
        @"^(?:https?://web\.archive\.org)?/web/\d{14}[a-z_]*/(?<target>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Drupal emits these throughout; every one becomes an ordinary space.</summary>
    private const char NonBreakingSpace = '\u00A0';

    private static readonly Regex WhitespaceRunPattern = new(@"\s+", RegexOptions.Compiled);

    private readonly RecipeSeedingOptions _options = options.Value;

    /// <summary>Parses one cached page.</summary>
    /// <param name="originalUrl">The canonical <c>myplate.gov</c> URL from the manifest, never the archive's.</param>
    /// <exception cref="SeedParseException">The page has no content root, name, ingredient block or instruction block.</exception>
    public ParsedSeedRecipe Parse(string html, string slug, string originalUrl)
    {
        var document = new HtmlParser().ParseDocument(html);

        var root = document.QuerySelector(ContentRootSelector)
            ?? throw new SeedParseException(SeedParseFailure.NoContentRoot,
                $"'{slug}' has no {ContentRootSelector} element — it is not a recipe page.");

        // JSON-LD lives in <head> outside the content root and never carries the ingredients or instructions, which exist only as markup.
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

    // ── Ingredients ───────────────────────────────────────────────────────────

    private static (IElement Field, MyPlateTemplate Template) FindIngredientField(IElement root, string slug)
    {
        if (root.QuerySelector(PrimaryIngredientSelector) is { } primary)
            return (primary, MyPlateTemplate.Primary);

        if (root.QuerySelector(LegacyIngredientSelector) is { } legacy)
            return (legacy, MyPlateTemplate.Legacy);

        throw new SeedParseException(SeedParseFailure.NoIngredientBlock,
            $"'{slug}' has neither {PrimaryIngredientSelector} nor {LegacyIngredientSelector}.");
    }

    /// <summary>One entry per list item, with the prep note split off the way the markup already splits it and quantities left in the text for Stage 4.</summary>
    private static List<ParsedIngredientLine> ReadIngredients(IElement field)
    {
        // Selecting list items rather than walking the field skips Drupal's <h2>Ingredients</h2> label.
        var lines = new List<ParsedIngredientLine>();

        foreach (var item in TopLevelListItems(field))
        {
            var noteElement = item.QuerySelector(IngredientNoteSelector);
            var note        = Clean(noteElement?.TextContent);

            // Removing the note from the item stops it being read twice.
            noteElement?.Remove();

            var text = Clean(item.TextContent);
            if (text is null) continue;

            // A wholly emboldened item ending in a colon is a group heading such as "For the Dressing:", not something to buy.
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

    // ── Instructions ──────────────────────────────────────────────────────────

    /// <summary>Ordered steps from every list on the page, plus any aside that trails the last list, which is a note and not a step.</summary>
    private static (List<string> Steps, string? TrailingAside) ReadInstructions(IElement field)
    {
        var body = field.QuerySelector(FieldValueSelector) ?? field;

        // The step lists are direct children of the field's value wrapper on every page, which the before/between/after walk below relies on.
        if (!body.Children.Any(child => child.LocalName == "ol")
            && body.QuerySelector("ol")?.ParentElement is { } listParent)
        {
            body = listParent;
        }

        var steps    = new List<string>();
        var asides   = new List<string>();
        var lastList = body.Children.LastOrDefault(child => child.LocalName == "ol");

        // With no list at all the whole field is one block of prose, so degrade rather than lose the recipe.
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

    /// <summary>The page's own description, falling back to the JSON-LD one because JSON-LD sometimes truncates.</summary>
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

    /// <summary>Serving count from the published yield, or the configured default when the page states none, since a serving size is a different quantity.</summary>
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

    /// <summary>The list's own items, ignoring any in a nested list because the parent item's text already contains them.</summary>
    private static IEnumerable<IElement> DirectListItems(IElement list) =>
        list.Children.Where(child => child.LocalName == "li");

    /// <summary>Items of every list under <paramref name="container"/> that is not itself nested inside a list item, in document order.</summary>
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

    /// <summary>Strips the archive's snapshot prefix from a URL to leave the resource it captured, returning an unprefixed URL unchanged.</summary>
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

    /// <summary>Collapses whitespace and normalises the non-breaking spaces Drupal emits, returning null for anything that holds no text.</summary>
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
