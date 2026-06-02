using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using Testcontainers.PostgreSql;

namespace RecipeApp.Tests.Infrastructure;

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16").Build();

    private RecipeAppFactory? _factory;

    public string ConnectionString => _container.GetConnectionString();

    public RecipeAppFactory Factory => _factory!;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsAssembly("RecipeApp.API"))
            .Options;
        return new AppDbContext(options);
    }

    public async ValueTask TruncateAllAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                ""RecipeStepIngredients"",
                ""RecipeSteps"",
                ""RecipeIngredients"",
                ""MealPlanRecipes"",
                ""MealPlans"",
                ""Recipes"",
                ""Ingredients""
            RESTART IDENTITY CASCADE;
        ");
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        _factory = new RecipeAppFactory(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        await _container.DisposeAsync();
    }
}
