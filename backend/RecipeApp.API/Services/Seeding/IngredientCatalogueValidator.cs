using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The catalogue's invariants (Phase 9.3 §4.7, rules 1–4), checked when a catalogue is built and
/// again when it is seeded.
///
/// <para><b>Rules 5 and 6 are gone from here (Phase 10 §4.7).</b> Rule 5, every MyPlate corpus name
/// reachable, needs the gitignored corpus, so it is now a test that runs only where the local cache
/// exists. Rule 6 checked the per-entry corpus counts, which the shipped catalogue no longer
/// carries.</para>
///
/// <para><b>Why twice.</b> The build pass and the seeder run on different machines at different
/// times, and the file between them is hand-editable by design — a reviewer correcting a category
/// is the intended workflow. A rule checked only at authoring time is a rule that stops holding the
/// moment someone acts on the review.</para>
///
/// <para><b>Why not in Postgres.</b> Rules 1–3 could be constraints. Rule 4 — no alias shared
/// between entries, no alias equal to any name — cannot be expressed against a <c>text[]</c> column
/// at reasonable cost, and it is the one that corrupts silently: an ambiguous alias resolves to
/// whichever row the lookup dictionary happened to see first, which is a function of row order. So
/// all four live here, where they can be tested.</para>
/// </summary>
public static class IngredientCatalogueValidator
{
    /// <summary>
    /// Rules 1–4: the invariants a catalogue can be checked against on its own, with no corpus
    /// present. Everything that reads or writes a catalogue runs these.
    /// </summary>
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

            // Display names are what the ingredient picker shows, so two entries sharing one look
            // like a duplicate row to the user ("dried sage" and "sage" both shown as "Sage").
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
            // Strict on purpose: the build pass canonicalises before writing, so a non-canonical
            // spelling here means the file was hand-edited with one MeasurementUnit.IsValid would
            // reject — and a unit that fails validation is a row no consolidation can group.
            if (entry.DefaultUnit is not null && !MeasurementUnit.IsValid(entry.DefaultUnit))
                errors.Add($"'{entry.Name}' has default unit '{entry.DefaultUnit}', which is not a " +
                           $"canonical storable unit ({string.Join(", ", MeasurementUnit.All)}).");
        }

        // ── Rule 4: aliases are unambiguous ───────────────────────────────────
        // Checked after every name is known, because "no alias equals any entry's name" is a
        // statement about the whole file, not about the entry carrying the alias.
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
