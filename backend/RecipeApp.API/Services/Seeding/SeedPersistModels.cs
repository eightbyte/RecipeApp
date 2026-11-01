namespace RecipeApp.API.Services.Seeding;

/// <summary>Why Stage 5 declined to persist a recipe, recorded against the slug while the run moves on.</summary>
public enum SeedPersistFailure
{
    /// <summary>The normalised artefact was derived from a different <c>parsed/</c> file than the one on disk now, so <c>--normalise</c> must be re-run.</summary>
    StaleArtefact,

    /// <summary>The artefact's ingredient rows no longer pair one-to-one with the parsed lines, so the file was edited outside the pipeline.</summary>
    IngredientCountMismatch,

    /// <summary>A step points at an ingredient row that does not exist.</summary>
    IngredientIndexOutOfRange,

    /// <summary>An ingredient name the committed catalogue cannot resolve.</summary>
    CatalogueMiss,

    /// <summary>The name, composed description or serving count falls outside <c>RecipeLimits</c>, so the recipe could be imported but never saved from the UI.</summary>
    ExceedsRecipeLimits,

    /// <summary><c>--force</c> asked to replace a recipe that a meal plan references, which the database refuses and which is left as it is.</summary>
    RecipeInUse,

    /// <summary>Excluded by the quality filter, counted separately from real failures and only raised when <see cref="RecipeSeedingOptions.ApplyQualityFilter"/> is on.</summary>
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

    /// <summary>Recipes already in the database and left alone, so re-running is a no-op.</summary>
    public int AlreadyPersisted { get; init; }

    /// <summary>Manifest entries with no normalised artefact yet — Stage 4 has not reached them.</summary>
    public int NotNormalised { get; init; }

    /// <summary>Recipes declined, excluding the quality filter's deliberate exclusions.</summary>
    public int Failed { get; init; }

    /// <summary>Recipes the quality filter excluded (only when it is on).</summary>
    public int FilteredOut { get; init; }

    /// <summary>Recipes persisted that the quality filter would have excluded, reported while the filter is off so its thresholds can be chosen from evidence.</summary>
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

    /// <summary>Rows moved to another catalogue entry by <see cref="SeedIngredientRowOverrides"/>.</summary>
    public int RowsReassigned { get; init; }

    /// <summary>Row-override lines that matched nothing, per slug — stale entries to remove.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StaleRowOverrideLines { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Failure reason per slug, for the closing run summary.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; init; } =
        new Dictionary<string, string>();
}
