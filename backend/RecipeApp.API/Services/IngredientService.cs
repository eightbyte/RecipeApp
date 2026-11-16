using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs.Ingredients;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

/// <summary>What a hand edit to the catalogue did: saved the Ingredient, found no Ingredient, or was refused because a name or alias is taken.</summary>
public record IngredientEditResult(Ingredient? Ingredient, string? Clash)
{
    public static readonly IngredientEditResult NotFound = new(null, null);
    public static IngredientEditResult Saved(Ingredient ingredient) => new(ingredient, null);
    public static IngredientEditResult Refused(string clash) => new(null, clash);
}

/// <summary>Edits the Ingredient Catalogue by hand, keeping every Ingredient's <c>Name</c> the normalised form of its display name.</summary>
public class IngredientService(AppDbContext db)
{
    /// <summary>The lookup form of a name or alias, as Scraping and Import match it.</summary>
    public static string Normalise(string name) => name.Trim().ToLowerInvariant();

    public async Task<IngredientEditResult> CreateAsync(CreateIngredientRequest request, CancellationToken ct = default)
    {
        var name    = Normalise(request.DisplayName);
        var aliases = NormaliseAliases(request.Aliases ?? [], name);

        var clash = await FindClashAsync(null, [name, .. aliases], ct);
        if (clash is not null) return IngredientEditResult.Refused(clash);

        var ingredient = new Ingredient
        {
            Id                 = Guid.NewGuid(),
            Name               = name,
            DisplayName        = request.DisplayName.Trim(),
            Category           = request.Category,
            DefaultUnit        = request.DefaultUnit,
            Aliases            = aliases,
            GramsPerMillilitre = request.GramsPerMillilitre,
            IsStaple           = request.IsStaple,
            // A new Ingredient with no density stated leaves the choice to the curated seed.
            HasHandSetDensity  = request.GramsPerMillilitre is not null,
            CreatedAt          = DateTime.UtcNow,
        };

        db.Ingredients.Add(ingredient);
        await db.SaveChangesAsync(ct);
        return IngredientEditResult.Saved(ingredient);
    }

    public async Task<IngredientEditResult> UpdateAsync(Guid id, UpdateIngredientRequest request, CancellationToken ct = default)
    {
        var ingredient = await db.Ingredients.FindAsync([id], ct);
        if (ingredient is null) return IngredientEditResult.NotFound;

        var name    = Normalise(request.DisplayName);
        var aliases = request.Aliases is null ? ingredient.Aliases : NormaliseAliases(request.Aliases, name);

        // Only what this save adds is checked, so a clash the catalogue already held never blocks an unrelated edit.
        var added = aliases.Except(ingredient.Aliases).ToList();
        if (name != ingredient.Name) added.Add(name);

        var clash = await FindClashAsync(id, added, ct);
        if (clash is not null) return IngredientEditResult.Refused(clash);

        ingredient.Name               = name;
        ingredient.DisplayName        = request.DisplayName.Trim();
        ingredient.Category           = request.Category;
        ingredient.DefaultUnit        = request.DefaultUnit;
        ingredient.GramsPerMillilitre = request.GramsPerMillilitre;
        ingredient.HasHandSetDensity  = true;
        ingredient.IsStaple           = request.IsStaple;
        ingredient.Aliases            = aliases;

        await db.SaveChangesAsync(ct);
        return IngredientEditResult.Saved(ingredient);
    }

    /// <summary>Makes it a Removed Ingredient, which recipes and shopping lists keep showing; removing one already removed changes nothing.</summary>
    /// <returns>False when no Ingredient has <paramref name="id"/>.</returns>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var ingredient = await db.Ingredients.FindAsync([id], ct);
        if (ingredient is null) return false;

        ingredient.RemovedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Makes a Removed Ingredient active again, unless an active Ingredient has since taken its name or an alias.</summary>
    public async Task<IngredientEditResult> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var ingredient = await db.Ingredients.FindAsync([id], ct);
        if (ingredient is null) return IngredientEditResult.NotFound;
        if (ingredient.RemovedAt is null) return IngredientEditResult.Saved(ingredient);

        var clash = await FindClashAsync(id, [ingredient.Name, .. ingredient.Aliases], ct);
        if (clash is not null) return IngredientEditResult.Refused(clash);

        ingredient.RemovedAt = null;
        await db.SaveChangesAsync(ct);
        return IngredientEditResult.Saved(ingredient);
    }

    /// <returns>Null when no Ingredient has <paramref name="id"/>.</returns>
    public async Task<IngredientUsageResponse?> GetUsageAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.Ingredients.AnyAsync(i => i.Id == id, ct)) return null;

        var recipeCount = await db.Recipes.CountAsync(r => r.Ingredients.Any(ri => ri.IngredientId == id), ct);
        return new IngredientUsageResponse(recipeCount);
    }

    /// <summary>Names the first other active Ingredient whose name or alias is one of <paramref name="keys"/>, or returns null when none is.</summary>
    private async Task<string?> FindClashAsync(Guid? selfId, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return null;

        var other = await db.Ingredients
            .AsNoTracking()
            .Where(i => i.RemovedAt == null && i.Id != selfId)
            .Where(i => keys.Contains(i.Name) || i.Aliases.Any(alias => keys.Contains(alias)))
            .OrderBy(i => i.DisplayName)
            .Select(i => new { i.Name, i.DisplayName, i.Aliases })
            .FirstOrDefaultAsync(ct);
        if (other is null) return null;

        var taken = keys.First(key => key == other.Name || other.Aliases.Contains(key));
        return $"'{taken}' is already used by {other.DisplayName}.";
    }

    /// <summary>Normalised, de-duplicated and sorted, without blanks or the Ingredient's own name, which already matches.</summary>
    private static string[] NormaliseAliases(IEnumerable<string> aliases, string name) =>
        [.. aliases.Select(Normalise)
                   .Where(alias => alias.Length > 0 && alias != name)
                   .Distinct()
                   .OrderBy(alias => alias, StringComparer.Ordinal)];
}
