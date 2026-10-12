namespace RecipeApp.API.Enums;

/// <summary>
/// What a first-run seed step did, stored in <see cref="Models.SeedLedgerEntry.Outcome"/>
/// (Phase 10 §4.1, §4.3).
/// </summary>
public static class SeedLedgerOutcome
{
    /// <summary>The step found its target empty and inserted the artefact.</summary>
    public const string Applied = "Applied";

    /// <summary>
    /// The step had no ledger row but its target already held data (an upgraded install), so it
    /// inserted nothing.
    /// </summary>
    public const string SkippedExistingData = "SkippedExistingData";

    public static readonly IReadOnlyList<string> All = [Applied, SkippedExistingData];

    public static bool IsValid(string value) => All.Contains(value);
}
