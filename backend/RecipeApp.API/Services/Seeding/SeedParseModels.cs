using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Which of the two Drupal content types a cached page was rendered from, recorded per recipe so a template-specific fault can be traced.</summary>
public enum MyPlateTemplate
{
    /// <summary>Ingredients under <c>field--name-field-mp-ingredients</c>.</summary>
    Primary,

    /// <summary>Older content type: ingredients under <c>field--name-field-ingredients</c>.</summary>
    Legacy,
}

/// <summary>Why a page could not be parsed, reported per slug in the run summary and written to <c>state.json</c>.</summary>
public enum SeedParseFailure
{
    /// <summary>No <c>.mp-recipe-full</c> element — the page is not a recipe page at all.</summary>
    NoContentRoot,

    /// <summary>Neither the JSON-LD Recipe node nor a heading yielded a name.</summary>
    NoName,

    /// <summary>Neither ingredient field class is present.</summary>
    NoIngredientBlock,

    /// <summary>The ingredient block exists but holds no usable items.</summary>
    NoIngredients,

    /// <summary>No <c>field--name-field-instructions</c> element.</summary>
    NoInstructionBlock,

    /// <summary>The instruction block exists but yielded no steps.</summary>
    NoSteps,
}

// ── parsed/{slug}.json (Stage 3 output) ───────────────────────────────────────

/// <summary>One recipe as read off its cached page: deterministic, no LLM and lossless, with quantities left as text for Stage 4 and notes kept apart from the description.</summary>
public record ParsedSeedRecipe
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Cache key and filename stem.</summary>
    public required string Slug { get; init; }

    /// <summary>The canonical <c>myplate.gov</c> URL, never the archive's, serving as provenance and as Stage 5's idempotency key.</summary>
    public required string SourceUrl { get; init; }

    public required MyPlateTemplate Template { get; init; }

    public required string Name { get; init; }

    /// <summary>Full description from the page's own element, preferred over JSON-LD, which truncates.</summary>
    public string? Description { get; init; }

    public required int Servings { get; init; }

    /// <summary>The recipe photo's canonical URL without the archive prefix, kept as provenance since Stage 5 reads the cached bytes.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The <c>recipeYield</c> string as published, such as <c>"4 Servings"</c>, kept so a surprising <see cref="Servings"/> can be checked.</summary>
    public string? SourceYield { get; init; }

    /// <summary>The recipe's notes field, plus any trailing aside that followed the last step list.</summary>
    public string? Notes { get; init; }

    /// <summary>The "Source:" credit, which is provenance and never discarded.</summary>
    public string? SourceCredit { get; init; }

    public required IReadOnlyList<ParsedIngredientLine> Ingredients { get; init; }

    /// <summary>Ordered steps, taken from the page's own <c>&lt;ol&gt;</c> markup.</summary>
    public required IReadOnlyList<string> Steps { get; init; }
}

/// <summary>One ingredient line, split the way the markup already splits it, where <c>Note</c> is the parenthetical prep note Drupal renders in its own span.</summary>
public record ParsedIngredientLine(string Text, string? Note)
{
    /// <summary>The line as the page publishes it with both spans joined, which is what "the source line" means because some amounts live only in the note.</summary>
    [JsonIgnore]
    public string FullText =>
        string.IsNullOrWhiteSpace(Note) ? Text : $"{Text} {Note.Trim()}";
}

// ── Stage results ─────────────────────────────────────────────────────────────

/// <summary>Outcome of a Stage 3 run.</summary>
public record SeedParseResult
{
    /// <summary>Pages parsed during this run.</summary>
    public int Parsed { get; init; }

    /// <summary>Pages whose parsed output was already cached and left untouched.</summary>
    public int Skipped { get; init; }

    /// <summary>Manifest entries with no cached page yet — harvest has not reached them.</summary>
    public int NotHarvested { get; init; }

    /// <summary>Pages that could not be parsed. Each is isolated; one bad page never aborts a run.</summary>
    public int Failed { get; init; }

    public int PrimaryTemplate { get; init; }
    public int LegacyTemplate { get; init; }

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}

// ── Failures ──────────────────────────────────────────────────────────────────

/// <summary>A page that cannot be turned into a recipe, caught per slug by the stage runner so one bad page never aborts a harvest.</summary>
public class SeedParseException(SeedParseFailure failure, string message) : Exception(message)
{
    public SeedParseFailure Failure { get; } = failure;

    /// <summary>The reason string written to <c>state.json</c>, e.g. <c>"ParseFailed: NoIngredientBlock"</c>.</summary>
    public string StateReason => $"ParseFailed: {Failure}";
}
