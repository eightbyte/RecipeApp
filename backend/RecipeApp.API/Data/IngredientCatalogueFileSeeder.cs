using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Data;

/// <summary>Loads the committed ingredient catalogue into Postgres with no LLM, network or arguments, filling nulls and never overwriting a human correction.</summary>
public static class IngredientCatalogueFileSeeder
{
    /// <summary>What one run changed: entries inserted, rows updated, rows unchanged, and whether existing values were overwritten rather than only filled.</summary>
    public record SeedCatalogueOutcome(int Inserted, int Updated, int Unchanged, bool Forced);

    /// <summary>Applies the artefact to the catalogue.</summary>
    /// <param name="force">Re-apply every field over existing rows rather than only filling nulls, which discards corrections.</param>
    /// <exception cref="CatalogueValidationException">The artefact contradicts itself, which is checked again here because the file is hand-editable.</exception>
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
                    IsStaple    = entry.IsStaple,
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
            "Ingredient catalogue seeded from content version {ContentVersion} ({Updated:u}): " +
            "{Inserted} inserted, {Changed} updated, {Unchanged} already current.",
            catalogue.ContentVersion, catalogue.UpdatedAt, inserted, updated, unchanged);

        return new SeedCatalogueOutcome(inserted, updated, unchanged, force);
    }

    /// <summary>Brings one existing row up to date and returns whether anything changed, only filling nulls and merging aliases, and leaving the Staple flag alone, unless <paramref name="force"/> is set.</summary>
    private static bool Apply(Ingredient row, IngredientCatalogueEntry entry, bool force)
    {
        var changed = false;

        if (force || string.IsNullOrWhiteSpace(row.DisplayName))
        {
            if (row.DisplayName != entry.DisplayName) { row.DisplayName = entry.DisplayName; changed = true; }
        }

        // "OTHER" is the column default, so a row carrying it was never categorised; anything else is a judgement and is left alone.
        if (force || string.IsNullOrWhiteSpace(row.Category) || row.Category == IngredientCategory.Other)
        {
            if (row.Category != entry.Category) { row.Category = entry.Category; changed = true; }
        }

        if (force || row.DefaultUnit is null)
        {
            if (row.DefaultUnit != entry.DefaultUnit) { row.DefaultUnit = entry.DefaultUnit; changed = true; }
        }

        // A flag cannot record that it was never set, so only a forced run may overrule the row.
        if (force && row.IsStaple != entry.IsStaple) { row.IsStaple = entry.IsStaple; changed = true; }

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
