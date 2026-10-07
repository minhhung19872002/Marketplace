using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Domain.Common;
using ShopHub.Domain.Finance;

namespace ShopHub.Api.Controllers;

[Route("api/wallet")]
[OwnerGuarded("Ví ShopHub, tài khoản ngân hàng và lệnh nạp / rút của chính người gọi (user_id lọc trong câu SQL); của người khác trả 404.")]
public sealed class ShopHubWalletController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<WalletDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new GetWalletQuery(page, pageSize), ct));

    /// <summary>OTP to the caller's own phone — needed to set the PIN or add a bank account.</summary>
    [HttpPost("otp")]
    [ProducesResponseType<ApiResponse<OtpIssued>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Otp(CancellationToken ct) =>
        OkData(await Sender.Send(new SendFinanceOtpCommand(), ct), "Đã gửi mã xác thực tới số điện thoại của bạn.");

    public record PinBody(string OtpCode, string Pin);

    [HttpPost("pin")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPin([FromBody] PinBody body, CancellationToken ct)
    {
        await Sender.Send(new SetWalletPinCommand(body.OtpCode, body.Pin), ct);
        return OkData<object?>(null, "Đã đặt mật khẩu Ví ShopHub.");
    }

    public record TopupBody(long Amount, Domain.Sales.PaymentMethod? Method);

    [HttpPost("topups")]
    [ProducesResponseType<ApiResponse<TopupStarted>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Topup([FromBody] TopupBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateTopupCommand(body.Amount, body.Method), ct));

    /// <summary>Online gateways a top-up can go through (those switched on by their keys).</summary>
    [HttpGet("topup-gateways")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<GatewayOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopupGateways(CancellationToken ct) => OkData(await Sender.Send(new TopupGatewaysQuery(), ct));

    [HttpGet("topups/{topupId:guid}")]
    [ProducesResponseType<ApiResponse<TopupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopup(Guid topupId, CancellationToken ct) => OkData(await Sender.Send(new GetTopupQuery(topupId), ct));

    public record BankBody(string BankCode, string AccountNo, string AccountName, string OtpCode);

    [HttpPost("bank-accounts")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBank([FromBody] BankBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddBankAccountCommand(body.BankCode, body.AccountNo, body.AccountName, body.OtpCode), ct), "Đã thêm tài khoản ngân hàng.");

    [HttpDelete("bank-accounts/{bankAccountId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveBank(Guid bankAccountId, CancellationToken ct)
    {
        await Sender.Send(new RemoveBankAccountCommand(bankAccountId), ct);
        return OkData<object?>(null, "Đã xoá tài khoản ngân hàng.");
    }

    public record WithdrawBody(Guid BankAccountId, long Amount, string Pin);

    [HttpPost("withdrawals")]
    [ProducesResponseType<ApiResponse<WithdrawalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new WalletWithdrawCommand(body.BankAccountId, body.Amount, body.Pin), ct), "Đã gửi yêu cầu rút tiền.");
}

[Route("api/seller/shops/{shopId:guid}/finance")]
[OwnerGuarded("Tài chính của shop mà người gọi là nhân viên có quyền FINANCE.*; shop khác trả 404.")]
public sealed class SellerFinanceController : ApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<ApiResponse<ShopFinanceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopFinanceSummaryQuery(shopId), ct));

    /// <summary>"Chờ giải ngân": completed orders not released yet, each with its breakdown and earliest release time.</summary>
    [HttpGet("pending")]
    [ProducesResponseType<ApiResponse<PagedResult<EarningDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending(Guid shopId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopPendingEarningsQuery(shopId, page, pageSize), ct));

    [HttpGet("released")]
    [ProducesResponseType<ApiResponse<PagedResult<EarningDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Released(Guid shopId, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopReleasedEarningsQuery(shopId, from, to, page, pageSize), ct));

    [HttpGet("transactions")]
    [ProducesResponseType<ApiResponse<PagedResult<WalletEntryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Transactions(Guid shopId, [FromQuery] LedgerAccountType account = LedgerAccountType.ShopAvailable,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopTransactionsQuery(shopId, account, page, pageSize), ct));

    /// <summary>Báo cáo đối soát theo kỳ [from, to): <c>format=Xlsx|Pdf</c>.</summary>
    [HttpGet("report")]
    public async Task<IActionResult> Report(Guid shopId, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] ReportFormat format = ReportFormat.Xlsx, CancellationToken ct = default)
    {
        var file = await Sender.Send(new ShopSettlementReportQuery(shopId, from, to, format), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("fee-invoice")]
    public async Task<IActionResult> FeeInvoice(Guid shopId, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        var file = await Sender.Send(new ShopFeeInvoiceQuery(shopId, from, to), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("withdrawals")]
    [ProducesResponseType<ApiResponse<PagedResult<WithdrawalDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdrawals(Guid shopId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopWithdrawalsQuery(shopId, page, pageSize), ct));

    public record WithdrawBody(Guid BankAccountId, long Amount);

    [HttpPost("withdrawals")]
    [ProducesResponseType<ApiResponse<WithdrawalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid shopId, [FromBody] WithdrawBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new ShopWithdrawCommand(shopId, body.BankAccountId, body.Amount), ct), "Đã gửi yêu cầu rút tiền.");

    [HttpPost("otp")]
    [ProducesResponseType<ApiResponse<OtpIssued>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Otp(Guid shopId, CancellationToken ct) =>
        OkData(await Sender.Send(new SendShopFinanceOtpCommand(shopId), ct), "Đã gửi mã xác thực tới số điện thoại của bạn.");

    public record BankBody(string BankCode, string AccountNo, string AccountName, string OtpCode, bool MakeDefault);

    public record OtpBody(string OtpCode);

    /// <summary>Đặt làm mặc định — payouts go there; needs the finance OTP.</summary>
    [HttpPost("bank-accounts/{bankAccountId:guid}/default")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultBank(Guid shopId, Guid bankAccountId, [FromBody] OtpBody body, CancellationToken ct)
    {
        await Sender.Send(new SetDefaultShopBankAccountCommand(shopId, bankAccountId, body.OtpCode), ct);
        return OkData<object?>(null, "Đã đặt làm tài khoản nhận tiền mặc định.");
    }

    /// <summary>Xoá a bank account that is not the default and has no withdrawal on its way.</summary>
    [HttpDelete("bank-accounts/{bankAccountId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveBank(Guid shopId, Guid bankAccountId, CancellationToken ct)
    {
        await Sender.Send(new RemoveShopBankAccountCommand(shopId, bankAccountId), ct);
        return OkData<object?>(null, "Đã xoá tài khoản ngân hàng.");
    }

    [HttpPost("bank-accounts")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBank(Guid shopId, [FromBody] BankBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddShopBankAccountCommand(shopId, body.BankCode, body.AccountNo, body.AccountName, body.OtpCode, body.MakeDefault), ct),
            "Đã thêm tài khoản ngân hàng.");
}

[Route("api/admin/finance")]
public sealed class FinanceAdminController : ApiControllerBase
{
    private const long MaxStatementBytes = 5 * 1024 * 1024;

    [HttpGet("fee-rules")]
    [RequirePermission(Permissions.FinanceFeeManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FeeRuleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FeeRules([FromQuery] FeeType? feeType = null, CancellationToken ct = default) =>
        OkData(await Sender.Send(new FeeRulesQuery(feeType), ct));

    public record FeeRuleBody(Guid? CategoryId, FeeType FeeType, int RateBp, DateTimeOffset ValidFrom, string? Note);

    [HttpPost("fee-rules")]
    [RequirePermission(Permissions.FinanceFeeManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateFeeRule([FromBody] FeeRuleBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateFeeRuleCommand(body.CategoryId, body.FeeType, body.RateBp, body.ValidFrom, body.Note), ct), "Đã lưu biểu phí.");

    [HttpGet("withdrawals")]
    [RequirePermission(Permissions.FinanceWithdrawalApprove)]
    [ProducesResponseType<ApiResponse<PagedResult<WithdrawalDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdrawals([FromQuery] WithdrawalStatus? status = WithdrawalStatus.Pending, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new AdminWithdrawalsQuery(status, page, pageSize), ct));

    [HttpPost("withdrawals/{withdrawalId:guid}/approve")]
    [RequirePermission(Permissions.FinanceWithdrawalApprove)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid withdrawalId, CancellationToken ct)
    {
        await Sender.Send(new DecideWithdrawalCommand(withdrawalId, true, null), ct);
        return OkData<object?>(null, "Đã duyệt và chuyển tiền.");
    }

    public record RejectBody(string Reason);

    [HttpPost("withdrawals/{withdrawalId:guid}/reject")]
    [RequirePermission(Permissions.FinanceWithdrawalApprove)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid withdrawalId, [FromBody] RejectBody body, CancellationToken ct)
    {
        await Sender.Send(new DecideWithdrawalCommand(withdrawalId, false, body.Reason), ct);
        return OkData<object?>(null, "Đã từ chối, tiền đã hoàn lại số dư.");
    }

    /// <summary>Platform accounts, totals of shop / buyer balances, and the ledger check (cached = recomputed, Σ debits = Σ credits).</summary>
    [HttpGet("ledger")]
    [RequirePermission(Permissions.FinanceLedgerView)]
    [ProducesResponseType<ApiResponse<LedgerOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ledger(CancellationToken ct) => OkData(await Sender.Send(new LedgerOverviewQuery(), ct));

    [HttpGet("ledger/entries")]
    [RequirePermission(Permissions.FinanceLedgerView)]
    [ProducesResponseType<ApiResponse<PagedResult<LedgerEntryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Entries([FromQuery] LedgerAccountType? accountType = null, [FromQuery] Guid? ownerId = null,
        [FromQuery] string? refType = null, [FromQuery] Guid? refId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new LedgerEntriesQuery(accountType, ownerId, refType, refId, page, pageSize), ct));

    /// <summary>Gateways and carriers a statement can be reconciled against.</summary>
    [HttpGet("reconcile/sources")]
    [RequirePermission(Permissions.FinanceReconcile)]
    [ProducesResponseType<ApiResponse<ReconcileSourcesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcileSources(CancellationToken ct) => OkData(await Sender.Send(new ReconcileSourcesQuery(), ct));

    /// <summary>
    /// The statement file the simulated providers write (<c>gateway</c>: the simulated gateway's transactions;
    /// <c>carrier</c>: COD parcels of the simulated carrier <paramref name="carrier"/>). Real gateways / carriers send theirs.
    /// </summary>
    [HttpGet("statements/{provider}")]
    [RequirePermission(Permissions.FinanceReconcile)]
    public async Task<IActionResult> Statement(string provider, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] string? carrier, CancellationToken ct)
    {
        var file = await Sender.Send(new ProviderStatementQuery(provider, from, to, carrier), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Upload one provider's statement (CSV) → every line matched with ShopHub's records of that provider, differences
    /// listed. <c>source</c>: the gateway (<c>Simulated</c> | <c>VnPay</c> | <c>MoMo</c> | <c>ZaloPay</c>) or the carrier code.
    /// </summary>
    [HttpPost("reconcile/{provider}")]
    [RequirePermission(Permissions.FinanceReconcile)]
    [RequestSizeLimit(MaxStatementBytes + 64 * 1024)]
    [ProducesResponseType<ApiResponse<ReconcileResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile(string provider, [FromForm] DateTimeOffset from, [FromForm] DateTimeOffset to, [FromForm] string? source,
        IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new BusinessRuleException("Vui lòng chọn tệp sao kê (CSV).");
        if (file.Length > MaxStatementBytes) throw new BusinessRuleException("Tệp sao kê tối đa 5 MB.");
        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);
        IRequest<ReconcileResult> command = provider switch
        {
            "gateway" => new ReconcileGatewayCommand(from, to, csv,
                Enum.TryParse<ShopHub.Domain.Sales.PaymentMethod>(source, true, out var method) && ShopHub.Domain.Sales.PaymentMethods.IsOnline(method)
                    ? method
                    : throw new BusinessRuleException("Chọn cổng thanh toán của tệp đối soát (VNPay, MoMo, ZaloPay hoặc cổng giả lập).")),
            "carrier" => new ReconcileCarrierCommand(from, to, csv, source ?? ""),
            _ => throw new NotFoundException("Không tìm thấy nhà cung cấp."),
        };
        return OkData(await Sender.Send(command, ct));
    }
}
