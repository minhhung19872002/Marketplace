using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;

namespace ShopHub.Application.Features.SystemConfig;

public record SystemParameterDto(
    string Key,
    string Value,
    string DataType,
    string Group,
    string Name,
    string Description,
    uint Version,
    DateTimeOffset? UpdatedAt,
    // Row id: the audit trail of a parameter is /api/admin/audit-logs?entity=SystemParameter&entityId={Id}
    Guid Id = default);

public record ListSystemParametersQuery(string? Group) : IRequest<IReadOnlyList<SystemParameterDto>>;

public sealed class ListSystemParametersHandler(IApplicationDbContext db)
    : IRequestHandler<ListSystemParametersQuery, IReadOnlyList<SystemParameterDto>>
{
    public async Task<IReadOnlyList<SystemParameterDto>> Handle(ListSystemParametersQuery request, CancellationToken ct)
    {
        var query = db.SystemParameters.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Group))
        {
            var group = request.Group.Trim().ToUpperInvariant();
            query = query.Where(p => p.Group == group);
        }

        return await query
            .OrderBy(p => p.Group).ThenBy(p => p.Key)
            .Select(p => new SystemParameterDto(
                p.Key, p.Value, p.DataType.ToString(), p.Group, p.Name, p.Description, p.Version, p.UpdatedAt, p.Id))
            .ToListAsync(ct);
    }
}
