using System.Linq.Expressions;

namespace RecipeApp.API.Services;

public static class QueryableSortExtensions
{
    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(
        this IQueryable<T> source, Expression<Func<T, TKey>> keySelector, bool ascending)
        => ascending ? source.OrderBy(keySelector) : source.OrderByDescending(keySelector);

    public static IOrderedQueryable<T> ThenByDirection<T, TKey>(
        this IOrderedQueryable<T> source, Expression<Func<T, TKey>> keySelector, bool ascending)
        => ascending ? source.ThenBy(keySelector) : source.ThenByDescending(keySelector);
}
