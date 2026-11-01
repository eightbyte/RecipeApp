using System.Text.Json.Serialization;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

// ── seed-data/ingredient-catalogue.json (the shipped, curated catalogue) ──────

/// <summary>
/// The shipped ingredient catalogue, hand-curated and seeded on every install so ingredient matching is a deterministic
/// dictionary lookup; it carries no densities and no corpus provenance.
/// </summary>
public record IngredientCatalogueFile
{
    /// <summary>The only file shape this build reads.</summary>
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Raised on every curated edit and recorded in the seed ledger.</summary>
    public required int ContentVersion { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>How the content was produced, e.g. <see cref="CatalogueProvenance.Curated"/>.</summary>
    public required string Provenance { get; init; }

    /// <summary>Ordered by <see cref="IngredientCatalogueEntry.Name"/> so an edit diffs cleanly.</summary>
    public required IReadOnlyList<IngredientCatalogueEntry> Entries { get; init; }
}

/// <summary>Values for <see cref="IngredientCatalogueFile.Provenance"/>.</summary>
public static class CatalogueProvenance
{
    /// <summary>Reviewed and edited by hand.</summary>
    public const string Curated = "curated";
}

/// <summary>One catalogue entry: a canonical ingredient plus every other name that resolves to it.</summary>
/// <param name="Aliases">Normalised lowercase names this entry absorbs, never equal to any entry's name and never shared with another entry.</param>
public record IngredientCatalogueEntry(
    string Name,
    string DisplayName,
    string Category,
    string? DefaultUnit,
    IReadOnlyList<string> Aliases);

// ── seed-data/myplate/catalogue-build.json (Stage 4.5 output) ─────────────────

/// <summary>What <c>--build-catalogue</c> derives from the normalised corpus, with the counts a reviewer needs; a reference and never the shipped file.</summary>
public record CatalogueBuildFile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public required DateTime GeneratedAt { get; init; }

    /// <summary>Which corpus this was derived from, and how much of it — provenance for a reviewer.</summary>
    public required CatalogueSource Source { get; init; }

    /// <summary>Ordered by <see cref="CatalogueBuildEntry.Name"/> so a regenerated file diffs cleanly.</summary>
    public required IReadOnlyList<CatalogueBuildEntry> Entries { get; init; }
}

public record CatalogueSource(
    string Corpus,
    int Recipes,
    int IngredientRows,
    int DistinctNames);

/// <summary>One derived entry: a catalogue entry plus the corpus evidence behind it.</summary>
/// <param name="DefaultUnit">Modal unit over the merged group's rows, or null when no row in the group states one.</param>
public record CatalogueBuildEntry(
    string Name,
    string DisplayName,
    string Category,
    string? DefaultUnit,
    IReadOnlyList<string> Aliases,
    int CorpusRows,
    int CorpusRecipes)
{
    /// <summary>The entry without its corpus evidence, for the rules every catalogue must meet.</summary>
    public IngredientCatalogueEntry ToCatalogueEntry() =>
        new(Name, DisplayName, Category, DefaultUnit, Aliases);
}

// ── Pass 1: what the corpus says about one name ───────────────────────────────

/// <summary>Everything the deterministic collect pass knows about one distinct ingredient name, and the only evidence the later passes use.</summary>
public record CorpusIngredientName
{
    public required string Name { get; init; }

    /// <summary>Rows carrying this exact name.</summary>
    public required int Rows { get; init; }

    /// <summary>Slugs of the recipes those rows came from, kept as a set so a merged group's recipe total is a union rather than a double-counting sum.</summary>
    public required IReadOnlyCollection<string> RecipeSlugs { get; init; }

    /// <summary>Distinct recipes those rows came from.</summary>
    public int Recipes => RecipeSlugs.Count;

    /// <summary>Storable unit → row count. The null key counts rows the source left unquantified.</summary>
    public required IReadOnlyDictionary<string, int> UnitCounts { get; init; }

    /// <summary>Rows whose source line stated no quantity.</summary>
    public required int UnquantifiedRows { get; init; }

    /// <summary>Observed display names → row count, used to pick a display form when the model offers none.</summary>
    public required IReadOnlyDictionary<string, int> DisplayNameCounts { get; init; }

    /// <summary>Up to <see cref="IngredientCatalogueBuilder.SampleLinesPerName"/> verbatim source lines, because the name alone is not enough evidence to merge on.</summary>
    public required IReadOnlyList<string> SampleLines { get; init; }
}

// ── Pass 2: what the model answered ───────────────────────────────────────────

/// <summary>One group assembled from the model's per-name answers, with no name because the entry is named after its most-used member.</summary>
/// <param name="DisplayName">The model's display form for the member the entry will be named after, or null to use the corpus modal form.</param>
/// <param name="Members">Corpus names this group absorbs, most-used first.</param>
public record CatalogueGroup(
    string? DisplayName,
    string Category,
    IReadOnlyList<string> Members);

// ── Results ───────────────────────────────────────────────────────────────────

/// <summary>Outcome of a <c>--build-catalogue</c> run.</summary>
public record CatalogueBuildResult
{
    /// <summary>Normalised recipes read.</summary>
    public int Recipes { get; init; }

    /// <summary>Ingredient rows across them.</summary>
    public int IngredientRows { get; init; }

    /// <summary>Distinct names before grouping.</summary>
    public int DistinctNames { get; init; }

    /// <summary>Entries written.</summary>
    public int Entries { get; init; }

    /// <summary>Batched model calls the run made.</summary>
    public int LlmCalls { get; init; }

    /// <summary>Names the model never returned, folded in as single-name entries because a dropped name is a recipe Stage 5 cannot persist.</summary>
    public int NamesRecoveredFromGaps { get; init; }

    /// <summary>Written file path, for the closing log line.</summary>
    public string? Path { get; init; }

    /// <summary>Validation errors. Non-empty means nothing was written.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    public bool Succeeded => ValidationErrors.Count == 0;
}

/// <summary>Outcome of a <c>--build-catalogue --batch</c> preview: the grouping pass run on named batches only, with every merge logged and nothing written.</summary>
public record CataloguePreviewResult(int TotalBatches, int BatchesRun, int Names, int Groups, int LlmCalls);

/// <summary>The catalogue contradicts itself or the corpus it claims to describe, thrown rather than logged because every consumer treats it as authoritative.</summary>
public class CatalogueValidationException(IReadOnlyList<string> errors)
    : Exception($"The ingredient catalogue is not valid:\n  - {string.Join("\n  - ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

// ── Regional spellings ────────────────────────────────────────────────────────

/// <summary>Alternate spellings for entries the corpus names in American English, attached as aliases so both spellings resolve to one row.</summary>
public static class RegionalIngredientSpellings
{
    /// <summary>Corpus name → alternate spellings that should resolve to it.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByCorpusName =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["bell pepper"]       = ["capsicum", "capsicums"],
            ["all-purpose flour"] = ["plain flour"],
            ["beef broth"]        = ["beef stock"],
            ["chicken broth"]     = ["chicken stock"],
            ["vegetable broth"]   = ["vegetable stock"],
            ["cilantro"]          = ["coriander leaves", "fresh coriander"],
            ["zucchini"]          = ["courgette", "courgettes"],
            ["eggplant"]          = ["aubergine", "aubergines"],
            ["green onion"]       = ["spring onion", "spring onions"],
            ["powdered sugar"]    = ["icing sugar"],
            ["cornstarch"]        = ["cornflour"],
            ["shrimp"]            = ["prawns", "prawn"],
            ["ground beef"]       = ["beef mince", "minced beef"],
            ["arugula"]           = ["rocket"],
            ["heavy cream"]       = ["double cream"],
            ["oatmeal"]           = ["porridge oats"],
            ["molasses"]          = ["treacle"],
            ["beet"]              = ["beetroot"],
            ["chickpeas"]         = ["garbanzo beans", "garbanzo bean"],
            ["baking soda"]       = ["bicarbonate of soda", "bicarb soda"],
        };
}

// ── JSON naming ───────────────────────────────────────────────────────────────

/// <summary>The grouping answer as the model returns it, in the schema's snake_case; groups are assembled from these answers and never returned by the model.</summary>
internal record CatalogueGroupingResponse(
    [property: JsonPropertyName("names")] List<CatalogueNameResponse>? Names);

internal record CatalogueNameResponse(
    [property: JsonPropertyName("label")] int? Label,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("same_as")] int? SameAs,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("category")] string? Category);
