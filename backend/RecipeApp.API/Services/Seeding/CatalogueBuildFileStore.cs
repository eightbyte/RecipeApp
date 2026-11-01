using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// Reads and writes <c>seed-data/myplate/catalogue-build.json</c>, what <c>--build-catalogue</c> derives, kept apart from the
/// shipped catalogue so a re-run cannot overwrite the curation.
/// </summary>
public class CatalogueBuildFileStore
{
    /// <summary>Suffix for the temporary file an atomic write moves into place.</summary>
    private const string TempExtension = ".tmp";

    public CatalogueBuildFileStore(
        IOptions<RecipeSeedingOptions> options,
        IHostEnvironment environment)
    {
        Path = IngredientCatalogueFileStore.ResolvePath(options.Value.CatalogueBuildFilePath, environment);
    }

    /// <summary>Absolute path of the build output, resolved against the content root.</summary>
    public string Path { get; }

    public bool Exists() => File.Exists(Path);

    /// <summary>Reads the build output.</summary>
    /// <exception cref="FileNotFoundException">No build has been run.</exception>
    /// <exception cref="CatalogueValidationException">The file is unreadable or in another version.</exception>
    public async Task<CatalogueBuildFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException(
                $"No catalogue build at {Path}. Run 'dotnet run -- {SeedRecipesCommand.CommandName} " +
                "--build-catalogue'.", Path);

        CatalogueBuildFile? file;
        try
        {
            await using var stream = File.OpenRead(Path);
            file = await JsonSerializer.DeserializeAsync<CatalogueBuildFile>(
                stream, IngredientCatalogueFileStore.JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new CatalogueValidationException([$"{Path} is not readable as JSON — {ex.Message}"]);
        }

        if (file is null)
            throw new CatalogueValidationException([$"{Path} deserialised to nothing."]);

        if (file.Version != CatalogueBuildFile.CurrentVersion)
            throw new CatalogueValidationException(
                [$"{Path} is version {file.Version}; this build reads version {CatalogueBuildFile.CurrentVersion}."]);

        return file;
    }

    /// <summary>Writes the build output through a temporary file, so a crash mid-write leaves the previous build intact.</summary>
    public async Task SaveAsync(CatalogueBuildFile file, CancellationToken ct = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = Path + TempExtension;

        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, file, IngredientCatalogueFileStore.JsonOptions, ct);

        File.Move(tempPath, Path, overwrite: true);
    }
}
