namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Which of the two Drupal content types a cached page was rendered from. Measured across the
/// harvested corpus: 1,024 primary and 65 legacy of 1,089 pages, with no page carrying both and
/// none carrying neither. Recorded per recipe so a template-specific extraction fault can be
/// traced back to the template rather than guessed at.
/// </summary>
public enum MyPlateTemplate
{
    /// <summary>Ingredients under <c>field--name-field-mp-ingredients</c>.</summary>
    Primary,

    /// <summary>Older content type: ingredients under <c>field--name-field-ingredients</c>.</summary>
    Legacy,
}

/// <summary>
/// Why a page could not be parsed. Reported per slug in the run summary and written to
/// <c>state.json</c>, because "1,089 pages, 3 failed" is only actionable if it also says how.
/// </summary>
public enum SeedParseFailure
{
    /// <summary>No <c>.mp-recipe-full</c> element — the page is not a recipe page at all.</summary>
    NoContentRoot,

    /// <summary>Neither the JSON-LD Recipe node nor a heading yielded a name.</summary>
    NoName,

    /// <summary>Neither ingredient field class is present (Phase 9 §10.1).</summary>
    NoIngredientBlock,

    /// <summary>The ingredient block exists but holds no usable items.</summary>
    NoIngredients,

    /// <summary>No <c>field--name-field-instructions</c> element.</summary>
    NoInstructionBlock,

    /// <summary>The instruction block exists but yielded no steps.</summary>
    NoSteps,
}

// ── parsed/{slug}.json (Stage 3 output) ───────────────────────────────────────

/// <summary>
/// One recipe as read off its cached page (Phase 9 §10) — deterministic, no LLM, and deliberately
/// lossless.
///
/// <para>Quantities are <b>not</b> parsed here. Vulgar fractions, container quantities
/// (<c>1 can (14.5 ounces)</c>) and descriptive counts (<c>2 medium celery stalks</c>) are exactly
/// what regex handles badly and the model handles well, so ingredient lines are carried through as
/// text and Stage 4 owns quantity extraction (§10.2).</para>
///
/// <para>Notes and the source credit are carried alongside the recipe rather than folded into it:
/// Stage 5 composes <c>Recipe.Description</c> from the description, the credit and the attribution
/// note, and cannot do that if Stage 3 has already merged them (§12).</para>
/// </summary>
public record ParsedSeedRecipe
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Cache key and filename stem.</summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The canonical <c>myplate.gov</c> URL, never the <c>web.archive.org</c> one — it is both the
    /// recipe's provenance and the idempotency key Stage 5 persists against (§12, §13).
    /// </summary>
    public required string SourceUrl { get; init; }

    public required MyPlateTemplate Template { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Full description. Taken from the page's own <c>.mp-recipe-full__description</c> in
    /// preference to JSON-LD, which truncates at ~150 characters on 286 of 1,089 pages (§10.3
    /// hazard 1).
    /// </summary>
    public string? Description { get; init; }

    public required int Servings { get; init; }

    /// <summary>
    /// The recipe photo's canonical URL with the archive's <c>/web/{timestamp}/</c> prefix removed
    /// (§10.3 hazard 3). Provenance only — the bytes were cached by Stage 2b, and Stage 5 reads
    /// them from the cache rather than from this URL.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>The <c>recipeYield</c> string as published, e.g. <c>"4 Servings"</c> — kept so a
    /// surprising <see cref="Servings"/> can be checked against what the page actually said.</summary>
    public string? SourceYield { get; init; }

    /// <summary>
    /// The recipe's notes field, plus any trailing aside that followed the last step list
    /// (§10.3 hazard 5).
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// The "Source:" credit, e.g. <c>"Recipe Adapted from: Food Hero, Oregon State University
    /// Cooperative Extension Service"</c>. These land-grant extension credits are provenance and
    /// are never discarded (§10.3 hazard 6).
    /// </summary>
    public string? SourceCredit { get; init; }

    public required IReadOnlyList<ParsedIngredientLine> Ingredients { get; init; }

    /// <summary>Ordered steps, taken from the page's own <c>&lt;ol&gt;</c> markup.</summary>
    public required IReadOnlyList<string> Steps { get; init; }
}

/// <summary>
/// One ingredient line, split the way the markup already splits it.
/// </summary>
/// <param name="Text">The item text, e.g. <c>"1 can (14.5 ounces) no salt added diced tomatoes"</c>.</param>
/// <param name="Note">
/// The parenthetical prep note Drupal renders in its own <c>span.notes</c>, e.g. <c>"(chopped)"</c>.
/// Present on 2,446 of 8,694 items; never more than one per item.
/// </param>
public record ParsedIngredientLine(string Text, string? Note);

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

/// <summary>
/// A page that cannot be turned into a recipe. Thrown by the parser and caught per slug by the
/// stage runner, which records it and moves on — Phase 9 §10.4: one bad page must not abort a
/// 1,000-recipe harvest.
/// </summary>
public class SeedParseException(SeedParseFailure failure, string message) : Exception(message)
{
    public SeedParseFailure Failure { get; } = failure;

    /// <summary>The reason string written to <c>state.json</c>, e.g. <c>"ParseFailed: NoIngredientBlock"</c>.</summary>
    public string StateReason => $"ParseFailed: {Failure}";
}
