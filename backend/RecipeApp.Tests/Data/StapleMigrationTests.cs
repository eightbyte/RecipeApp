using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeApp.API.Data;
using Testcontainers.PostgreSql;

namespace RecipeApp.Tests.Data;

/// <summary>An existing database gains salt, black pepper and water as its only Staples when it migrates.</summary>
public class StapleMigrationTests : IAsyncLifetime
{
    private const string LastMigrationBeforeStaples = "20261101201859_RenamePlannedMealAndBatchSize";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(), o => o.MigrationsAssembly("RecipeApp.API"))
            .Options);

    [Fact]
    public async Task OnlySaltBlackPepperAndWaterBecomeStaples()
    {
        await using (var beforeStaples = CreateDbContext())
        {
            await beforeStaples.GetService<IMigrator>().MigrateAsync(LastMigrationBeforeStaples);

            foreach (var name in new[] { "salt", "black pepper", "water", "onion", "salt & pepper" })
                await beforeStaples.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""Ingredients"" (""Id"", ""Name"", ""DisplayName"")
                    VALUES ({Guid.NewGuid()}, {name}, {name})");
        }

        await using (var afterStaples = CreateDbContext())
            await afterStaples.Database.MigrateAsync();

        await using var read = CreateDbContext();
        var stapleNames = await read.Ingredients.AsNoTracking()
            .Where(i => i.IsStaple)
            .Select(i => i.Name)
            .ToListAsync();

        stapleNames.Should().BeEquivalentTo("salt", "black pepper", "water");
    }
}
