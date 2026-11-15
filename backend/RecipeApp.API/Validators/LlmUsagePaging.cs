namespace RecipeApp.API.Validators;

/// <summary>Range and page sizes for <c>GET /llm/usage</c>, each default applying when the client omits the parameter.</summary>
public static class LlmUsagePaging
{
    public const int DefaultDays     = 30;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize     = 100;
}
