using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using Testcontainers.PostgreSql;

namespace RecipeApp.Tests.Data;

/// <summary>Planned meals stored under the old meal-plan-recipe schema survive the rename, keeping their Batch Sizes.</summary>
public class PlannedMealMigrationTests : IAsyncLifetime
{
    private const string LastMigrationBeforeRename = "20261012184335_AddSeedLedger";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(), o => o.MigrationsAssembly("RecipeApp.API"))
            .Options);

    [Fact]
    public async Task ExistingPlannedMealsKeepTheirBatchSizes()
    {
        var planId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var batchSizeByPlannedMealId = new Dictionary<Guid, string>
        {
            [Guid.NewGuid()] = BatchSize.Half,
            [Guid.NewGuid()] = BatchSize.Regular,
            [Guid.NewGuid()] = BatchSize.Double,
        };

        await using (var beforeRename = CreateDbContext())
        {
            await beforeRename.GetService<IMigrator>().MigrateAsync(LastMigrationBeforeRename);

            await beforeRename.Database.ExecuteSqlAsync(
                $@"INSERT INTO ""Recipes"" (""Id"", ""Name"") VALUES ({recipeId}, 'Soup')");
            await beforeRename.Database.ExecuteSqlAsync(
                $@"INSERT INTO ""MealPlans"" (""Id"", ""Name"", ""IsActive"") VALUES ({planId}, 'Week', true)");

            var displayOrder = 0;
            foreach (var (plannedMealId, batchSize) in batchSizeByPlannedMealId)
            {
                displayOrder++;
                await beforeRename.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""MealPlanRecipes"" (""Id"", ""MealPlanId"", ""RecipeId"", ""PortionSize"", ""DisplayOrder"")
                    VALUES ({plannedMealId}, {planId}, {recipeId}, {batchSize}, {displayOrder})");
            }
        }

        await using (var afterRename = CreateDbContext())
            await afterRename.Database.MigrateAsync();

        await using var read = CreateDbContext();
        var plannedMeals = await read.PlannedMeals.AsNoTracking().ToListAsync();

        plannedMeals.ToDictionary(m => m.Id, m => m.BatchSize)
            .Should().BeEquivalentTo(batchSizeByPlannedMealId);
        plannedMeals.Should().OnlyContain(m => m.MealPlanId == planId && m.RecipeId == recipeId);
    }

    [Fact]
    public async Task NoKeyOrIndexKeepsTheOldTableName()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var namesOfTable = await db.Database.SqlQuery<string>($@"
            SELECT conname::text AS ""Value"" FROM pg_constraint WHERE conrelid = '""PlannedMeals""'::regclass
            UNION
            SELECT indexname::text FROM pg_indexes WHERE tablename = 'PlannedMeals'").ToListAsync();

        namesOfTable.Should().BeEquivalentTo(
            "PK_PlannedMeals",
            "FK_PlannedMeals_MealPlans_MealPlanId",
            "FK_PlannedMeals_Recipes_RecipeId",
            "IX_PlannedMeals_MealPlanId",
            "IX_PlannedMeals_RecipeId");
    }
}
