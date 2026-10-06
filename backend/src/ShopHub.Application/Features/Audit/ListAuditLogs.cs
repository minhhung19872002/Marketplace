using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;

namespace ShopHub.Application.Features.Audit;

public record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string? Ip,
    string Action,
    string Entity,
    string? EntityId,
    string? OldValue,
    string? NewValue,
    DateTimeOffset OccurredAt);

public record ListAuditLogsQuery(
    int Page = 1,
    int PageSize = PagingLimits.DefaultPageSize,
    Guid? UserId = null,
    string? Action = null,
    string? Entity = null,
    string? EntityId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<PagedResult<AuditLogDto>>, IPagedRequest, IDateRangeRequest;

public sealed class ListAuditLogsValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsValidator()
    {
        this.ApplyPagingRules();
        this.ApplyDateRangeRules();
    }
}

public sealed class ListAuditLogsHandler(IApplicationDbContext db) : IRequestHandler<ListAuditLogsQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> Handle(ListAuditLogsQuery request, CancellationToken ct)
    {
        // Filter in SQL first, then page
        var query = db.AuditLogs.AsNoTracking();
        if (request.UserId is { } userId) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(a => a.Action == request.Action.ToUpper());
        if (!string.IsNullOrWhiteSpace(request.Entity)) query = query.Where(a => a.Entity == request.Entity);
        if (!string.IsNullOrWhiteSpace(request.EntityId)) query = query.Where(a => a.EntityId == request.EntityId);
        if (request.From is { } from) query = query.Where(a => a.OccurredAt >= from);
        if (request.To is { } to) query = query.Where(a => a.OccurredAt <= to);

        return await query
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .ToPagedResultAsync(
                a => new AuditLogDto(a.Id, a.UserId,
                    db.Users.IgnoreQueryFilters().Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault(),
                    a.Ip, a.Action, a.Entity, a.EntityId, a.OldValue, a.NewValue, a.OccurredAt),
                request, ct);
    }
}
