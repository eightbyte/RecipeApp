using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Data;

/// <summary>
/// Loads the committed ingredient catalogue into Postgres (Phase 9.3 §4.5) — the seeding half of
/// Stage 4.5, and the only half any machine but the author's ever runs.
///
/// <para><b>No LLM, no network, no arguments.</b> This replaced <c>IngredientCatalogueSeeder</c>,
/// which generated a catalogue from a model's notion of "common household cooking ingredients" — a
/// list whose relationship to the recipe corpus was coincidental by construction, and which was
/// unreproducible because it carried a rolling exclusion list of what it had already produced. The
/// artefact this reads is derived from the corpus, reviewed once and committed, so every machine
/// gets the same catalogue.</para>
///
/// <para><b>Fill nulls, never overwrite.</b> The same contract
/// <see cref="IngredientDensitySeeder"/> already honours, and for the same reason: seed data is a
/// starting point, and a value a human corrected outranks the one this file suggests. A deliberate
/// refresh is what <c>--force</c> is for.</para>
/// </summary>
public static class IngredientCatalogueFileSeeder
{
    /// <summary>What one run changed.</summary>
    /// <param name="Inserted">Entries that had no catalogue row and now do.</param>
    /// <param name="Updated">Existing rows whose nulls were filled, or which <paramref name="Forced"/> overwrote.</param>
    /// <param name="Unchanged">Existing rows the artefact had nothing to add to.</param>
    /// <param name="Forced">Whether existing values were overwritten rather than only filled.</param>
    public record SeedCatalogueOutcome(int Inserted, int Updated, int Unchanged, bool Forced);

    /// <summary>
    /// Applies the artefact to the catalogue.
    /// </summary>
    /// <param name="force">
    /// Re-apply every field over existing rows rather than only filling nulls. A deliberate refresh,
    /// never the default — it discards corrections.
    /// </param>
    /// <exception cref="CatalogueValidationException">
    /// The artefact contradicts itself. Checked again here rather than trusted from build time: the
    /// file is hand-editable by design — a reviewer correcting a category is the intended workflow —
    /// so a rule enforced only at authoring time stops holding the moment someone acts on the review.
    /// </exception>
    public static async Task<SeedCatalogueOutcome> SeedAsync(
        AppDbContext db,
        IngredientCatalogueFile catalogue,
        ILogger? logger = null,
        bool force = false,
        CancellationToken ct = default)
    {
        var errors = IngredientCatalogueValidator.Validate(catalogue.Entries);
        if (errors.Count > 0) throw new CatalogueValidationException(errors);

        var existing = await db.Ingredients.ToListAsync(ct);
        var byName   = existing.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);

        var inserted  = 0;
        var updated   = 0;
        var unchanged = 0;

        foreach (var entry in catalogue.Entries)
        {
            if (!byName.TryGetValue(entry.Name, out var row))
            {
                db.Ingredients.Add(new Ingredient
                {
                    Id          = Guid.NewGuid(),
                    Name        = entry.Name,
                    DisplayName = entry.DisplayName,
                    Category    = entry.Category,
                    DefaultUnit = entry.DefaultUnit,
                    Aliases     = [.. entry.Aliases],
                    CreatedAt   = DateTime.UtcNow,
                });
                inserted++;
                continue;
            }

            if (Apply(row, entry, force)) updated++;
            else unchanged++;
        }

        await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "Ingredient catalogue seeded from the {Corpus} corpus artefact ({Generated:u}): " +
            "{Inserted} inserted, {Updated} updated, {Unchanged} already current.",
            catalogue.Source.Corpus, catalogue.GeneratedAt, inserted, updated, unchanged);

        return new SeedCatalogueOutcome(inserted, updated, unchanged, force);
    }

    /// <summary>
    /// Brings one existing row up to date, returning whether anything changed.
    ///
    /// <para>Without <paramref name="force"/> this only ever fills a null or replaces the
    /// <c>"OTHER"</c> placeholder, and aliases are merged rather than replaced — a row created at
    /// runtime by a scrape is exactly the case this protects, and taking its aliases away would
    /// break lookups that already work.</para>
    /// </summary>
    private static bool Apply(Ingredient row, IngredientCatalogueEntry entry, bool force)
    {
        var changed = false;

        if (force || string.IsNullOrWhiteSpace(row.DisplayName))
        {
            if (row.DisplayName != entry.DisplayName) { row.DisplayName = entry.DisplayName; changed = true; }
        }

        // "OTHER" is the column default, so a row carrying it was never categorised rather than
        // categorised as miscellaneous. Anything else is a judgement — the artefact's or a human's —
        // and is left alone.
        if (force || string.IsNullOrWhiteSpace(row.Category) || row.Category == IngredientCategory.Other)
        {
            if (row.Category != entry.Category) { row.Category = entry.Category; changed = true; }
        }

        if (force || row.DefaultUnit is null)
        {
            if (row.DefaultUnit != entry.DefaultUnit) { row.DefaultUnit = entry.DefaultUnit; changed = true; }
        }

        var aliases = force
            ? entry.Aliases
            : row.Aliases.Concat(entry.Aliases).Distinct(StringComparer.OrdinalIgnoreCase);

        var merged = aliases.OrderBy(alias => alias, StringComparer.Ordinal).ToArray();
        if (!merged.SequenceEqual(row.Aliases, StringComparer.OrdinalIgnoreCase))
        {
            row.Aliases = merged;
            changed = true;
        }

        return changed;
    }
}
