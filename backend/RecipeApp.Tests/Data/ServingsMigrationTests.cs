using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RecipeApp.API.Data;
using Testcontainers.PostgreSql;

namespace RecipeApp.Tests.Data;

/// <summary>Stored Batch Sizes become the Servings they stood for: the recipe's Servings times the multiplier, rounded up and capped at 100.</summary>
public class ServingsMigrationTests : IAsyncLifetime
{
    private const string LastMigrationBeforeServings = "20261104035127_AddShoppingListItemSources";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(), o => o.MigrationsAssembly("RecipeApp.API"))
            .Options);

    [Fact]
    public async Task PlannedMealsAndItemSourcesStoreServings()
    {
        var planId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var ingredientId = Guid.NewGuid();
        var fiveServings = Guid.NewGuid();
        var oneServing = Guid.NewGuid();
        var sixtyServings = Guid.NewGuid();

        var halfOfFive = Guid.NewGuid();
        var regularOfFive = Guid.NewGuid();
        var doubleOfFive = Guid.NewGuid();
        var unknownOfFive = Guid.NewGuid();
        var halfOfOne = Guid.NewGuid();
        var doubleOfSixty = Guid.NewGuid();
        var sourceHalfOfFive = Guid.NewGuid();
        var sourceOfDeletedRecipe = Guid.NewGuid();

        await using (var before = CreateDbContext())
        {
            await before.GetService<IMigrator>().MigrateAsync(LastMigrationBeforeServings);

            foreach (var (recipeId, servings) in new[] { (fiveServings, 5), (oneServing, 1), (sixtyServings, 60) })
                await before.Database.ExecuteSqlAsync(
                    $@"INSERT INTO ""Recipes"" (""Id"", ""Name"", ""Servings"") VALUES ({recipeId}, 'Soup', {servings})");
            await before.Database.ExecuteSqlAsync(
                $@"INSERT INTO ""MealPlans"" (""Id"", ""Name"", ""IsActive"") VALUES ({planId}, 'Week', true)");

            var plannedMeals = new[]
            {
                (halfOfFive, fiveServings, "HALF"),
                (regularOfFive, fiveServings, "REGULAR"),
                (doubleOfFive, fiveServings, "DOUBLE"),
                (unknownOfFive, fiveServings, "TRIPLE"),
                (halfOfOne, oneServing, "HALF"),
                (doubleOfSixty, sixtyServings, "DOUBLE"),
            };
            var displayOrder = 0;
            foreach (var (plannedMealId, recipeId, batchSize) in plannedMeals)
            {
                displayOrder++;
                await before.Database.ExecuteSqlAsync($@"
                    INSERT INTO ""PlannedMeals"" (""Id"", ""MealPlanId"", ""RecipeId"", ""BatchSize"", ""DisplayOrder"")
                    VALUES ({plannedMealId}, {planId}, {recipeId}, {batchSize}, {displayOrder})");
            }

            await before.Database.ExecuteSqlAsync(
                $@"INSERT INTO ""Ingredients"" (""Id"", ""Name"", ""DisplayName"") VALUES ({ingredientId}, 'onion', 'Onion')");
            await before.Database.ExecuteSqlAsync(
                $@"INSERT INTO ""ShoppingLists"" (""Id"", ""MealPlanId"") VALUES ({listId}, {planId})");
            await before.Database.ExecuteSqlAsync($@"
                INSERT INTO ""ShoppingListItems"" (""Id"", ""ShoppingListId"", ""IngredientId"", ""IsChecked"", ""IsCustom"", ""NeedsReview"", ""DisplayOrder"")
                VALUES ({itemId}, {listId}, {ingredientId}, false, false, false, 1)");
            await before.Database.ExecuteSqlAsync($@"
                INSERT INTO ""ShoppingListItemSources"" (""Id"", ""ShoppingListItemId"", ""RecipeId"", ""RecipeName"", ""BatchSize"", ""DisplayOrder"")
                VALUES ({sourceHalfOfFive}, {itemId}, {fiveServings}, 'Soup', 'HALF', 1),
                       ({sourceOfDeletedRecipe}, {itemId}, NULL, 'Stew', 'DOUBLE', 2)");
        }

        await using (var after = CreateDbContext())
            await after.Database.MigrateAsync();

        await using var read = CreateDbContext();
        var servingsByPlannedMealId = await read.PlannedMeals.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Servings);
        var servingsBySourceId = await read.ShoppingListItemSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Servings);

        servingsByPlannedMealId.Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [halfOfFive] = 3,
            [regularOfFive] = 5,
            [doubleOfFive] = 10,
            [unknownOfFive] = 5,
            [halfOfOne] = 1,
            [doubleOfSixty] = 100,
        });
        servingsBySourceId.Should().BeEquivalentTo(new Dictionary<Guid, int?>
        {
            [sourceHalfOfFive] = 3,
            [sourceOfDeletedRecipe] = null,
        });
    }
}
