using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>
/// <c>Ingredient.Aliases</c> against a real Postgres (Phase 9.3 §4.6).
///
/// <para>A <c>text[]</c> column is the one shape in this schema where the provider, not EF, decides
/// whether the round trip works — and the whole alias mechanism is silently useless if an empty
/// array comes back null, so it is worth proving rather than assuming.</para>
/// </summary>
[Collection("Database")]
public class IngredientAliasTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Ingredient Row(string name, string[]? aliases = null) => new()
    {
        Id          = Guid.NewGuid(),
        Name        = name,
        DisplayName = name,
        Category    = IngredientCategory.Produce,
        Aliases     = aliases ?? [],
    };

    [Fact]
    public async Task AliasesRoundTripThroughPostgres()
    {
        await using (var write = db.CreateDbContext())
        {
            write.Ingredients.Add(Row("bell pepper", ["capsicum", "sweet pepper", "green pepper"]));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        var row = await read.Ingredients.AsNoTracking().SingleAsync();

        row.Aliases.Should().Equal("capsicum", "sweet pepper", "green pepper");
    }

    [Fact]
    public async Task TheDefaultIsAnEmptyArrayAndNeverNull()
    {
        // Every consumer enumerates Aliases without a null guard, so a null here would be a
        // NullReferenceException in the middle of a thousand-recipe import.
        await using (var write = db.CreateDbContext())
        {
            write.Ingredients.Add(Row("onion"));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        var row = await read.Ingredients.AsNoTracking().SingleAsync();

        row.Aliases.Should().NotBeNull();
        row.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task AliasesCanBeReplacedOnAnExistingRow()
    {
        await using (var write = db.CreateDbContext())
        {
            write.Ingredients.Add(Row("chickpeas", ["ceci beans"]));
            await write.SaveChangesAsync();
        }

        await using (var update = db.CreateDbContext())
        {
            var row = await update.Ingredients.SingleAsync();
            row.Aliases = ["garbanzo beans", "chick peas"];
            await update.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        (await read.Ingredients.AsNoTracking().SingleAsync())
            .Aliases.Should().Equal("garbanzo beans", "chick peas");
    }

    [Fact]
    public async Task AnAliasIsQueryableServerSide()
    {
        // ResolveOrCreateIngredientAsync resolves an alias with a Contains predicate, which has to
        // translate to SQL rather than silently pulling the catalogue into memory.
        await using (var write = db.CreateDbContext())
        {
            write.Ingredients.Add(Row("chickpeas", ["garbanzo beans"]));
            write.Ingredients.Add(Row("lentils"));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        var match = await read.Ingredients
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Aliases.Contains("garbanzo beans"));

        match.Should().NotBeNull();
        match!.Name.Should().Be("chickpeas");
    }
}
