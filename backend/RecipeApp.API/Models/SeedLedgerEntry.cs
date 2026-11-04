namespace RecipeApp.API.Models;

/// <summary>
/// Record of a one-time first-run seed step, one row per step; the ledger rather than the seeded data is the evidence that a
/// step ran, and the primary key stops two hosts both recording one.
/// </summary>
public class SeedLedgerEntry
{
    /// <summary>The step's key — see <see cref="Data.SeedLedgerKeys"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What the step did — see <see cref="Enums.SeedLedgerOutcome"/>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The <c>contentVersion</c> of the seed artefact the step read, recorded for both outcomes.</summary>
    public int ArtefactVersion { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
