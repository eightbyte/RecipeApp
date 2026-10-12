using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;

namespace RecipeApp.API.Services.RecipePacks;

/// <summary>
/// Reads recipe packs (Phase 10 §4.4.1). Read-only: a pack is written by hand or by another tool,
/// never by the app.
/// </summary>
public class RecipePackFileStore(IOptions<RecipePackOptions> options, IHostEnvironment environment)
{
    /// <summary>
    /// Strict, because a pack is usually written by hand. A missing required field, a null where
    /// the format forbids one, or an unknown property (most often a misspelt one, such as
    /// <c>"imgae"</c>) is an error rather than a silently dropped value.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy                 = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations           = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling               = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling                  = JsonCommentHandling.Skip,
        AllowTrailingCommas                  = true,
    };

    /// <summary>Absolute path of the built-in sample pack.</summary>
    public string SamplePackPath { get; } =
        IngredientCatalogueFileStore.ResolvePath(options.Value.SamplePackFilePath, environment);

    /// <summary>
    /// Reads a pack. Any <see cref="RecipePackFile.ContentVersion"/> is accepted; an unknown
    /// <see cref="RecipePackFile.FormatVersion"/> is not.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="RecipePackValidationException">The file is not a pack this build can read.</exception>
    public static async Task<RecipePackFile> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No recipe pack at {path}.", path);

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);

            // The format is checked before the full read, so a file in another shape is reported
            // as that rather than as whichever field it happens to lack.
            var formatVersion = JsonSerializer.Deserialize<FormatProbe>(json, ProbeOptions)?.FormatVersion;
            if (formatVersion != RecipePackFile.CurrentFormatVersion)
                throw new RecipePackValidationException(
                    [$"{path} is format version {formatVersion?.ToString() ?? "(none)"}; this build " +
                     $"reads format version {RecipePackFile.CurrentFormatVersion}."]);

            return JsonSerializer.Deserialize<RecipePackFile>(json, JsonOptions)
                ?? throw new RecipePackValidationException([$"{path} deserialised to nothing."]);
        }
        catch (JsonException ex)
        {
            throw new RecipePackValidationException([$"{path} is not a readable recipe pack: {ex.Message}"]);
        }
    }

    /// <summary>The probe ignores every other property, so it must not inherit the strict options.</summary>
    private static readonly JsonSerializerOptions ProbeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        AllowTrailingCommas  = true,
    };

    private sealed record FormatProbe(int? FormatVersion);
}

/// <summary>
/// A pack that cannot be imported, with every reason found. Nothing was written.
/// </summary>
public class RecipePackValidationException(IReadOnlyList<string> errors)
    : Exception($"The recipe pack has {errors.Count} error(s):\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
