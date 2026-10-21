namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Why a recipe was rejected by Stage 4. Every value describes something the model got wrong about
/// <i>this</i> recipe, so the recipe is retried and then excluded while the run continues (Phase 9
/// §16). A run that is broken rather than unlucky is reported by
/// <see cref="SeedNormaliseResult.Aborted"/> instead.
/// </summary>
public enum SeedNormaliseFailure
{
    /// <summary>The model returned a different number of ingredients than the page lists.</summary>
    IngredientCountMismatch,

    /// <summary>The model returned a different number of steps than the page lists.</summary>
    StepCountMismatch,

    /// <summary>Step numbers are not 1..N without gaps or repeats (Phase 9 §11.3).</summary>
    StepNumbersNotContiguous,

    /// <summary>An <c>ingredient_indexes</c> entry points outside the ingredients array.</summary>
    IngredientIndexOutOfRange,

    /// <summary>An ingredient came back with no name to match against the catalogue.</summary>
    MissingIngredientName,

    /// <summary>
    /// An ingredient failed <see cref="SeedQuantityGate"/> — the detail names the line and the
    /// <see cref="SeedQuantityRejection"/>.
    /// </summary>
    QuantityRejected,

    /// <summary>The parsed page yielded no usable serving count (Phase 9 §11.3).</summary>
    NonPositiveServings,

    /// <summary>The model produced output the extraction schema's own shape could not be read from.</summary>
    InvalidLlmOutput,

    /// <summary>The model did not answer within the configured budget.</summary>
    LlmTimeout,
}

/// <summary>
/// Tells the two kinds of Stage 4 failure apart, which is what the run-abort guard needs and did
/// not have.
/// </summary>
public static class SeedNormaliseFailureExtensions
{
    /// <summary>
    /// Whether this failure says inference itself is unavailable, rather than saying something
    /// about one recipe.
    ///
    /// <para>Only two values qualify. An unloaded model, an exhausted GPU and a broken grammar all
    /// surface as <see cref="SeedNormaliseFailure.InvalidLlmOutput"/> or
    /// <see cref="SeedNormaliseFailure.LlmTimeout"/>, because nothing came back that could be read.
    /// Every other value is a judgement about output that <i>did</i> come back: the model answered,
    /// the grammar held, and the gate read the answer and disagreed with it. A quantity rejection is
    /// therefore evidence that the pipeline is working, and it must never be counted as evidence
    /// that the pipeline is broken.</para>
    ///
    /// <para><b>Getting this wrong wedged a run completely.</b> The abort guard counted consecutive
    /// failures among attempted recipes, and cached successes are skipped before the counter is
    /// reached. On a resumed run every recipe it attempts below the high-water mark is one that
    /// already failed, and those failures reproduce — so the guard tripped on the first five of a
    /// thirty-recipe backlog and stopped at manifest position 77, never reaching the 744 recipes
    /// that had never been tried. Each re-run did exactly the same thing. Failing everything it
    /// attempts is the <i>expected</i> state of a resumed run, not a symptom.</para>
    /// </summary>
    public static bool IsSystemic(this SeedNormaliseFailure failure) =>
        failure is SeedNormaliseFailure.InvalidLlmOutput or SeedNormaliseFailure.LlmTimeout;
}

// ── normalised/{slug}.json (Stage 4 output) ───────────────────────────────────

/// <summary>
/// One recipe after the LLM pass (Phase 9 §11): quantities extracted from the ingredient lines and
/// each step linked to the ingredients it uses.
///
/// <para><b>The model owns less of this file than §11 anticipated.</b> Stage 3 established that
/// every page in the corpus already publishes an ordered step list (§22.1), so the name,
/// description, servings, image, notes, credit and every step's wording are copied from the parsed
/// page and the model's versions of them are discarded. Its output survives in exactly two places:
/// the ingredient rows, and each step's <see cref="NormalisedSeedStep.IngredientIndexes"/>. A model
/// that paraphrases a step therefore cannot corrupt one.</para>
///
/// <para>Deriving from the cache means Stage 5 never needs the LLM: deleting this file and
/// re-running is the prompt-iteration loop (§13), and the directory is a candidate for committing
/// so that no other developer or CI run needs a GPU (§18 Q1). That is why it carries the whole
/// recipe rather than referring back to <c>parsed/</c>.</para>
/// </summary>
public record NormalisedSeedRecipe
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Cache key and filename stem.</summary>
    public required string Slug { get; init; }

    /// <summary>The canonical <c>myplate.gov</c> URL — provenance, and Stage 5's idempotency key (§13).</summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Hash of the <c>parsed/</c> file this was derived from. A re-parse that changes the input
    /// invalidates this artefact, and the runner re-normalises it instead of silently keeping
    /// output derived from a page the parser no longer reads the same way.
    /// </summary>
    public required string ParsedFingerprint { get; init; }

    public required DateTime NormalisedAt { get; init; }

    /// <summary>LLM attempts this recipe took, including the successful one. Above 1 means the
    /// validation gate rejected an earlier answer — the signal that the prompt needs work.</summary>
    public required int Attempts { get; init; }

    // ── Copied from the parsed page, not from the model ───────────────────────

    public required string Name { get; init; }
    public string? Description { get; init; }
    public required int Servings { get; init; }
    public string? ImageUrl { get; init; }
    public string? Notes { get; init; }
    public string? SourceCredit { get; init; }

    public required IReadOnlyList<NormalisedSeedIngredient> Ingredients { get; init; }
    public required IReadOnlyList<NormalisedSeedStep> Steps { get; init; }
}

/// <summary>
/// One ingredient row, carrying both sides of the Stage A conversion so the arithmetic can be
/// audited from the cache alone — the same shape <c>RecipeIngredient</c> stores it in.
/// </summary>
/// <param name="Name">Catalogue-matching name: lowercase, singular, no quantity or preparation.</param>
/// <param name="DisplayName">Name as it should be shown.</param>
/// <param name="Amount">
/// Amount after <c>MeasurementConverter.ToCanonical</c>, as <see cref="SeedQuantityGate"/> admitted
/// it. Null when the source line states no quantity (Phase 9.1) — never zero, which would render as
/// <c>0 g Salt</c> and sum into shopping lists.
/// </param>
/// <param name="Unit">Canonical storable unit, or null exactly when <paramref name="Amount"/> is.</param>
/// <param name="SourceAmount">The amount the model read off the line, before conversion.</param>
/// <param name="SourceUnit">The unit as the model read it, e.g. <c>ounces</c>. Provenance only —
/// never summed, never consolidated (CLAUDE.md).</param>
/// <param name="Notes">Preparation and descriptive detail: <c>chopped</c>, <c>low-sodium</c>, <c>optional</c>.</param>
/// <param name="SourceText">
/// The verbatim ingredient line the model was given. Kept so the quantity gate's source-evidenced
/// half can be re-checked, and so a suspicious row can be read against what the page actually said,
/// without opening <c>parsed/</c>.
/// </param>
public record NormalisedSeedIngredient(
    string Name,
    string DisplayName,
    decimal? Amount,
    string? Unit,
    decimal? SourceAmount,
    string? SourceUnit,
    string? Notes,
    string SourceText);

/// <param name="StepNumber">1-based, contiguous.</param>
/// <param name="Instruction">The parsed page's own wording, not the model's retelling.</param>
/// <param name="IngredientIndexes">
/// 0-based positions in <see cref="NormalisedSeedRecipe.Ingredients"/> used by this step. The one
/// thing about steps only the model can supply, and what drives Cooking Mode (§14.1 criterion 6).
/// </param>
public record NormalisedSeedStep(
    int StepNumber,
    string Instruction,
    IReadOnlyList<int> IngredientIndexes);

// ── Stage results ─────────────────────────────────────────────────────────────

/// <summary>Outcome of a Stage 4 run.</summary>
public record SeedNormaliseResult
{
    /// <summary>Recipes the model handled and the gate admitted during this run.</summary>
    public int Normalised { get; init; }

    /// <summary>Recipes whose cached output was current and left untouched.</summary>
    public int Skipped { get; init; }

    /// <summary>Manifest entries with no parsed recipe yet — Stage 3 has not reached them.</summary>
    public int NotParsed { get; init; }

    /// <summary>Recipes excluded after exhausting their retries. Never persisted (§11.3).</summary>
    public int Failed { get; init; }

    /// <summary>Recipes that needed more than one attempt — the prompt-quality signal.</summary>
    public int Retried { get; init; }

    /// <summary>
    /// Recipes re-normalised because their parsed input had changed since the cached output was
    /// written. Non-zero after a <c>--parse --force</c> that actually changed the corpus.
    /// </summary>
    public int Stale { get; init; }

    /// <summary>
    /// True when the run stopped early on <see cref="RecipeSeedingOptions.MaxConsecutiveLlmFailures"/>.
    ///
    /// <para>Every value above describes something the model got wrong about one recipe, which is
    /// isolated and retried. This is the other case: inference itself is unavailable — no model
    /// loaded, no GPU, a broken grammar — and every remaining recipe would fail identically. It has
    /// no per-recipe failure of its own because it is not a property of any recipe.</para>
    /// </summary>
    public bool Aborted { get; init; }

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}

// ── Failures ──────────────────────────────────────────────────────────────────

/// <summary>
/// A recipe the LLM pass could not produce trustworthy output for. Caught per slug by the stage
/// runner, which records it and moves on: Phase 9 §16's rule is that one bad recipe never aborts a
/// run, and §11.3's is that a suspect recipe is never persisted.
/// </summary>
public class SeedNormaliseException(SeedNormaliseFailure failure, string detail)
    : Exception($"{failure}: {detail}")
{
    public SeedNormaliseFailure Failure { get; } = failure;

    /// <summary>What went wrong, in enough detail to act on — which line, which rejection.</summary>
    public string Detail { get; } = detail;

    /// <summary>
    /// LLM calls this recipe consumed before it was excluded, so <c>state.json</c> records the work
    /// a failure actually cost. It read 0 for every failed recipe before, because the runner only
    /// ever added the attempt count of a recipe that succeeded — a recipe that burned three passes
    /// and lost looked untouched in <c>--report</c>.
    /// </summary>
    public int Attempts { get; internal set; }

    /// <summary>The reason string written to <c>state.json</c> and printed in the run summary.</summary>
    public string StateReason => $"NormaliseFailed: {Failure} — {Detail}";
}
