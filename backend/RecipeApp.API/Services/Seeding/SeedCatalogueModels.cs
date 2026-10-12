using System.Text.Json.Serialization;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

// ── seed-data/ingredient-catalogue.json (the shipped, curated catalogue) ──────

/// <summary>
/// The shipped ingredient catalogue (Phase 10 §4.7, D8) — what an ingredient <i>is</i>. First
/// derived from the MyPlate corpus by <c>--build-catalogue</c>, then curated by hand, and now
/// edited only by hand. Every install seeds from it, so it carries no MyPlate provenance.
///
/// <para><b>Why this is an artefact and not a runtime step.</b> Phase 9 §12 routed catalogue
/// matching through a per-recipe LLM call whose candidate list grew as the run proceeded, so the
/// same ingredient name could resolve differently depending on where its recipe sat in the
/// manifest. A committed catalogue looked up by dictionary makes the import deterministic on every
/// machine.</para>
///
/// <para>It carries no density: <see cref="Data.IngredientDensitySeeder"/> is the single source of
/// truth for that, curated and cited, and two sources would be one too many. It carries nothing
/// recipe-specific either — how much of an ingredient a recipe uses is the recipe's business.</para>
///
/// <para><b>Two versions, two jobs.</b> <see cref="FormatVersion"/> is the file's shape, and the
/// loader refuses one it cannot read. <see cref="ContentVersion"/> is raised on every curated edit
/// and never refused: a content edit must not stop the loader reading the file.</para>
/// </summary>
public record IngredientCatalogueFile
{
    /// <summary>The only file shape this build reads.</summary>
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Raised on every curated edit. Recorded in the seed ledger (Phase 10 D7).</summary>
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
    /// <summary>Reviewed and edited by hand (Phase 10 §4.7).</summary>
    public const string Curated = "curated";
}

/// <summary>
/// One catalogue entry: a canonical ingredient plus every other name that resolves to it.
/// </summary>
/// <param name="Name">Normalised lowercase name — becomes <c>Ingredient.Name</c>.</param>
/// <param name="DisplayName">Human-readable form — becomes <c>Ingredient.DisplayName</c>.</param>
/// <param name="Category">One of <see cref="IngredientCategory.All"/>.</param>
/// <param name="DefaultUnit">Canonical storable unit, or null when none is typical.</param>
/// <param name="Aliases">
/// Names this entry absorbs: corpus names, folded entries and regional spellings. Normalised
/// lowercase, never equal to any entry's <paramref name="Name"/>, never shared with another entry
/// (§4.7 rule 4).
/// </param>
public record IngredientCatalogueEntry(
    string Name,
    string DisplayName,
    string Category,
    string? DefaultUnit,
    IReadOnlyList<string> Aliases);

// ── seed-data/myplate/catalogue-build.json (Stage 4.5 output) ─────────────────

/// <summary>
/// What <c>--build-catalogue</c> derives from the normalised MyPlate corpus (Phase 9.3 §4.2): a
/// catalogue in the original version-1 shape, with the corpus counts a reviewer needs.
///
/// <para><b>A reference, never the shipped file.</b> Phase 10 §4.7 moved this output away from
/// <c>ingredient-catalogue.json</c>, because a re-run would otherwise overwrite the curated
/// catalogue. It lives in the gitignored MyPlate cache and is read only by a person doing a later
/// curation pass.</para>
/// </summary>
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

/// <param name="Corpus">Corpus identifier, e.g. <c>myplate</c>.</param>
/// <param name="Recipes">Normalised recipes read.</param>
/// <param name="IngredientRows">Ingredient rows across those recipes.</param>
/// <param name="DistinctNames">Distinct ingredient names before grouping.</param>
public record CatalogueSource(
    string Corpus,
    int Recipes,
    int IngredientRows,
    int DistinctNames);

/// <summary>
/// One derived entry: a catalogue entry plus the corpus evidence behind it.
/// </summary>
/// <param name="Name">Normalised lowercase name, named after the group's most-used member.</param>
/// <param name="DisplayName">Human-readable form.</param>
/// <param name="Category">One of <see cref="IngredientCategory.All"/>.</param>
/// <param name="DefaultUnit">
/// Modal unit over the merged group's rows, or null when no row in the group states one. Derived
/// from the corpus rather than asked for: it is stated 8,882 times, and a model's guess would
/// replace observations with an opinion (Phase 9.3 §2.5, §4.4).
/// </param>
/// <param name="Aliases">Corpus names this entry absorbs, plus any regional spelling.</param>
/// <param name="CorpusRows">Ingredient rows backing this entry — how much evidence it rests on.</param>
/// <param name="CorpusRecipes">Distinct recipes those rows came from.</param>
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

/// <summary>
/// Everything the deterministic collect pass knows about a single distinct ingredient name
/// (§4.3 pass 1). Built without an LLM or a database, and the only evidence the later passes use.
/// </summary>
public record CorpusIngredientName
{
    public required string Name { get; init; }

    /// <summary>Rows carrying this exact name.</summary>
    public required int Rows { get; init; }

    /// <summary>
    /// Slugs of the recipes those rows came from. Kept as the set rather than a count so a merged
    /// group's recipe total can be a union — summing counts would double-count a recipe that uses
    /// two names from the same group, and <c>tomatoes</c> beside <c>diced tomatoes</c> is exactly
    /// that shape.
    /// </summary>
    public required IReadOnlyCollection<string> RecipeSlugs { get; init; }

    /// <summary>Distinct recipes those rows came from.</summary>
    public int Recipes => RecipeSlugs.Count;

    /// <summary>Storable unit → row count. The null key counts rows the source left unquantified.</summary>
    public required IReadOnlyDictionary<string, int> UnitCounts { get; init; }

    /// <summary>Rows whose source line stated no quantity (Phase 9.1's population).</summary>
    public required int UnquantifiedRows { get; init; }

    /// <summary>Observed display names → row count, used to pick a display form when the model offers none.</summary>
    public required IReadOnlyDictionary<string, int> DisplayNameCounts { get; init; }

    /// <summary>
    /// Up to <see cref="IngredientCatalogueBuilder.SampleLinesPerName"/> verbatim source lines.
    ///
    /// <para>Not decoration. <c>diced tomatoes</c> is only identifiable as canned from its line —
    /// all 32 read <c>1 can (14.5 ounces) … diced tomatoes</c> — and <c>cranberries</c> covers both
    /// <c>2 cups fresh or frozen cranberries</c> and <c>1 cup dried cranberries</c>, which are
    /// different purchases. The name alone is not sufficient evidence to merge on (§4.3).</para>
    /// </summary>
    public required IReadOnlyList<string> SampleLines { get; init; }
}

// ── Pass 2: what the model answered ───────────────────────────────────────────

/// <summary>
/// One group assembled from the model's per-name answers, before the deterministic passes finish it.
///
/// <para>There is no name here, deliberately. The model is never asked for a canonical name: the
/// entry is named after its most-used member, derived the same way the default unit is. A name the
/// model proposed could be some other batch's corpus name — one string both an entry here and an
/// alias there, §4.7 rule 4's exact subject — and no single batch can see that.</para>
/// </summary>
/// <param name="DisplayName">
/// The model's display form for the member the entry will be named after, or null to fall back to
/// the corpus modal form.
/// </param>
/// <param name="Category">Category, already validated against <see cref="IngredientCategory"/>.</param>
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

    /// <summary>
    /// Names the model never returned, folded in as single-name entries rather than dropped.
    /// A dropped name is a recipe Stage 5 cannot persist, so a gap in the answer is repaired, and
    /// reported so it can be reviewed.
    /// </summary>
    public int NamesRecoveredFromGaps { get; init; }

    /// <summary>Written file path, for the closing log line.</summary>
    public string? Path { get; init; }

    /// <summary>Validation errors. Non-empty means nothing was written.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    public bool Succeeded => ValidationErrors.Count == 0;
}

/// <summary>
/// Outcome of a <c>--build-catalogue --batch</c> preview: the grouping pass run on named batches
/// only, with every merge logged and nothing written.
/// </summary>
/// <param name="TotalBatches">Batches a full build would run.</param>
/// <param name="BatchesRun">Named batches that exist and were run.</param>
/// <param name="Names">Names across the batches run.</param>
/// <param name="Groups">Groups those names formed.</param>
/// <param name="LlmCalls">Model calls, retries included.</param>
public record CataloguePreviewResult(int TotalBatches, int BatchesRun, int Names, int Groups, int LlmCalls);

/// <summary>
/// The catalogue contradicts itself, or the corpus it claims to describe is not the one on disk.
/// Thrown rather than logged: a catalogue nobody can trust is worse than no catalogue, and every
/// consumer downstream treats it as authoritative (§4.7).
/// </summary>
public class CatalogueValidationException(IReadOnlyList<string> errors)
    : Exception($"The ingredient catalogue is not valid:\n  - {string.Join("\n  - ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

// ── Regional spellings ────────────────────────────────────────────────────────

/// <summary>
/// Alternate spellings for entries the corpus names in American English, seeded into the build
/// rather than discovered by it.
///
/// <para>Phase 9.3 §2.2 measured the problem: of the 41 hand-written starter names, six never occur
/// in the corpus at all, and three of those are British/Australian spellings of things the corpus
/// does have — it says <c>bell pepper</c>, not <c>capsicum</c>; <c>all-purpose flour</c>, not
/// <c>plain flour</c>; <c>beef broth</c>, not <c>beef stock</c>. Seeded as they stood, both
/// spellings sat in the catalogue as separate rows and never consolidated in a shopping list.</para>
///
/// <para>These are corpus-absent by definition, so §4.7 rule 5 cannot surface them and the build
/// pass has no evidence from which to invent them. They are stated here, reviewed as code, and
/// attached as aliases — so both spellings keep working and only one row exists (§4.9).</para>
/// </summary>
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

/// <summary>
/// The grouping answer as the model returns it, in the schema's snake_case: one object per labelled
/// name. Kept separate from <see cref="CatalogueGroup"/> because the two shapes genuinely differ —
/// groups are assembled from these answers, never returned by the model.
/// </summary>
internal record CatalogueGroupingResponse(
    [property: JsonPropertyName("names")] List<CatalogueNameResponse>? Names);

internal record CatalogueNameResponse(
    [property: JsonPropertyName("label")] int? Label,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("same_as")] int? SameAs,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("category")] string? Category);
