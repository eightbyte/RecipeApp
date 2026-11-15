using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Llm;
using RecipeApp.API.Models;
using RecipeApp.API.Validators;

namespace RecipeApp.API.Services.Llm;

/// <summary>Reads the recorded LLM Calls back, for seeing what Scraping on a paid provider costs.</summary>
public class LlmUsageService(AppDbContext db)
{
    /// <remarks>The query is assumed valid, as <c>LlmUsageQueryValidator</c> runs before the handler.</remarks>
    public async Task<LlmUsageResponse> GetAsync(LlmUsageQuery query, CancellationToken ct = default)
    {
        var now  = DateTime.UtcNow;
        var from = query.From is { } f ? AsUtc(f) : now.AddDays(-LlmUsagePaging.DefaultDays);
        var to   = query.To is { } t ? AsUtc(t) : now;

        var calls = db.LlmCalls.AsNoTracking().Where(c => c.CreatedAt >= from && c.CreatedAt < to);

        if (query.Operation is not null)
        {
            var operation = Enum.Parse<LlmOperation>(query.Operation);
            calls = calls.Where(c => c.Operation == operation);
        }

        if (query.Provider is not null)
        {
            var provider = LlmProviderNames.Parse(query.Provider);
            calls = calls.Where(c => c.Provider == provider);
        }

        var totals = await calls
            .GroupBy(c => new { c.Provider, c.Model, c.Operation })
            .Select(g => new LlmUsageTotalResponse(
                g.Key.Provider,
                g.Key.Model,
                g.Key.Operation,
                g.Count(),
                g.Sum(c => (long?)c.InputTokens) ?? 0,
                g.Sum(c => (long?)c.CacheReadTokens) ?? 0,
                g.Sum(c => (long?)c.CacheWriteTokens) ?? 0,
                g.Sum(c => (long?)c.OutputTokens) ?? 0,
                g.Sum(c => c.EstimatedCostUsd) ?? 0m,
                g.Count(c => c.EstimatedCostUsd == null)))
            .ToListAsync(ct);

        var totalCount = totals.Sum(group => group.Calls);

        var pageSize = query.PageSize ?? LlmUsagePaging.DefaultPageSize;
        // Clamped because a huge page number overflows int, and skipping int.MaxValue rows is already past the end.
        var skip = (int)Math.Min(((long)(query.Page ?? 1) - 1) * pageSize, int.MaxValue);

        var page = await calls
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new LlmUsageResponse(
            totals
                .OrderBy(group => group.Provider)
                .ThenBy(group => group.Model)
                .ThenBy(group => group.Operation)
                .ToList(),
            page.Select(c => c.ToResponse()).ToList(),
            totalCount);
    }

    /// <summary>A query-string time with no zone is read as UTC, because every LLM Call is stored in UTC.</summary>
    private static DateTime AsUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc   => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    };
}
