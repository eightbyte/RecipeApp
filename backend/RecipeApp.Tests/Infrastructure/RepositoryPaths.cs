namespace RecipeApp.Tests.Infrastructure;

/// <summary>
/// Locates source-tree files that tests check as artefacts, such as the shipped ingredient
/// catalogue and the local MyPlate cache. Found by walking up from the test binaries, so it works
/// from any build output directory.
/// </summary>
public static class RepositoryPaths
{
    private const string ApiProjectDirectory = "RecipeApp.API";
    private const string ApiProjectFile      = "RecipeApp.API.csproj";

    /// <summary>The API project directory, which is the app's content root.</summary>
    public static string ApiContentRoot { get; } = FindApiContentRoot();

    /// <summary>The shipped catalogue in the source tree.</summary>
    public static string ShippedCataloguePath =>
        Path.Combine(ApiContentRoot, "seed-data", "ingredient-catalogue.json");

    /// <summary>The shipped sample recipe pack in the source tree.</summary>
    public static string ShippedSamplePackPath =>
        Path.Combine(ApiContentRoot, "seed-data", "sample-recipes.json");

    /// <summary>The local MyPlate cache. Gitignored, so it exists only on a machine that harvested it.</summary>
    public static string MyPlateCacheDirectory => Path.Combine(ApiContentRoot, "seed-data", "myplate");

    private static string FindApiContentRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ApiProjectDirectory);
            if (File.Exists(Path.Combine(candidate, ApiProjectFile))) return candidate;
        }

        throw new DirectoryNotFoundException(
            $"No {ApiProjectDirectory}/{ApiProjectFile} above {AppContext.BaseDirectory}.");
    }
}
