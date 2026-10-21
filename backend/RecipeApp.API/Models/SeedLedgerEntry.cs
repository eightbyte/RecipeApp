namespace RecipeApp.API.Models;

/// <summary>
/// Record of a one-time first-run seed step (Phase 10 §4.1). One row per step.
///
/// <para>The ledger, not the seeded data, is the evidence that a step ran: an empty
/// <c>Recipes</c> table no longer means "never seeded", so deleting every sample recipe does not
/// bring them back (D2). A step runs only when its <see cref="Key"/> has no row, and the step and
/// its row are written in one transaction.</para>
///
/// <para>The primary key on <see cref="Key"/> means two hosts starting against the same database
/// cannot both record a step; the loser's transaction fails and rolls back.</para>
/// </summary>
public class SeedLedgerEntry
{
    /// <summary>The step's key — see <see cref="Data.SeedLedgerKeys"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What the step did — see <see cref="Enums.SeedLedgerOutcome"/>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// The <c>contentVersion</c> of the seed artefact the step read. Recorded for both outcomes so
    /// an additive catalogue update can be added later without a schema change (D7).
    /// </summary>
    public int ArtefactVersion { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
