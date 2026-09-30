namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Why Stage 5 declined to persist a recipe. Each is recorded against the slug and the run moves on
/// (Phase 9 §16: one bad recipe never aborts a run; no suspect recipe is ever persisted).
/// </summary>
public enum SeedPersistFailure
{
    /// <summary>
    /// The normalised artefact was derived from a different <c>parsed/</c> file than the one on disk
    /// now — the page was re-parsed and not re-normalised. Re-run <c>--normalise</c>.
    /// </summary>
    StaleArtefact,

    /// <summary>
    /// The artefact's ingredient rows no longer pair one-to-one with the parsed lines (§11.3). Stage 4
    /// never writes this, so it means the file was edited outside the pipeline (§24.4.2).
    /// </summary>
    IngredientCountMismatch,

    /// <summary>A step points at an ingredient row that does not exist — the other half of §24.4.2.</summary>
    IngredientIndexOutOfRange,

    /// <summary>An ingredient name the committed catalogue cannot resolve (§12.1, §24.4.1).</summary>
    CatalogueMiss,

    /// <summary>
    /// The name, composed description or serving count falls outside <c>RecipeLimits</c>, so the
    /// recipe could be imported but never edited and saved in the UI.
    /// </summary>
    ExceedsRecipeLimits,

    /// <summary>
    /// <c>--force</c> asked to replace a recipe that a meal plan references. Deleting it is refused by
    /// the database (<c>MealPlanRecipe → Recipe</c> is <c>Restrict</c>), and silently removing it from
    /// someone's plan would be worse. The existing row is left as it is.
    /// </summary>
    RecipeInUse,

    /// <summary>
    /// Excluded by the §18 Q3 quality filter. Only raised when
    /// <see cref="RecipeSeedingOptions.ApplyQualityFilter"/> is on; counted separately from real
    /// failures so the §20 "≥ 95% persisted" bar stays measurable.
    /// </summary>
    BelowQualityThreshold,
}

/// <summary>A recipe Stage 5 declined to persist. Caught per slug by the stage runner.</summary>
public class SeedPersistException(SeedPersistFailure failure, string detail)
    : Exception($"{failure}: {detail}")
{
    public SeedPersistFailure Failure { get; } = failure;

    public string Detail { get; } = detail;

    /// <summary>The reason string written to <c>state.json</c> and printed in the run summary.</summary>
    public string StateReason => $"PersistFailed: {Failure} — {Detail}";
}

/// <summary>What persisting one recipe did, for the run summary.</summary>
/// <param name="Replaced">An existing row was deleted first, under <c>--force</c>.</param>
/// <param name="NonIngredientRowsRemoved">Heading and note rows dropped by <see cref="SeedNonIngredientRows"/>.</param>
/// <param name="StaleSkipListLines">Skip-list lines this recipe did not contain.</param>
/// <param name="AmountsDropped">Rows stored unquantified because their amount was unexplained (§24.3a).</param>
/// <param name="HasImage">Whether a harvested photo was copied into image storage.</param>
public record SeedPersistOutcome(
    bool Replaced,
    int NonIngredientRowsRemoved,
    IReadOnlyList<string> StaleSkipListLines,
    int AmountsDropped,
    bool HasImage);

/// <summary>Outcome of a Stage 5 run.</summary>
public record SeedPersistResult
{
    /// <summary>Recipes written to the database during this run, replacements included.</summary>
    public int Persisted { get; init; }

    /// <summary>Of <see cref="Persisted"/>, how many replaced an existing row under <c>--force</c>.</summary>
    public int Replaced { get; init; }

    /// <summary>Recipes already in the database and left alone — re-running is a no-op (§20).</summary>
    public int AlreadyPersisted { get; init; }

    /// <summary>Manifest entries with no normalised artefact yet — Stage 4 has not reached them.</summary>
    public int NotNormalised { get; init; }

    /// <summary>Recipes declined, excluding the quality filter's deliberate exclusions.</summary>
    public int Failed { get; init; }

    /// <summary>Recipes the quality filter excluded (only when it is on).</summary>
    public int FilteredOut { get; init; }

    /// <summary>
    /// Recipes persisted that the quality filter <i>would</i> have excluded — reported while the
    /// filter is off so its thresholds can be chosen from evidence after the trial (§18 Q3).
    /// </summary>
    public int BelowQualityThreshold { get; init; }

    /// <summary>Heading and note rows removed across the persisted recipes.</summary>
    public int NonIngredientRowsRemoved { get; init; }

    /// <summary>Rows stored without an amount because the line could not account for it.</summary>
    public int AmountsDropped { get; init; }

    /// <summary>Persisted recipes with no photo.</summary>
    public int WithoutImage { get; init; }

    /// <summary>Skip-list lines that matched nothing, per slug — stale entries to remove.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StaleSkipListLines { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}
