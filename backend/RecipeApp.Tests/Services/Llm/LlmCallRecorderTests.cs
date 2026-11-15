using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Helpers;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>An LLM Call is kept whatever becomes of the caller's own unit of work.</summary>
[Collection("Database")]
public class LlmCallRecorderTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => db.TruncateAllAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class FixtureContexts(DatabaseFixture db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.CreateDbContext();
    }

    private LlmCallRecorder Recorder() => new(new FixtureContexts(db));

    private static LlmCall Call() => new()
    {
        Id               = Guid.NewGuid(),
        CreatedAt        = DateTime.UtcNow,
        Provider         = LlmProvider.Anthropic,
        Model            = "claude-opus-5-5",
        Operation        = LlmOperation.SeedNormalise,
        CorrelationId    = Guid.NewGuid(),
        Outcome          = LlmCallOutcome.FailedExtraction,
        DurationMs       = 1234,
        InputTokens      = 1200,
        CacheReadTokens  = 300,
        CacheWriteTokens = 40,
        OutputTokens     = 950,
        EstimatedCostUsd = 0.02406m,
    };

    [Fact]
    public async Task ARecordedCall_IsStoredWithEveryField()
    {
        var call = Call();

        await Recorder().RecordAsync(call);

        await using var read = db.CreateDbContext();
        (await read.LlmCalls.SingleAsync()).Should().BeEquivalentTo(call, o => o
            .Using<DateTime>(c => c.Subject.Should().BeCloseTo(c.Expectation, TimeSpan.FromMilliseconds(1)))
            .WhenTypeIs<DateTime>());
    }

    /// <summary>A seed stage that fails rolls its own work back, and the calls it paid for must not vanish with it, nor save its pending changes.</summary>
    [Fact]
    public async Task ACallRecordedInsideACallersTransaction_SurvivesItsRollback_AndSavesNoneOfTheCallersChanges()
    {
        var call = Call();

        await using (var caller = db.CreateDbContext())
        {
            await using var transaction = await caller.Database.BeginTransactionAsync();
            caller.Ingredients.Add(TestDataBuilder.Ingredient("onion", "Onion"));
            await caller.SaveChangesAsync();
            caller.Ingredients.Add(TestDataBuilder.Ingredient("garlic", "Garlic"));

            await Recorder().RecordAsync(call);

            await transaction.RollbackAsync();
        }

        await using var read = db.CreateDbContext();
        (await read.LlmCalls.SingleAsync()).Id.Should().Be(call.Id);
        (await read.Ingredients.AnyAsync()).Should().BeFalse();
    }
}
