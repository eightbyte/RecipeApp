using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Reads and writes <c>seed-data/ingredient-catalogue.json</c> — the committed catalogue artefact
/// (Phase 9.3 §4.2).
///
/// <para>Deliberately <i>outside</i> <see cref="SeedCacheStore"/>'s tree. The cache under
/// <c>seed-data/myplate/</c> is scratch that <c>--refresh-cache</c> is allowed to delete; this file
/// is reviewed source that must survive it. Keeping it a sibling also means the existing
/// <c>.gitignore</c> rules — which ignore <c>raw/</c>, <c>parsed/</c>, <c>normalised/</c>,
/// <c>images/</c> and <c>state.json</c> beneath <c>seed-data/**</c> — already leave it tracked
/// without a new exception.</para>
/// </summary>
public class IngredientCatalogueFileStore
{
    /// <summary>Suffix for the temporary file an atomic write moves into place.</summary>
    private const string TempExtension = ".tmp";

    /// <summary>
    /// Indented camelCase, matching <see cref="SeedCacheStore"/>. Indentation is not cosmetic here:
    /// the artefact is reviewed and diffed by a human, and a one-line file is neither.
    /// </summary>
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
        var configured = options.Value.CatalogueFilePath;
        Path = System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.Combine(environment.ContentRootPath, configured);
    }

    /// <summary>Absolute path of the artefact, resolved against the content root.</summary>
    public string Path { get; }

    public bool Exists() => File.Exists(Path);

    /// <summary>
    /// Reads the artefact.
    /// </summary>
    /// <exception cref="FileNotFoundException">The artefact has not been built.</exception>
    /// <exception cref="CatalogueValidationException">The file is unreadable or malformed.</exception>
    public async Task<IngredientCatalogueFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException(
                $"No ingredient catalogue at {Path}. Build one with " +
                $"'dotnet run -- {SeedRecipesCommand.CommandName} --build-catalogue'.", Path);

        IngredientCatalogueFile? file;
        try
        {
            await using var stream = File.OpenRead(Path);
            file = await JsonSerializer.DeserializeAsync<IngredientCatalogueFile>(stream, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            // Unlike the derived caches, this one is never silently rebuilt. It is reviewed,
            // committed source: a corrupt copy means someone's edit broke it, and rebuilding over
            // the top would destroy the evidence along with the edit.
            throw new CatalogueValidationException([$"{Path} is not readable as JSON — {ex.Message}"]);
        }

        if (file is null)
            throw new CatalogueValidationException([$"{Path} deserialised to nothing."]);

        if (file.Version != IngredientCatalogueFile.CurrentVersion)
            throw new CatalogueValidationException(
                [$"{Path} is version {file.Version}; this build reads version " +
                 $"{IngredientCatalogueFile.CurrentVersion}."]);

        return file;
    }

    /// <summary>
    /// Writes the artefact through a temporary file, so a crash mid-write leaves the reviewed copy
    /// intact rather than truncating it.
    /// </summary>
    public async Task SaveAsync(IngredientCatalogueFile file, CancellationToken ct = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = Path + TempExtension;

        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, file, JsonOptions, ct);

        File.Move(tempPath, Path, overwrite: true);
    }
}
