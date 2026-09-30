using RecipeApp.API.Enums;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The catalogue's invariants (Phase 9.3 §4.7), checked when the artefact is built and again when
/// it is seeded.
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
    /// Rules 1–4: the invariants the artefact can be checked against on its own, with no corpus
    /// present. Everything that reads the catalogue runs these.
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<IngredientCatalogueEntry> entries)
    {
        var errors = new List<string>();

        var namesSeen   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var aliasOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

            if (string.IsNullOrWhiteSpace(entry.DisplayName))
                errors.Add($"'{entry.Name}' has no display name.");

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

    /// <summary>
    /// Rules 5–6: the invariants that need the corpus, so only the build pass can check them.
    ///
    /// <para>Rule 5 is the load-bearing one. A corpus name the pass dropped is an ingredient no
    /// recipe using it can resolve, which is a recipe Stage 5 cannot persist — and catching it here
    /// names the problem at authoring time instead of a thousand rows into an import.</para>
    /// </summary>
    /// <param name="entries">The entries about to be written.</param>
    /// <param name="corpusNames">Every distinct ingredient name in the normalised corpus.</param>
    /// <param name="corpusRows">Total ingredient rows in the corpus.</param>
    public static IReadOnlyList<string> ValidateAgainstCorpus(
        IReadOnlyList<IngredientCatalogueEntry> entries,
        IReadOnlyCollection<string> corpusNames,
        int corpusRows)
    {
        var errors = new List<string>();

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            reachable.Add(entry.Name);
            foreach (var alias in entry.Aliases) reachable.Add(alias);
        }

        // ── Rule 5: every corpus name is reachable ────────────────────────────
        var unreachable = corpusNames.Where(name => !reachable.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (unreachable.Count > 0)
            errors.Add($"{unreachable.Count} corpus name(s) are reachable as neither a name nor an " +
                       $"alias, so any recipe using them cannot be persisted: " +
                       $"{string.Join(", ", unreachable.Take(20))}" +
                       (unreachable.Count > 20 ? ", …" : string.Empty));

        // ── Rule 6: the provenance counts add up ──────────────────────────────
        var claimedRows = entries.Sum(entry => entry.CorpusRows);
        if (claimedRows != corpusRows)
            errors.Add($"Entries claim {claimedRows} corpus rows but the corpus holds {corpusRows}. " +
                       "Every row belongs to exactly one entry, so a mismatch means a name was " +
                       "double-counted or lost.");

        return errors;
    }
}
