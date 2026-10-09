using MediatR;

namespace ShopHub.Application.Features.Admin;

/// <summary>Đạt / cảnh báo / chưa đạt.</summary>
public enum CheckStatus
{
    Pass,
    Warn,
    Fail,
}

/// <param name="Blocking">A failing blocking item means the platform must not open for real sales yet.</param>
public record LaunchCheckDto(string Id, string Group, string Title, CheckStatus Status, string Detail, bool Blocking);

/// <param name="Ready">Every blocking item passes.</param>
public record LaunchChecklistDto(bool Ready, int Passed, int Total, IReadOnlyList<LaunchCheckDto> Items, DateTimeOffset CheckedAt);

/// <summary>
/// "Danh sách kiểm trước khi mở bán" (G4-D): every item is measured on the running system (parameters, .env-driven
/// providers, data, last backup), nothing is ticked by hand. The procedure around it is docs/09-kiem-tra-truoc-mo-ban.md.
/// </summary>
public interface ILaunchChecklist
{
    Task<LaunchChecklistDto> RunAsync(CancellationToken ct);
}

public record LaunchChecklistQuery : IRequest<LaunchChecklistDto>;

public sealed class LaunchChecklistHandler(ILaunchChecklist checklist) : IRequestHandler<LaunchChecklistQuery, LaunchChecklistDto>
{
    public Task<LaunchChecklistDto> Handle(LaunchChecklistQuery request, CancellationToken ct) => checklist.RunAsync(ct);
}
