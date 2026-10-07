using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Finance;

// ---------- overview ----------

public record ShopFinanceSummaryDto(long Pending, long Available, long Withdrawing, long ReleasedTotal, long WithdrawMin, long WithdrawPerWeek,
    long AutoApproveMax, IReadOnlyList<BankAccountDto> BankAccounts);

public record ShopFinanceSummaryQuery(Guid ShopId) : IRequest<ShopFinanceSummaryDto>;

public sealed class ShopFinanceSummaryHandler(IApplicationDbContext db, SellerAccess access, Ledger ledger, ISystemParameters parameters)
    : IRequestHandler<ShopFinanceSummaryQuery, ShopFinanceSummaryDto>
{
    public async Task<ShopFinanceSummaryDto> Handle(ShopFinanceSummaryQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var withdrawing = await db.Withdrawals.Where(w => w.OwnerType == LedgerOwnerType.Shop && w.OwnerId == request.ShopId
                                                          && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing))
            .SumAsync(w => (long?)w.Amount, ct) ?? 0;
        var released = await db.SettlementItems.Where(i => i.ShopId == request.ShopId).SumAsync(i => (long?)i.Net, ct) ?? 0;
        var banks = await db.ShopBankAccounts.AsNoTracking().Where(b => b.ShopId == request.ShopId).OrderByDescending(b => b.IsDefault).ThenBy(b => b.CreatedAt)
            .Select(b => new BankAccountDto(b.Id, b.BankCode, b.AccountNoLast4, b.AccountName, b.VerifiedAt != null, b.IsDefault)).ToListAsync(ct);
        return new ShopFinanceSummaryDto(
            await ledger.BalanceAsync(AccountKey.Shop(request.ShopId, LedgerAccountType.ShopPending), ct),
            await ledger.BalanceAsync(AccountKey.Shop(request.ShopId, LedgerAccountType.ShopAvailable), ct),
            withdrawing, released,
            await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawMin, ct),
            await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawPerWeek, ct),
            await parameters.GetIntAsync(ParameterKeys.FinanceWithdrawAutoApproveMax, ct),
            banks);
    }
}

// ---------- "chờ giải ngân": completed orders not released yet, with their live breakdown ----------

public record EarningDto(Guid OrderId, string OrderCode, DateTimeOffset? CompletedAt, DateTimeOffset? ReleaseAfter, bool HasOpenReturn, long Goods,
    long ShopDiscount, long RefundsBorne, long FixedFee, long PaymentFee, long ServiceFee, long Net, DateTimeOffset? ReleasedAt);

public record ShopPendingEarningsQuery(Guid ShopId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<EarningDto>>, IPagedRequest;

public sealed class ShopPendingEarningsHandler(IApplicationDbContext db, SellerAccess access, OrderLedger orders, ISystemParameters parameters)
    : IRequestHandler<ShopPendingEarningsQuery, PagedResult<EarningDto>>
{
    public async Task<PagedResult<EarningDto>> Handle(ShopPendingEarningsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var window = await parameters.GetIntAsync(ParameterKeys.ReturnWindowDays, ct);
        var hold = await parameters.GetIntAsync(ParameterKeys.FinanceReleaseHoldDays, ct);
        var query = db.Orders.AsNoTracking()
            .Where(o => o.ShopId == request.ShopId && o.Status == OrderStatus.Completed && !db.SettlementItems.Any(s => s.OrderId == o.Id))
            .OrderByDescending(o => o.CompletedAt).ThenBy(o => o.Id);
        var page = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Include(o => o.Items).ThenInclude(i => i.Discounts).ToListAsync(ct);
        var ids = page.Select(o => o.Id).ToList();
        var open = await db.ReturnRequests.Where(r => ids.Contains(r.OrderId) && r.Status != ReturnStatus.Refunded && r.Status != ReturnStatus.Closed
                                                      && r.Status != ReturnStatus.Cancelled).Select(r => r.OrderId).Distinct().ToListAsync(ct);
        var items = new List<EarningDto>();
        foreach (var o in page)
        {
            var b = await orders.BreakdownAsync(o, ct);
            var after = new[] { o.DeliveredAt?.AddDays(window), o.CompletedAt?.AddDays(hold) }.Max();
            items.Add(new EarningDto(o.Id, o.Code, o.CompletedAt, after, open.Contains(o.Id), b.Goods, b.ShopDiscount, b.RefundsBorne, b.FixedFee,
                b.PaymentFee, b.ServiceFee, b.Net, null));
        }
        return new PagedResult<EarningDto>(items, await query.CountAsync(ct), request.Page, request.PageSize);
    }
}

// ---------- "đã giải ngân" ----------

public record ShopReleasedEarningsQuery(Guid ShopId, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<EarningDto>>, IPagedRequest;

public sealed class ShopReleasedEarningsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopReleasedEarningsQuery, PagedResult<EarningDto>>
{
    public async Task<PagedResult<EarningDto>> Handle(ShopReleasedEarningsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var fromAt = request.From?.ToUniversalTime();
        var toAt = request.To?.ToUniversalTime();
        var query = from i in db.SettlementItems.AsNoTracking()
                    join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                    where i.ShopId == request.ShopId && (fromAt == null || i.ReleasedAt >= fromAt) && (toAt == null || i.ReleasedAt < toAt)
                    orderby i.ReleasedAt descending, o.Code
                    select new EarningDto(o.Id, o.Code, o.CompletedAt, null, false, i.Goods, i.ShopDiscount, i.RefundsBorne, i.FixedFee, i.PaymentFee,
                        i.ServiceFee, i.Net, i.ReleasedAt);
        return new PagedResult<EarningDto>(await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct),
            await query.CountAsync(ct), request.Page, request.PageSize);
    }
}

// ---------- ledger lines of the shop's balances ----------

public record ShopTransactionsQuery(Guid ShopId, LedgerAccountType Account = LedgerAccountType.ShopAvailable, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<WalletEntryDto>>, IPagedRequest;

public sealed class ShopTransactionsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopTransactionsQuery, PagedResult<WalletEntryDto>>
{
    public async Task<PagedResult<WalletEntryDto>> Handle(ShopTransactionsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        if (request.Account is not (LedgerAccountType.ShopAvailable or LedgerAccountType.ShopPending))
            throw new NotFoundException("Không tìm thấy tài khoản.");
        return await FinanceViews.EntriesAsync(db, AccountKey.Shop(request.ShopId, request.Account), request.Page, request.PageSize, ct);
    }
}

// ---------- reports: đối soát (Excel / PDF) and hoá đơn phí sàn ----------

public enum ReportFormat
{
    Xlsx,
    Pdf,
}

public record FileResultDto(string FileName, string ContentType, byte[] Content);

internal static class SettlementReports
{
    public static async Task<SettlementReport> BuildAsync(IApplicationDbContext db, ISystemParameters parameters, Guid shopId, DateTimeOffset start,
        DateTimeOffset end, DateTimeOffset now, CancellationToken ct)
    {
        start = start.ToUniversalTime();
        end = end.ToUniversalTime();
        if (end <= start) throw new BusinessRuleException("Ngày kết thúc phải sau ngày bắt đầu.");
        if (end - start > TimeSpan.FromDays(366)) throw new BusinessRuleException("Mỗi báo cáo tối đa một năm.");
        var shop = await db.Shops.AsNoTracking().Where(s => s.Id == shopId).Select(s => s.Name).SingleAsync(ct);
        var rows = await (from i in db.SettlementItems.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on i.OrderId equals o.Id
                          where i.ShopId == shopId && i.ReleasedAt >= start && i.ReleasedAt < end
                          orderby i.ReleasedAt, o.Code
                          select new SettlementReportRow(o.Code, o.CompletedAt ?? i.ReleasedAt, i.ReleasedAt, i.Goods, i.ShopDiscount, i.RefundsBorne,
                              i.FixedFee, i.PaymentFee, i.ServiceFee, i.Net)).ToListAsync(ct);
        return new SettlementReport(shop, await parameters.GetStringAsync(ParameterKeys.SitePlatformName, ct), start, end, rows, now);
    }

    public static string Stamp(DateTimeOffset from, DateTimeOffset to) =>
        $"{VietnamTime.ToLocal(from):yyyyMMdd}-{VietnamTime.ToLocal(to.AddTicks(-1)):yyyyMMdd}";
}

public record ShopSettlementReportQuery(Guid ShopId, DateTimeOffset From, DateTimeOffset To, ReportFormat Format) : IRequest<FileResultDto>;

public sealed class ShopSettlementReportHandler(IApplicationDbContext db, SellerAccess access, IFinanceDocuments documents, ISystemParameters parameters,
    IClock clock) : IRequestHandler<ShopSettlementReportQuery, FileResultDto>
{
    public async Task<FileResultDto> Handle(ShopSettlementReportQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var report = await SettlementReports.BuildAsync(db, parameters, request.ShopId, request.From, request.To, clock.UtcNow, ct);
        var name = $"doi-soat-{SettlementReports.Stamp(request.From, request.To)}";
        return request.Format == ReportFormat.Pdf
            ? new FileResultDto($"{name}.pdf", "application/pdf", documents.SettlementReportPdf(report))
            : new FileResultDto($"{name}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", documents.SettlementReportExcel(report));
    }
}

public record ShopFeeInvoiceQuery(Guid ShopId, DateTimeOffset From, DateTimeOffset To) : IRequest<FileResultDto>;

public sealed class ShopFeeInvoiceHandler(IApplicationDbContext db, SellerAccess access, IFinanceDocuments documents, ISystemParameters parameters,
    IClock clock) : IRequestHandler<ShopFeeInvoiceQuery, FileResultDto>
{
    public async Task<FileResultDto> Handle(ShopFeeInvoiceQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var report = await SettlementReports.BuildAsync(db, parameters, request.ShopId, request.From, request.To, clock.UtcNow, ct);
        var taxCode = await db.ShopKycs.AsNoTracking().Where(k => k.ShopId == request.ShopId).Select(k => k.TaxCode).FirstOrDefaultAsync(ct);
        var stamp = SettlementReports.Stamp(request.From, request.To);
        var invoice = new FeeInvoice($"HDP-{stamp}-{request.ShopId.ToString("N")[..6].ToUpperInvariant()}",
            await parameters.GetStringAsync(ParameterKeys.SiteLegalName, ct), await parameters.GetStringAsync(ParameterKeys.SiteLegalAddress, ct),
            await parameters.GetStringAsync(ParameterKeys.SiteTaxCode, ct), report.ShopName, taxCode, request.From, request.To,
            report.FixedFee, report.PaymentFee, report.ServiceFee, report.Rows.Count, clock.UtcNow);
        return new FileResultDto($"hoa-don-phi-{stamp}.pdf", "application/pdf", documents.FeeInvoicePdf(invoice));
    }
}

// ---------- withdrawals ----------

public record ShopWithdrawalsQuery(Guid ShopId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<WithdrawalDto>>, IPagedRequest;

public sealed class ShopWithdrawalsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopWithdrawalsQuery, PagedResult<WithdrawalDto>>
{
    public async Task<PagedResult<WithdrawalDto>> Handle(ShopWithdrawalsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceView, ct);
        var query = db.Withdrawals.AsNoTracking().Where(w => w.OwnerType == LedgerOwnerType.Shop && w.OwnerId == request.ShopId)
            .OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PagedResult<WithdrawalDto>(rows.Select(w => WithdrawalService.ToDto(w, null)).ToList(), await query.CountAsync(ct), request.Page,
            request.PageSize);
    }
}

public record ShopWithdrawCommand(Guid ShopId, Guid BankAccountId, long Amount) : IRequest<WithdrawalDto>;

public sealed class ShopWithdrawHandler(IApplicationDbContext db, SellerAccess access, WithdrawalService withdrawals)
    : IRequestHandler<ShopWithdrawCommand, WithdrawalDto>
{
    public async Task<WithdrawalDto> Handle(ShopWithdrawCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.FinanceWithdraw, ct);
        var bank = await db.ShopBankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.ShopId == request.ShopId, ct)
                   ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");
        if (bank.VerifiedAt is null) throw new ConflictException("Tài khoản ngân hàng chưa được xác minh.", "BANK_NOT_VERIFIED");
        var w = await withdrawals.RequestAsync(LedgerOwnerType.Shop, request.ShopId,
            new WithdrawalService.Destination(bank.Id, bank.BankCode, bank.AccountNoEncrypted, bank.AccountNoLast4, bank.AccountName), request.Amount,
            staff.UserId, ct);
        return WithdrawalService.ToDto(w, null);
    }
}

// ---------- bank accounts (adding one needs an OTP to the caller's phone) ----------

public record AddShopBankAccountCommand(Guid ShopId, string BankCode, string AccountNo, string AccountName, string OtpCode, bool MakeDefault)
    : IRequest<Guid>;

public sealed class AddShopBankAccountValidator : AbstractValidator<AddShopBankAccountCommand>
{
    public AddShopBankAccountValidator()
    {
        RuleFor(x => x.BankCode).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng chọn ngân hàng.")
            .Must(BankCatalogue.IsKnown).WithMessage(BankCatalogue.UnknownMessage);
        RuleFor(x => x.AccountNo).NotNull().WithMessage("Vui lòng nhập số tài khoản.").Matches(@"^\d{6,20}$").WithMessage("Số tài khoản gồm 6–20 chữ số.");
        RuleFor(x => x.AccountName).NotEmpty().WithMessage("Vui lòng nhập tên chủ tài khoản.").MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
        RuleFor(x => x.OtpCode).NotNull().WithMessage("Vui lòng nhập mã xác thực.").Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
    }
}

public sealed class AddShopBankAccountHandler(IApplicationDbContext db, SellerAccess access, OtpService otp, IDataEncryptor encryptor, IClock clock)
    : IRequestHandler<AddShopBankAccountCommand, Guid>
{
    public async Task<Guid> Handle(AddShopBankAccountCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.FinanceWithdraw, ct);
        if (await db.ShopBankAccounts.CountAsync(b => b.ShopId == request.ShopId, ct) >= 5)
            throw new ConflictException("Mỗi shop lưu tối đa 5 tài khoản ngân hàng.", "BANK_LIMIT");
        await FinanceOtp.VerifyAsync(db, otp, staff.UserId, request.OtpCode, ct);
        var no = request.AccountNo.Trim();
        var account = new ShopBankAccount(request.ShopId, request.BankCode, encryptor.Encrypt(no), no[^4..], request.AccountName, request.MakeDefault);
        account.MarkVerified(clock.UtcNow);
        if (request.MakeDefault)
            foreach (var other in await db.ShopBankAccounts.Where(b => b.ShopId == request.ShopId && b.IsDefault).ToListAsync(ct)) other.ClearDefault();
        db.ShopBankAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }
}

public record SetDefaultShopBankAccountCommand(Guid ShopId, Guid BankAccountId, string OtpCode) : IRequest<Unit>;

public sealed class SetDefaultShopBankAccountValidator : AbstractValidator<SetDefaultShopBankAccountCommand>
{
    public SetDefaultShopBankAccountValidator() =>
        RuleFor(x => x.OtpCode).NotNull().WithMessage("Vui lòng nhập mã xác thực.").Matches(@"^\d{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
}

/// <summary>
/// "Đặt làm mặc định" (D6, L099): payouts go to the default account, so changing it needs the finance OTP like adding one.
/// The old default is cleared and saved first — the partial unique index allows one default per shop at any moment.
/// </summary>
public sealed class SetDefaultShopBankAccountHandler(IApplicationDbContext db, SellerAccess access, OtpService otp)
    : IRequestHandler<SetDefaultShopBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultShopBankAccountCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.FinanceWithdraw, ct);
        var account = await db.ShopBankAccounts.FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.ShopId == request.ShopId, ct)
                      ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");
        await FinanceOtp.VerifyAsync(db, otp, staff.UserId, request.OtpCode, ct);
        if (account.IsDefault) return Unit.Value;
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shop:banks:{request.ShopId}", ct);
        foreach (var other in await db.ShopBankAccounts.Where(b => b.ShopId == request.ShopId && b.IsDefault).ToListAsync(ct)) other.ClearDefault();
        await db.SaveChangesAsync(ct);
        account.MakeDefault();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record RemoveShopBankAccountCommand(Guid ShopId, Guid BankAccountId) : IRequest<Unit>;

/// <summary>
/// Xoá a bank account (D6, L099): never the default (money would silently go elsewhere — make another one default first)
/// and never one a withdrawal is still on its way to. Soft delete: past withdrawals keep their bank snapshot.
/// </summary>
public sealed class RemoveShopBankAccountHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<RemoveShopBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(RemoveShopBankAccountCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceWithdraw, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shop:banks:{request.ShopId}", ct);
        var account = await db.ShopBankAccounts.FirstOrDefaultAsync(b => b.Id == request.BankAccountId && b.ShopId == request.ShopId, ct)
                      ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");
        if (account.IsDefault)
            throw new ConflictException("Không xoá được tài khoản mặc định. Hãy đặt tài khoản khác làm mặc định trước.", "BANK_IS_DEFAULT");
        if (await db.Withdrawals.AnyAsync(w => w.BankAccountId == account.Id && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing), ct))
            throw new ConflictException("Tài khoản đang có lệnh rút tiền chưa hoàn tất, chưa xoá được.", "BANK_IN_USE");
        db.ShopBankAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

/// <summary>
/// OTP before a shop adds a bank account or withdraws (III.7): only staff holding FINANCE.WITHDRAW may ask for it —
/// checked explicitly, not through the side effect of a read that needs FINANCE.VIEW (L080).
/// </summary>
public record SendShopFinanceOtpCommand(Guid ShopId) : IRequest<OtpIssued>;

public sealed class SendShopFinanceOtpHandler(SellerAccess access, ISender sender) : IRequestHandler<SendShopFinanceOtpCommand, OtpIssued>
{
    public async Task<OtpIssued> Handle(SendShopFinanceOtpCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.FinanceWithdraw, ct);
        return await sender.Send(new SendFinanceOtpCommand(), ct);
    }
}
