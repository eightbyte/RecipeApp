using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RecipeApp.API.Data;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.Tests.Infrastructure;
using static RecipeApp.Tests.RecipePacks.RecipePackFixtures;

namespace RecipeApp.Tests.RecipePacks;

/// <summary>
/// <c>import-recipes</c> end to end (Phase 10 §4.4.3), against a database created empty for each
/// test in the shared container, so the command's own migration and catalogue import are what make
/// it usable. The catalogue is the shipped one.
/// </summary>
[Collection("Database")]
public class ImportRecipesCommandRunTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly string _databaseName = "import_recipes_" + Guid.NewGuid().ToString("N");
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recipeapp-import-recipes-tests", Guid.NewGuid().ToString("N"));

    private RecipeAppFactory _factory = null!;
    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        // A database name is an identifier, which cannot be a parameter. It is generated above.
        var createDatabase = $"CREATE DATABASE \"{_databaseName}\"";
        await using (var admin = db.CreateDbContext())
            await admin.Database.ExecuteSqlRawAsync(createDatabase);

        _connectionString = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = _databaseName }.ConnectionString;
        _factory = new RecipeAppFactory(_connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        var dropDatabase = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await using (var admin = db.CreateDbContext())
            await admin.Database.ExecuteSqlRawAsync(dropDatabase);

        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString, o => o.MigrationsAssembly("RecipeApp.API"))
            .Options);

    private Task<int> RunAsync(params string[] args) =>
        ImportRecipesCommand.RunAsync(_factory.Services, NullLogger.Instance, args);

    private static RecipePackFile ValidPack() => Pack(
        Recipe("Garlic Rice",
            [Row("rice", 200, "g"), Row("garlic", 2, "pcs"), Row("olive oil", 1, "tbsp"), Row("salt", null, null, "to taste")],
            [Step("Fry the garlic in the oil.", "garlic", "olive oil"), Step("Add the rice and salt.", "rice", "salt")],
            sourceUrl: "https://example.com/garlic-rice"));

    [Fact]
    public async Task OnAnEmptyDatabaseItMigratesLoadsTheCatalogueAndImports()
    {
        var path = await WriteAsync(_root, ValidPack());

        var exitCode = await RunAsync(path);

        exitCode.Should().Be(0);
        await using var context = CreateDbContext();
        (await context.Ingredients.CountAsync()).Should().BeGreaterThan(100, "the shipped catalogue was loaded");
        (await context.Recipes.SingleAsync()).Name.Should().Be("Garlic Rice");
    }

    [Fact]
    public async Task SkippedRecipesStillExitZero()
    {
        var path = await WriteAsync(_root, ValidPack());
        await RunAsync(path);

        (await RunAsync(path)).Should().Be(0);

        await using var context = CreateDbContext();
        (await context.Recipes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AValidationFailureExitsOneAndWritesNoRecipe()
    {
        var path = await WriteAsync(_root, Pack(Recipe("Mystery", [Row("unobtainium")])));

        (await RunAsync(path)).Should().Be(1);

        await using var context = CreateDbContext();
        (await context.Recipes.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AMissingPackExitsOne() =>
        (await RunAsync(Path.Combine(_root, "absent.json"))).Should().Be(1);

    [Fact]
    public async Task AUsageErrorExitsOne() =>
        (await RunAsync("--force")).Should().Be(1);

    [Fact]
    public async Task ItsForceDoesNotForceImportTheCatalogue()
    {
        // A non-force catalogue import fills nulls only, so a hand correction survives it. A forced
        // one would put the artefact's display name back.
        var path = await WriteAsync(_root, ValidPack());
        await RunAsync(path);

        await using (var context = CreateDbContext())
        {
            var garlic = await context.Ingredients.SingleAsync(row => row.Name == "garlic");
            garlic.DisplayName = "Hand-corrected garlic";
            await context.SaveChangesAsync();
        }

        (await RunAsync(path, ImportRecipesCommand.ForceFlag)).Should().Be(0);

        await using var check = CreateDbContext();
        (await check.Ingredients.SingleAsync(row => row.Name == "garlic"))
            .DisplayName.Should().Be("Hand-corrected garlic");
    }
}
