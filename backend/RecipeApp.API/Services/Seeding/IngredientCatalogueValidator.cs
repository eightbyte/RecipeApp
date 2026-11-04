using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The catalogue's invariants (rules 1–4), checked when a catalogue is built and again when it is seeded because the file is
/// hand-editable, and kept in code rather than Postgres because an ambiguous alias corrupts silently.
/// </summary>
public static class IngredientCatalogueValidator
{
    /// <summary>Rules 1–4: the invariants a catalogue can be checked against on its own, run by everything that reads or writes a catalogue.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<IngredientCatalogueEntry> entries)
    {
        var errors = new List<string>();

        var namesSeen          = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var displayNameOwners  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliasOwners        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            // ── Rule 1: every name unique, lowercase, non-blank ────────────────
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                errors.Add($"An entry has a blank name (display name '{entry.DisplayName}').");
                continue;
            }

            if (entry.Name != entry.Name.ToLowerInvariant() || entry.Name != entry.Name.Trim())
                errors.Add($"'{entry.Name}' is not stored normalised — names are lowercase and trimmed.");

            if (namesSeen.TryGetValue(entry.Name, out var firstIndex))
                errors.Add($"'{entry.Name}' appears as the name of more than one entry (first at index {firstIndex}).");
            else
                namesSeen[entry.Name] = namesSeen.Count;

            // Display names are what the ingredient picker shows, so two entries sharing one look like a duplicate row.
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
                errors.Add($"'{entry.Name}' has no display name.");
            else if (displayNameOwners.TryGetValue(entry.DisplayName.Trim(), out var displayNameOwner))
                errors.Add($"'{entry.Name}' has display name '{entry.DisplayName}', which '{displayNameOwner}' " +
                           "already uses. The ingredient picker would show them as duplicates.");
            else
                displayNameOwners[entry.DisplayName.Trim()] = entry.Name;

            // ── Rule 2: category is one of ours ────────────────────────────────
            if (!IngredientCategory.IsValid(entry.Category))
                errors.Add($"'{entry.Name}' has category '{entry.Category}', which is not one of " +
                           $"{string.Join(", ", IngredientCategory.All)}.");

            // ── Rule 3: default unit is storable, in its canonical spelling ────
            // Strict on purpose, because a unit that fails validation is a row no consolidation can group.
            if (entry.DefaultUnit is not null && !MeasurementUnit.IsValid(entry.DefaultUnit))
                errors.Add($"'{entry.Name}' has default unit '{entry.DefaultUnit}', which is not a " +
                           $"canonical storable unit ({string.Join(", ", MeasurementUnit.All)}).");
        }

        // ── Rule 4: aliases are unambiguous ───────────────────────────────────
        // Checked after every name is known, because "no alias equals any entry's name" is a statement about the whole file.
        foreach (var entry in entries)
        {
            var seenWithinEntry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var alias in entry.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    errors.Add($"'{entry.Name}' has a blank alias.");
                    continue;
                }

                if (alias != alias.ToLowerInvariant() || alias != alias.Trim())
                    errors.Add($"'{entry.Name}' has alias '{alias}', which is not stored normalised.");

                if (!seenWithinEntry.Add(alias))
                    errors.Add($"'{entry.Name}' lists alias '{alias}' twice.");

                if (namesSeen.ContainsKey(alias))
                    errors.Add($"'{entry.Name}' has alias '{alias}', which is also an entry's name. " +
                               "An alias that shadows a name resolves by row order, not by intent.");

                if (aliasOwners.TryGetValue(alias, out var owner) && !string.Equals(owner, entry.Name, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Alias '{alias}' is claimed by both '{owner}' and '{entry.Name}'.");
                else
                    aliasOwners[alias] = entry.Name;
            }
        }

        return errors;
    }
}
