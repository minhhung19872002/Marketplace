using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ShopHub.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public interface IPagedRequest
{
    int Page { get; }
    int PageSize { get; }
}

public static class PagingLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public static class PagingExtensions
{
    /// <summary>
    /// Count and fetch on the SAME filtered query. Only accepts an ordered query; the ordering chain
    /// must end with a unique key (enforced by StablePagingOrderTests).
    /// </summary>
    public static Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IOrderedQueryable<T> query, IPagedRequest request, CancellationToken ct) =>
        query.ToPagedResultAsync(x => x, request, ct);

    /// <summary>Order on the entity, then project each page row (projection runs after Skip/Take).</summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> query,
        System.Linq.Expressions.Expression<Func<TSource, TResult>> selector,
        IPagedRequest request,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(selector)
            .ToListAsync(ct);
        return new PagedResult<TResult>(items, total, request.Page, request.PageSize);
    }
}

public static class PagingRules
{
    public static void ApplyPagingRules<T>(this AbstractValidator<T> validator) where T : IPagedRequest
    {
        validator.RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Số trang phải từ 1 trở lên.");
        validator.RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagingLimits.MaxPageSize)
            .WithMessage($"Số dòng mỗi trang phải từ 1 đến {PagingLimits.MaxPageSize}.");
    }
}
