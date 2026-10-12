using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Data;

/// <summary>
/// The first-run seed ledger table against a real Postgres (Phase 10 §4.1).
///
/// <para>The primary key on <c>Key</c> is the race guard the first-run seeder relies on, so it
/// is proved here rather than assumed from the model configuration.</para>
/// </summary>
[Collection("Database")]
public class SeedLedgerTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static SeedLedgerEntry Entry(string key, string outcome = SeedLedgerOutcome.Applied) => new()
    {
        Key             = key,
        Outcome         = outcome,
        ArtefactVersion = 1,
    };

    [Fact]
    public async Task AnEntryRoundTripsThroughPostgres()
    {
        var appliedAt = new DateTime(2026, 10, 12, 18, 30, 0, DateTimeKind.Utc);

        await using (var write = db.CreateDbContext())
        {
            var entry = Entry(SeedLedgerKeys.IngredientCatalogue, SeedLedgerOutcome.SkippedExistingData);
            entry.ArtefactVersion = 3;
            entry.AppliedAt       = appliedAt;
            write.SeedLedger.Add(entry);
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        var row = await read.SeedLedger.AsNoTracking().SingleAsync();

        row.Key.Should().Be(SeedLedgerKeys.IngredientCatalogue);
        row.Outcome.Should().Be(SeedLedgerOutcome.SkippedExistingData);
        row.ArtefactVersion.Should().Be(3);
        row.AppliedAt.Should().Be(appliedAt);
        row.AppliedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task EachStepHasItsOwnRow()
    {
        await using (var write = db.CreateDbContext())
        {
            write.SeedLedger.AddRange(SeedLedgerKeys.All.Select(key => Entry(key)));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateDbContext();
        (await read.SeedLedger.Select(l => l.Key).ToListAsync())
            .Should().BeEquivalentTo(SeedLedgerKeys.All);
    }

    [Fact]
    public async Task ASecondRowForTheSameStepIsRejected()
    {
        // Two hosts starting on a fresh database each open a separate context and each try to
        // record the step. The database, not the seeder, must make the loser fail.
        await using (var first = db.CreateDbContext())
        {
            first.SeedLedger.Add(Entry(SeedLedgerKeys.SampleRecipes));
            await first.SaveChangesAsync();
        }

        await using var second = db.CreateDbContext();
        second.SeedLedger.Add(Entry(SeedLedgerKeys.SampleRecipes));

        var save = () => second.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();

        await using var read = db.CreateDbContext();
        (await read.SeedLedger.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AppliedAtDefaultsInTheDatabaseWhenNotSet()
    {
        await using (var write = db.CreateDbContext())
        {
            await write.Database.ExecuteSqlRawAsync(
                """INSERT INTO "SeedLedger" ("Key", "Outcome", "ArtefactVersion") VALUES ({0}, {1}, 1)""",
                SeedLedgerKeys.SampleRecipes, SeedLedgerOutcome.Applied);
        }

        await using var read = db.CreateDbContext();
        var row = await read.SeedLedger.AsNoTracking().SingleAsync();

        row.AppliedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void KeysAreDistinct() =>
        SeedLedgerKeys.All.Should().OnlyHaveUniqueItems();

    [Fact]
    public void OutcomesAreDistinctAndValid()
    {
        SeedLedgerOutcome.All.Should().OnlyHaveUniqueItems();
        SeedLedgerOutcome.All.Should().OnlyContain(o => SeedLedgerOutcome.IsValid(o));
        SeedLedgerOutcome.IsValid("Unknown").Should().BeFalse();
    }
}
