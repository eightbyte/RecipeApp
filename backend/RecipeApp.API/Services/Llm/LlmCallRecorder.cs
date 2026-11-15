using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services.Llm;

/// <summary>
/// Writes each LLM Call through a context of its own, so the caller's change tracker never saves it by accident and the
/// caller's transaction, such as a rolled-back seed stage, never takes it away.
/// </summary>
public sealed class LlmCallRecorder(IDbContextFactory<AppDbContext> contexts) : ILlmCallRecorder
{
    public async Task RecordAsync(LlmCall call, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.LlmCalls.Add(call);
        await db.SaveChangesAsync(ct);
    }
}
