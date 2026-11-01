using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>Reads <c>seed-data/ingredient-catalogue.json</c>, the shipped catalogue, read-only and kept outside the cache tree so <c>--refresh-cache</c> cannot delete it.</summary>
public class IngredientCatalogueFileStore
{
    /// <summary>Indented camelCase matching <see cref="SeedCacheStore"/>, because the catalogue files are reviewed and diffed by a human.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public IngredientCatalogueFileStore(
        IOptions<RecipeSeedingOptions> options,
        IHostEnvironment environment)
    {
        Path = ResolvePath(options.Value.CatalogueFilePath, environment);
    }

    /// <summary>Absolute path of the catalogue, resolved against the content root.</summary>
    public string Path { get; }

    public bool Exists() => File.Exists(Path);

    /// <summary>Reads the catalogue, accepting any <see cref="IngredientCatalogueFile.ContentVersion"/> but not an unknown <see cref="IngredientCatalogueFile.FormatVersion"/>.</summary>
    /// <exception cref="FileNotFoundException">The catalogue is missing.</exception>
    /// <exception cref="CatalogueValidationException">The file is unreadable, malformed or in a format this build cannot read.</exception>
    public async Task<IngredientCatalogueFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException($"No ingredient catalogue at {Path}.", Path);

        // The format is checked before the full read, so a file in another shape is reported as that rather than as whichever property it lacks.
        int? formatVersion;
        IngredientCatalogueFile? file;
        try
        {
            var json = await File.ReadAllTextAsync(Path, ct);
            formatVersion = JsonSerializer.Deserialize<FormatProbe>(json, JsonOptions)?.FormatVersion;

            if (formatVersion != IngredientCatalogueFile.CurrentFormatVersion)
                throw new CatalogueValidationException(
                    [$"{Path} is format version {formatVersion?.ToString() ?? "(none)"}; this build " +
                     $"reads format version {IngredientCatalogueFile.CurrentFormatVersion}."]);

            file = JsonSerializer.Deserialize<IngredientCatalogueFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Unlike the derived caches this is never silently rebuilt, because a corrupt copy means someone's edit broke it.
            throw new CatalogueValidationException([$"{Path} is not readable as JSON — {ex.Message}"]);
        }

        return file ?? throw new CatalogueValidationException([$"{Path} deserialised to nothing."]);
    }

    /// <summary>Resolves a configured path against the content root unless it is already absolute.</summary>
    internal static string ResolvePath(string configured, IHostEnvironment environment) =>
        System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.Combine(environment.ContentRootPath, configured);

    /// <summary>Only the field needed to decide whether the rest of the file can be read.</summary>
    private sealed record FormatProbe(int? FormatVersion);
}
