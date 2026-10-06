using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Common;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Finance;

// ---------- fee schedule (VI.7) ----------

public record FeeRuleDto(Guid Id, Guid? CategoryId, string? CategoryName, FeeType FeeType, int RateBp, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    string? Note, bool InForce);

public record FeeRulesQuery(FeeType? FeeType = null) : IRequest<IReadOnlyList<FeeRuleDto>>;

public sealed class FeeRulesHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<FeeRulesQuery, IReadOnlyList<FeeRuleDto>>
{
    public async Task<IReadOnlyList<FeeRuleDto>> Handle(FeeRulesQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await (from r in db.FeeRules.AsNoTracking()
                          join c in db.Categories.AsNoTracking().IgnoreQueryFilters() on r.CategoryId equals c.Id into cs
                          from c in cs.DefaultIfEmpty()
                          where request.FeeType == null || r.FeeType == request.FeeType
                          select new { r, Name = c == null ? null : c.Name }).ToListAsync(ct);
        return rows.OrderBy(x => x.r.FeeType).ThenBy(x => x.Name ?? "").ThenByDescending(x => x.r.ValidFrom)
            .Select(x => new FeeRuleDto(x.r.Id, x.r.CategoryId, x.Name, x.r.FeeType, x.r.RateBp, x.r.ValidFrom, x.r.ValidTo, x.r.Note, x.r.AppliesAt(now)))
            .ToList();
    }
}

public record CreateFeeRuleCommand(Guid? CategoryId, FeeType FeeType, int RateBp, DateTimeOffset ValidFrom, string? Note) : IRequest<Guid>;

public sealed class CreateFeeRuleValidator : AbstractValidator<CreateFeeRuleCommand>
{
    public CreateFeeRuleValidator()
    {
        RuleFor(x => x.RateBp).InclusiveBetween(0, 5_000).WithMessage("Tỉ lệ phí phải từ 0% đến 50%.");
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("Ghi chú tối đa 200 ký tự.");
        RuleFor(x => x.CategoryId).Null().When(x => x.FeeType == FeeType.Payment).WithMessage("Phí thanh toán áp dụng cho mọi ngành hàng.");
    }
}

/// <summary>A new rate starts at a date (today or later): the rule in force for the same scope ends there; orders keep the rate of their date.</summary>
public sealed class CreateFeeRuleHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<CreateFeeRuleCommand, Guid>
{
    public async Task<Guid> Handle(CreateFeeRuleCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (request.ValidFrom < now.AddMinutes(-5)) throw new BusinessRuleException("Biểu phí mới chỉ áp dụng từ hôm nay trở đi, không sửa phí của đơn đã bán.");
        if (request.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new NotFoundException("Không tìm thấy ngành hàng.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var rule = await FeeSchedule.StartAsync(db, request.CategoryId, request.FeeType, request.RateBp, request.ValidFrom, request.Note, now, ct);
        // The category screen shows the fixed fee in force
        if (request.FeeType == FeeType.Fixed && request.CategoryId is { } id && request.ValidFrom <= now)
            (await db.Categories.SingleAsync(c => c.Id == id, ct)).SetCommission(request.RateBp);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rule.Id;
    }
}

// ---------- withdrawals ----------

public record AdminWithdrawalsQuery(WithdrawalStatus? Status = WithdrawalStatus.Pending, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<WithdrawalDto>>, IPagedRequest;

public sealed class AdminWithdrawalsHandler(IApplicationDbContext db) : IRequestHandler<AdminWithdrawalsQuery, PagedResult<WithdrawalDto>>
{
    public async Task<PagedResult<WithdrawalDto>> Handle(AdminWithdrawalsQuery request, CancellationToken ct)
    {
        var query = db.Withdrawals.AsNoTracking().Where(w => request.Status == null || w.Status == request.Status)
            .OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var shopIds = rows.Where(w => w.OwnerType == LedgerOwnerType.Shop).Select(w => w.OwnerId).ToList();
        var userIds = rows.Where(w => w.OwnerType == LedgerOwnerType.Buyer).Select(w => w.OwnerId).ToList();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return new PagedResult<WithdrawalDto>(rows.Select(w => WithdrawalService.ToDto(w,
                w.OwnerType == LedgerOwnerType.Shop ? shops.GetValueOrDefault(w.OwnerId) : users.GetValueOrDefault(w.OwnerId))).ToList(),
            await query.CountAsync(ct), request.Page, request.PageSize);
    }
}

public record DecideWithdrawalCommand(Guid WithdrawalId, bool Approve, string? Reason) : IRequest<Unit>;

public sealed class DecideWithdrawalValidator : AbstractValidator<DecideWithdrawalCommand>
{
    public DecideWithdrawalValidator() =>
        RuleFor(x => x.Reason).NotEmpty().When(x => !x.Approve).WithMessage("Vui lòng nhập lý do từ chối.");
}

public sealed class DecideWithdrawalHandler(WithdrawalService withdrawals, ICurrentUser currentUser) : IRequestHandler<DecideWithdrawalCommand, Unit>
{
    public async Task<Unit> Handle(DecideWithdrawalCommand request, CancellationToken ct)
    {
        var adminId = currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
        if (request.Approve) await withdrawals.ApproveAsync(request.WithdrawalId, adminId, null, ct);
        else await withdrawals.RejectAsync(request.WithdrawalId, adminId, request.Reason!, ct);
        return Unit.Value;
    }
}

// ---------- ledger ----------

public record LedgerAccountDto(Guid Id, LedgerOwnerType OwnerType, Guid OwnerId, string? OwnerName, LedgerAccountType Type, string Label, long Balance);

public record LedgerOverviewDto(IReadOnlyList<LedgerAccountDto> Platform, IReadOnlyList<LedgerAccountDto> Totals, LedgerCheckResult Check);

public record LedgerOverviewQuery : IRequest<LedgerOverviewDto>;

public sealed class LedgerOverviewHandler(IApplicationDbContext db, LedgerCheckService check) : IRequestHandler<LedgerOverviewQuery, LedgerOverviewDto>
{
    public async Task<LedgerOverviewDto> Handle(LedgerOverviewQuery request, CancellationToken ct)
    {
        var platform = await db.LedgerAccounts.AsNoTracking().Where(a => a.OwnerType == LedgerOwnerType.Platform).OrderBy(a => a.Type).ToListAsync(ct);
        var totals = await db.LedgerAccounts.AsNoTracking().Where(a => a.OwnerType != LedgerOwnerType.Platform)
            .GroupBy(a => new { a.OwnerType, a.Type }).Select(g => new { g.Key.OwnerType, g.Key.Type, Balance = g.Sum(a => a.Balance) }).ToListAsync(ct);
        return new LedgerOverviewDto(
            platform.Select(a => new LedgerAccountDto(a.Id, a.OwnerType, a.OwnerId, null, a.Type, LedgerAccount.Label(a.Type), a.Balance)).ToList(),
            totals.OrderBy(t => t.Type).Select(t => new LedgerAccountDto(Guid.Empty, t.OwnerType, Guid.Empty, null, t.Type, LedgerAccount.Label(t.Type), t.Balance))
                .ToList(),
            await check.RunAsync(ct));
    }
}

public record LedgerEntryDto(Guid Id, Guid TransactionId, string Kind, LedgerOwnerType OwnerType, Guid OwnerId, LedgerAccountType AccountType,
    LedgerDirection Direction, long Amount, string RefType, Guid RefId, string Description, DateTimeOffset PostedAt);

public record LedgerEntriesQuery(LedgerAccountType? AccountType, Guid? OwnerId, string? RefType, Guid? RefId, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<LedgerEntryDto>>, IPagedRequest;

public sealed class LedgerEntriesHandler(IApplicationDbContext db) : IRequestHandler<LedgerEntriesQuery, PagedResult<LedgerEntryDto>>
{
    public async Task<PagedResult<LedgerEntryDto>> Handle(LedgerEntriesQuery request, CancellationToken ct)
    {
        var query = from e in db.LedgerEntries.AsNoTracking()
                    join a in db.LedgerAccounts.AsNoTracking() on e.AccountId equals a.Id
                    join t in db.LedgerTransactions.AsNoTracking() on e.TransactionId equals t.Id
                    where (request.AccountType == null || a.Type == request.AccountType) && (request.OwnerId == null || a.OwnerId == request.OwnerId)
                          && (request.RefType == null || e.RefType == request.RefType) && (request.RefId == null || e.RefId == request.RefId)
                    orderby e.PostedAt descending, e.TransactionId, e.Direction
                    select new LedgerEntryDto(e.Id, e.TransactionId, t.Kind, a.OwnerType, a.OwnerId, a.Type, e.Direction, e.Amount, e.RefType, e.RefId,
                        e.Description, e.PostedAt);
        return new PagedResult<LedgerEntryDto>(await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct),
            await query.CountAsync(ct), request.Page, request.PageSize);
    }
}

// ---------- reconciliation with the gateway and the carrier (VI.7) ----------

/// <summary>Statements the providers send (the simulated ones generate theirs from their own records).</summary>
public interface IProviderStatements
{
    /// <summary>CSV <c>txn_id,payment_id,amount,refunded_amount,paid_at</c> of successful payments in [from, to).</summary>
    Task<string> GatewayStatementCsvAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>CSV <c>tracking_no,cod_amount,shipping_fee,delivered_at</c> of COD parcels delivered in [from, to).</summary>
    Task<string> CarrierCodStatementCsvAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public enum ReconcileIssue
{
    MissingInSystem,     // the provider has it, ShopHub does not
    MissingInStatement,  // ShopHub has it, the provider's file does not
    AmountMismatch,      // both have it with different amounts
    RefundMismatch,      // the refunded amounts differ
    FeeMismatch,         // the shipping fee differs
    BadLine,             // the line cannot be read
}

public record ReconcileLine(string Reference, ReconcileIssue Issue, long? ProviderAmount, long? SystemAmount, string Note);

public record ReconcileResult(int StatementLines, int Matched, IReadOnlyList<ReconcileLine> Issues, long StatementTotal, long SystemTotal);

public record ReconcileGatewayCommand(DateTimeOffset From, DateTimeOffset To, string Csv) : IRequest<ReconcileResult>;

/// <summary>Each successful gateway transaction against the payments and refunds ShopHub recorded, to the đồng.</summary>
public sealed class ReconcileGatewayHandler(IApplicationDbContext db) : IRequestHandler<ReconcileGatewayCommand, ReconcileResult>
{
    public async Task<ReconcileResult> Handle(ReconcileGatewayCommand request, CancellationToken ct)
    {
        var lines = Csv.Parse(request.Csv);
        var (from, to) = (request.From.ToUniversalTime(), request.To.ToUniversalTime());
        var system = await db.Payments.AsNoTracking()
            .Where(p => p.Method == PaymentMethod.Simulated && p.ProviderTxnId != null && p.PaidAt >= from && p.PaidAt < to
                        && (p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.Refunded))
            .ToListAsync(ct);
        var paymentIds = system.Select(p => p.Id).ToList();
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.PaymentId != null && paymentIds.Contains(r.PaymentId!.Value) && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.PaymentId!.Value).Select(g => new { g.Key, Total = g.Sum(r => r.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        // A payment refunded in full right after arriving late is a refund too (payment marked Refunded, no refund row)
        long RefundedOf(Payment p) => refunds.GetValueOrDefault(p.Id) is var r && r > 0 ? r : p.Status == PaymentStatus.Refunded ? p.Amount : 0;

        var byTxn = system.ToDictionary(p => p.ProviderTxnId!);
        var issues = new List<ReconcileLine>();
        var seen = new HashSet<string>();
        int matched = 0;
        long statementTotal = 0;
        foreach (var (row, number) in lines.Skip(1).Select((r, i) => (r, i + 2)))
        {
            if (row.Length < 4 || !long.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
                               || !long.TryParse(row[3], NumberStyles.None, CultureInfo.InvariantCulture, out var refunded))
            {
                issues.Add(new ReconcileLine($"dòng {number}", ReconcileIssue.BadLine, null, null, "Không đọc được dòng này."));
                continue;
            }
            var txn = row[0];
            statementTotal += amount;
            seen.Add(txn);
            if (!byTxn.TryGetValue(txn, out var p))
            {
                issues.Add(new ReconcileLine(txn, ReconcileIssue.MissingInSystem, amount, null, "Cổng có giao dịch nhưng ShopHub không ghi nhận."));
                continue;
            }
            if (p.Amount != amount)
                issues.Add(new ReconcileLine(txn, ReconcileIssue.AmountMismatch, amount, p.Amount, "Số tiền giao dịch lệch."));
            else if (RefundedOf(p) != refunded)
                issues.Add(new ReconcileLine(txn, ReconcileIssue.RefundMismatch, refunded, RefundedOf(p), "Số tiền đã hoàn lệch."));
            else matched++;
        }
        foreach (var p in system.Where(p => !seen.Contains(p.ProviderTxnId!)))
            issues.Add(new ReconcileLine(p.ProviderTxnId!, ReconcileIssue.MissingInStatement, null, p.Amount, "ShopHub ghi nhận nhưng tệp của cổng không có."));
        return new ReconcileResult(Math.Max(0, lines.Count - 1), matched, issues, statementTotal, system.Sum(p => p.Amount));
    }
}

public record ReconcileCarrierCommand(DateTimeOffset From, DateTimeOffset To, string Csv) : IRequest<ReconcileResult>;

/// <summary>COD collected by the carrier against the delivered COD orders (amount to collect and shipping fee).</summary>
public sealed class ReconcileCarrierHandler(IApplicationDbContext db) : IRequestHandler<ReconcileCarrierCommand, ReconcileResult>
{
    public async Task<ReconcileResult> Handle(ReconcileCarrierCommand request, CancellationToken ct)
    {
        var lines = Csv.Parse(request.Csv);
        var (fromAt, toAt) = (request.From.ToUniversalTime(), request.To.ToUniversalTime());
        var system = await (from s in db.Shipments.AsNoTracking()
                            join o in db.Orders.AsNoTracking() on s.OrderId equals o.Id
                            where s.Direction == ShipmentDirection.Outbound && o.PaymentMethod == PaymentMethod.Cod && o.DeliveredAt >= fromAt
                                  && o.DeliveredAt < toAt
                            select new { s.TrackingNo, o.Code, Cod = o.GrandTotal, Fee = o.ShippingFee }).ToListAsync(ct);
        var byTracking = system.ToDictionary(s => s.TrackingNo);
        var issues = new List<ReconcileLine>();
        var seen = new HashSet<string>();
        int matched = 0;
        long statementTotal = 0;
        foreach (var (row, number) in lines.Skip(1).Select((r, i) => (r, i + 2)))
        {
            if (row.Length < 3 || !long.TryParse(row[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cod)
                               || !long.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out var fee))
            {
                issues.Add(new ReconcileLine($"dòng {number}", ReconcileIssue.BadLine, null, null, "Không đọc được dòng này."));
                continue;
            }
            var tracking = row[0];
            statementTotal += cod;
            seen.Add(tracking);
            if (!byTracking.TryGetValue(tracking, out var s))
            {
                issues.Add(new ReconcileLine(tracking, ReconcileIssue.MissingInSystem, cod, null, "Hãng thu hộ nhưng không có đơn COD đã giao trong kỳ."));
                continue;
            }
            if (s.Cod != cod) issues.Add(new ReconcileLine(tracking, ReconcileIssue.AmountMismatch, cod, s.Cod, $"Tiền thu hộ đơn {s.Code} lệch."));
            else if (s.Fee != fee) issues.Add(new ReconcileLine(tracking, ReconcileIssue.FeeMismatch, fee, s.Fee, $"Phí vận chuyển đơn {s.Code} lệch."));
            else matched++;
        }
        foreach (var s in system.Where(s => !seen.Contains(s.TrackingNo)))
            issues.Add(new ReconcileLine(s.TrackingNo, ReconcileIssue.MissingInStatement, null, s.Cod, $"Đơn {s.Code} đã giao nhưng tệp của hãng không có."));
        return new ReconcileResult(Math.Max(0, lines.Count - 1), matched, issues, statementTotal, system.Sum(s => s.Cod));
    }
}

public record ProviderStatementQuery(string Provider, DateTimeOffset From, DateTimeOffset To) : IRequest<FileResultDto>;

public sealed class ProviderStatementHandler(IProviderStatements statements) : IRequestHandler<ProviderStatementQuery, FileResultDto>
{
    public async Task<FileResultDto> Handle(ProviderStatementQuery request, CancellationToken ct)
    {
        var csv = request.Provider switch
        {
            "gateway" => await statements.GatewayStatementCsvAsync(request.From.ToUniversalTime(), request.To.ToUniversalTime(), ct),
            "carrier" => await statements.CarrierCodStatementCsvAsync(request.From.ToUniversalTime(), request.To.ToUniversalTime(), ct),
            _ => throw new NotFoundException("Không tìm thấy nhà cung cấp."),
        };
        return new FileResultDto($"sao-ke-{request.Provider}-{SettlementReports.Stamp(request.From, request.To)}.csv", "text/csv",
            System.Text.Encoding.UTF8.GetBytes(csv));
    }
}

internal static class Csv
{
    /// <summary>Minimal CSV: comma separated, optional double quotes, no embedded newlines (provider statement files).</summary>
    public static List<string[]> Parse(string text) =>
        text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(',').Select(c => c.Trim().Trim('"')).ToArray()).ToList();
}
