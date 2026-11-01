namespace RecipeApp.API.Services.Seeding;

/// <summary>Why a recipe was rejected by Stage 4: each value is something the model got wrong about this recipe, which is retried and then excluded while the run continues.</summary>
public enum SeedNormaliseFailure
{
    /// <summary>The model returned a different number of ingredients than the page lists.</summary>
    IngredientCountMismatch,

    /// <summary>The model returned a different number of steps than the page lists.</summary>
    StepCountMismatch,

    /// <summary>Step numbers are not 1..N without gaps or repeats.</summary>
    StepNumbersNotContiguous,

    /// <summary>An <c>ingredient_indexes</c> entry points outside the ingredients array.</summary>
    IngredientIndexOutOfRange,

    /// <summary>An ingredient came back with no name to match against the catalogue.</summary>
    MissingIngredientName,

    /// <summary>An ingredient failed <see cref="SeedQuantityGate"/>, and the detail names the line and the <see cref="SeedQuantityRejection"/>.</summary>
    QuantityRejected,

    /// <summary>The parsed page yielded no usable serving count.</summary>
    NonPositiveServings,

    /// <summary>The model produced output the extraction schema's own shape could not be read from.</summary>
    InvalidLlmOutput,

    /// <summary>The model did not answer within the configured budget.</summary>
    LlmTimeout,
}

/// <summary>Tells apart the two kinds of Stage 4 failure, which the run-abort guard needs.</summary>
public static class SeedNormaliseFailureExtensions
{
    /// <summary>
    /// Whether this failure says inference itself is unavailable rather than something about one recipe, because counting
    /// content rejections as systemic wedged resumed runs.
    /// </summary>
    public static bool IsSystemic(this SeedNormaliseFailure failure) =>
        failure is SeedNormaliseFailure.InvalidLlmOutput or SeedNormaliseFailure.LlmTimeout;
}

// ── normalised/{slug}.json (Stage 4 output) ───────────────────────────────────

/// <summary>One recipe after the LLM pass: only the ingredient rows and each step's ingredient linkage come from the model, and everything else is copied from the parsed page.</summary>
public record NormalisedSeedRecipe
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Cache key and filename stem.</summary>
    public required string Slug { get; init; }

    /// <summary>The canonical <c>myplate.gov</c> URL, kept as provenance and as Stage 5's idempotency key.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Hash of the <c>parsed/</c> file this was derived from, so a re-parse that changes the input invalidates this artefact.</summary>
    public required string ParsedFingerprint { get; init; }

    public required DateTime NormalisedAt { get; init; }

    /// <summary>LLM attempts this recipe took, where more than 1 means the validation gate rejected an earlier answer.</summary>
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

/// <summary>One ingredient row, carrying both sides of the Stage A conversion so the arithmetic can be audited from the cache alone.</summary>
/// <param name="Amount">Amount after conversion as the quantity gate admitted it; null when the source states no quantity, and never zero.</param>
/// <param name="SourceUnit">The unit as the model read it, kept as provenance and never summed.</param>
/// <param name="SourceText">The verbatim ingredient line the model was given, so a suspicious row can be read against what the page said.</param>
public record NormalisedSeedIngredient(
    string Name,
    string DisplayName,
    decimal? Amount,
    string? Unit,
    decimal? SourceAmount,
    string? SourceUnit,
    string? Notes,
    string SourceText);

/// <param name="IngredientIndexes">0-based positions in <see cref="NormalisedSeedRecipe.Ingredients"/> used by this step, the one thing about steps only the model supplies.</param>
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

    /// <summary>Recipes excluded after exhausting their retries and never persisted.</summary>
    public int Failed { get; init; }

    /// <summary>Recipes that needed more than one attempt — the prompt-quality signal.</summary>
    public int Retried { get; init; }

    /// <summary>Recipes re-normalised because their parsed input changed since the cached output was written.</summary>
    public int Stale { get; init; }

    /// <summary>True when the run stopped early on <see cref="RecipeSeedingOptions.MaxConsecutiveLlmFailures"/> because inference itself is unavailable.</summary>
    public bool Aborted { get; init; }

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}

// ── Failures ──────────────────────────────────────────────────────────────────

/// <summary>A recipe the LLM pass could not produce trustworthy output for, caught per slug so one bad recipe never aborts a run.</summary>
public class SeedNormaliseException(SeedNormaliseFailure failure, string detail)
    : Exception($"{failure}: {detail}")
{
    public SeedNormaliseFailure Failure { get; } = failure;

    /// <summary>What went wrong, in enough detail to act on — which line, which rejection.</summary>
    public string Detail { get; } = detail;

    /// <summary>LLM calls this recipe consumed before it was excluded, so <c>state.json</c> records the work a failure actually cost.</summary>
    public int Attempts { get; internal set; }

    /// <summary>The reason string written to <c>state.json</c> and printed in the run summary.</summary>
    public string StateReason => $"NormaliseFailed: {Failure} — {Detail}";
}
