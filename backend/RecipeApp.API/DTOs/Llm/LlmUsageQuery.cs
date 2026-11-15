using Microsoft.AspNetCore.Mvc;

namespace RecipeApp.API.DTOs.Llm;

/// <summary>The query string of <c>GET /llm/usage</c>, bound with <c>[AsParameters]</c>; the explicit names keep the documented parameters camelCase.</summary>
/// <param name="From">Inclusive, read as UTC when it names no zone, and <c>LlmUsagePaging.DefaultDays</c> ago when null.</param>
/// <param name="To">Exclusive, read as UTC when it names no zone, and now when null.</param>
/// <param name="Operation">One <c>LlmOperation</c> name, such as <c>ScrapeUrl</c>.</param>
/// <param name="Provider">One provider as the API names it, <c>modelServer</c> or <c>anthropic</c>.</param>
/// <param name="Page">Counted from 1; null means 1.</param>
/// <param name="PageSize">At most <c>LlmUsagePaging.MaxPageSize</c>; null means <c>LlmUsagePaging.DefaultPageSize</c>.</param>
public record LlmUsageQuery(
    [FromQuery(Name = "from")]      DateTime? From,
    [FromQuery(Name = "to")]        DateTime? To,
    [FromQuery(Name = "operation")] string? Operation,
    [FromQuery(Name = "provider")]  string? Provider,
    [FromQuery(Name = "page")]      int? Page,
    [FromQuery(Name = "pageSize")]  int? PageSize
);
