namespace RecipeApp.API.Validators;

/// <summary>Page sizes for <c>GET /recipes</c>, the default applying when the client omits <c>pageSize</c>.</summary>
public static class RecipeListPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize     = 100;
}
