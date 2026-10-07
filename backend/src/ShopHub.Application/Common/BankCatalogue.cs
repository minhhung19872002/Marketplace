using MediatR;

namespace ShopHub.Application.Common;

/// <summary>
/// The banks a shop or a buyer can receive money at (D6, L098) — one list on the server, read by every form
/// (<c>GET /api/site/banks</c>) and checked by every command that takes a bank code.
/// </summary>
public static class BankCatalogue
{
    public sealed record Bank(string Code, string Name);

    public static readonly IReadOnlyList<Bank> All =
    [
        new("VCB", "Vietcombank"),
        new("TCB", "Techcombank"),
        new("BIDV", "BIDV"),
        new("VTB", "VietinBank"),
        new("ACB", "ACB"),
        new("MB", "MB Bank"),
        new("TPB", "TPBank"),
        new("VPB", "VPBank"),
        new("AGR", "Agribank"),
        new("STB", "Sacombank"),
        new("VIB", "VIB"),
        new("HDB", "HDBank"),
    ];

    public const string UnknownMessage = "Ngân hàng không có trong danh sách hỗ trợ.";

    public static bool IsKnown(string? code) => code is not null && All.Any(b => b.Code == code);
}

public record BanksQuery : IRequest<IReadOnlyList<BankCatalogue.Bank>>;

public sealed class BanksHandler : IRequestHandler<BanksQuery, IReadOnlyList<BankCatalogue.Bank>>
{
    public Task<IReadOnlyList<BankCatalogue.Bank>> Handle(BanksQuery request, CancellationToken ct) => Task.FromResult(BankCatalogue.All);
}
