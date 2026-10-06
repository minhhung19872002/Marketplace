using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Reports;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Identity;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Admin;

// =====================================================================================================================
// VI.2 users: detail, reset password
// =====================================================================================================================

public record AdminUserOrderDto(string Code, string ShopName, OrderStatus Status, long GrandTotal, DateTimeOffset CreatedAt);

public record AdminUserDeviceDto(string? Device, string? Ip, DateTimeOffset SignedInAt, DateTimeOffset ExpiresAt, bool Active);

public record AdminUserDetailDto(
    Guid Id, string FullName, string? Phone, string? Email, UserStatus Status, string? LockReason, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles, int OrderCount, long Spent, int ReviewCount, int ReportedReviews, int ReturnCount,
    IReadOnlyList<AdminUserOrderDto> RecentOrders, IReadOnlyList<AdminUserDeviceDto> Devices, IReadOnlyList<string> OwnedShops);

public record AdminUserDetailQuery(Guid UserId) : IRequest<AdminUserDetailDto>;

/// <summary>Everything an operator needs about one account (spec VI.2): orders, reviews, violations, devices. The audit trail is <c>/api/admin/audit-logs?userId=</c>.</summary>
public sealed class AdminUserDetailHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<AdminUserDetailQuery, AdminUserDetailDto>
{
    public async Task<AdminUserDetailDto> Handle(AdminUserDetailQuery request, CancellationToken ct)
    {
        var u = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.UserId, ct)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");
        var now = clock.UtcNow;
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == u.Id select r.Name).ToListAsync(ct);
        var orders = db.Orders.AsNoTracking().Where(o => o.BuyerId == u.Id);
        var recent = await (from o in orders
                            join s in db.Shops.IgnoreQueryFilters() on o.ShopId equals s.Id
                            orderby o.CreatedAt descending, o.Id
                            select new AdminUserOrderDto(o.Code, s.Name, o.Status, o.GrandTotal, o.CreatedAt)).Take(10).ToListAsync(ct);
        var devices = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == u.Id && t.ReplacedById == null)
            .OrderByDescending(t => t.CreatedAt).Take(10)
            .Select(t => new AdminUserDeviceDto(t.Device, t.Ip, t.CreatedAt, t.ExpiresAt, t.RevokedAt == null && t.ExpiresAt > now)).ToListAsync(ct);
        var reviewIds = db.Reviews.IgnoreQueryFilters().Where(r => r.BuyerId == u.Id).Select(r => r.Id);
        return new AdminUserDetailDto(u.Id, u.FullName, u.Phone, u.Email, u.Status, u.LockReason, u.CreatedAt, u.LastLoginAt, roles,
            await orders.CountAsync(ct),
            await orders.Placed().Where(o => o.Status != OrderStatus.Cancelled).SumAsync(o => (long?)o.GrandTotal, ct) ?? 0,
            await reviewIds.CountAsync(ct),
            await db.ReviewReports.CountAsync(r => reviewIds.Contains(r.ReviewId), ct),
            await db.ReturnRequests.CountAsync(r => r.BuyerId == u.Id, ct),
            recent, devices,
            await db.Shops.IgnoreQueryFilters().Where(s => s.OwnerId == u.Id).Select(s => s.Name).ToListAsync(ct));
    }
}

public record ResetPasswordResult(string TemporaryPassword);

public record AdminResetPasswordCommand(Guid UserId) : IRequest<ResetPasswordResult>;

/// <summary>
/// Đặt lại mật khẩu (spec VI.2): a one-time password shown once to the operator (never stored in clear, never logged),
/// the user must change it at next sign-in, and every open session is cut now.
/// </summary>
public sealed class AdminResetPasswordHandler(IApplicationDbContext db, IPasswordHasher hasher, SessionService sessions)
    : IRequestHandler<AdminResetPasswordCommand, ResetPasswordResult>
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public async Task<ResetPasswordResult> Handle(AdminResetPasswordCommand request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct) ?? throw new NotFoundException("Không tìm thấy người dùng.");
        // Letters + digits, 12 characters: meets the password rule and is easy to read out
        var chars = System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(Alphabet, 10);
        var temporary = new string(chars) + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10, 100);
        user.ChangePassword(hasher.Hash(temporary));
        user.RequirePasswordChange();
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, "Quản trị đặt lại mật khẩu", null, ct);
        return new ResetPasswordResult(temporary);
    }
}

// =====================================================================================================================
// VI.3 penalty points
// =====================================================================================================================

public record ShopPenaltyDto(Guid Id, int Points, string Reason, string? OrderCode, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? GivenBy,
    DateTimeOffset? RevokedAt, string? RevokeReason, bool Counts);

public record ShopPenaltiesDto(PenaltyStatus Status, IReadOnlyList<ShopPenaltyDto> Items);

public record ShopPenaltiesQuery(Guid ShopId) : IRequest<ShopPenaltiesDto>;

public sealed class ShopPenaltiesHandler(IApplicationDbContext db, ShopPenaltyService penalties, IClock clock) : IRequestHandler<ShopPenaltiesQuery, ShopPenaltiesDto>
{
    public async Task<ShopPenaltiesDto> Handle(ShopPenaltiesQuery request, CancellationToken ct)
    {
        if (!await db.Shops.IgnoreQueryFilters().AnyAsync(s => s.Id == request.ShopId, ct)) throw new NotFoundException("Không tìm thấy shop.");
        var now = clock.UtcNow;
        var rows = await (from p in db.ShopPenalties.AsNoTracking()
                          where p.ShopId == request.ShopId
                          orderby p.CreatedAt descending, p.Id
                          select new
                          {
                              p,
                              Order = db.Orders.Where(o => o.Id == p.OrderId).Select(o => o.Code).FirstOrDefault(),
                              Admin = db.Users.IgnoreQueryFilters().Where(u => u.Id == p.GivenBy).Select(u => u.FullName).FirstOrDefault(),
                          }).Take(200).ToListAsync(ct);
        return new ShopPenaltiesDto(await penalties.OfShopAsync(request.ShopId, ct),
            rows.Select(r => new ShopPenaltyDto(r.p.Id, r.p.Points, r.p.Reason, r.Order, r.p.CreatedAt, r.p.ExpiresAt, r.Admin ?? (r.p.GivenBy is null ? "Hệ thống" : null),
                r.p.RevokedAt, r.p.RevokeReason, r.p.CountsAt(now))).ToList());
    }
}

public record AddShopPenaltyCommand(Guid ShopId, int Points, string Reason, int? ExpiresInDays) : IRequest<PenaltyStatus>;

public sealed class AddShopPenaltyValidator : AbstractValidator<AddShopPenaltyCommand>
{
    public AddShopPenaltyValidator()
    {
        RuleFor(x => x.Points).InclusiveBetween(1, 20).WithMessage("Mỗi lần ghi từ 1 đến 20 điểm.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng nhập lý do phạt.").MaximumLength(300);
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 730).When(x => x.ExpiresInDays is not null).WithMessage("Hạn điểm phạt từ 1 đến 730 ngày.");
    }
}

public sealed class AddShopPenaltyHandler(IApplicationDbContext db, ShopPenaltyService penalties, ISystemParameters parameters, ICurrentUser currentUser,
    IOutbox outbox, IClock clock) : IRequestHandler<AddShopPenaltyCommand, PenaltyStatus>
{
    public async Task<PenaltyStatus> Handle(AddShopPenaltyCommand request, CancellationToken ct)
    {
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ShopId, ct) ?? throw new NotFoundException("Không tìm thấy shop.");
        var now = clock.UtcNow;
        var days = request.ExpiresInDays ?? (int)await parameters.GetIntAsync(ParameterKeys.ShopPenaltyExpiryDays, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"penalty:{shop.Id}", ct);
        db.ShopPenalties.Add(new ShopPenalty(shop.Id, request.Points, request.Reason, null, now, now.AddDays(days), currentUser.UserId));
        outbox.Enqueue(OutboxTypes.ShopEvent, new ShopEventPayload(shop.Id, "PENALTY", $"{request.Points} điểm — {request.Reason.Trim()}"));
        var status = await penalties.RecomputeAsync(shop.Id, ct);
        await tx.CommitAsync(ct);
        return status;
    }
}

public record RevokeShopPenaltyCommand(Guid PenaltyId, string Reason) : IRequest<PenaltyStatus>;

public sealed class RevokeShopPenaltyHandler(IApplicationDbContext db, ShopPenaltyService penalties, IClock clock) : IRequestHandler<RevokeShopPenaltyCommand, PenaltyStatus>
{
    public async Task<PenaltyStatus> Handle(RevokeShopPenaltyCommand request, CancellationToken ct)
    {
        var p = await db.ShopPenalties.FirstOrDefaultAsync(x => x.Id == request.PenaltyId, ct) ?? throw new NotFoundException("Không tìm thấy điểm phạt.");
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"penalty:{p.ShopId}", ct);
        p.Revoke(request.Reason, clock.UtcNow);
        var status = await penalties.RecomputeAsync(p.ShopId, ct);
        await tx.CommitAsync(ct);
        return status;
    }
}

// =====================================================================================================================
// VI.5 orders: lookup, full history, controlled intervention
// =====================================================================================================================

public record AdminOrderRowDto(Guid Id, string Code, string ShopName, string BuyerName, OrderStatus Status, OrderPaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod, long GrandTotal, DateTimeOffset CreatedAt);

public record AdminOrdersQuery(string? Q, OrderStatus? Status, int Page = 1, int PageSize = 20) : IRequest<PagedResult<AdminOrderRowDto>>, IPagedRequest;

public sealed class AdminOrdersValidator : AbstractValidator<AdminOrdersQuery>
{
    public AdminOrdersValidator() => this.ApplyPagingRules();
}

/// <summary>Tra mọi đơn (spec VI.5): by order code, tracking number, buyer phone / name or shop name.</summary>
public sealed class AdminOrdersHandler(IApplicationDbContext db) : IRequestHandler<AdminOrdersQuery, PagedResult<AdminOrderRowDto>>
{
    public async Task<PagedResult<AdminOrderRowDto>> Handle(AdminOrdersQuery request, CancellationToken ct)
    {
        var q = from o in db.Orders.AsNoTracking()
                join s in db.Shops.IgnoreQueryFilters() on o.ShopId equals s.Id
                join u in db.Users.IgnoreQueryFilters() on o.BuyerId equals u.Id
                select new { o, o.Id, ShopName = s.Name, u.FullName, u.Phone };
        if (request.Status is { } status) q = q.Where(x => x.o.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            var upper = term.ToUpperInvariant();
            var lower = term.ToLowerInvariant();
            q = q.Where(x => x.o.Code == upper || x.Phone == term || x.FullName.ToLower().Contains(lower) || x.ShopName.ToLower().Contains(lower)
                             || db.Shipments.Any(s => s.OrderId == x.o.Id && s.TrackingNo == term));
        }
        return await q.OrderByDescending(x => x.o.CreatedAt).ThenBy(x => x.Id).ToPagedResultAsync(
            x => new AdminOrderRowDto(x.o.Id, x.o.Code, x.ShopName, x.FullName, x.o.Status, x.o.PaymentStatus, x.o.PaymentMethod, x.o.GrandTotal, x.o.CreatedAt),
            request, ct);
    }
}

public record AdminOrderLineDto(string Name, string? Variant, long UnitPrice, int Quantity, long LineTotal);

public record AdminOrderHistoryDto(OrderStatus? From, OrderStatus To, OrderActor Actor, string? ActorName, string? Reason, DateTimeOffset At);

public record AdminPaymentDto(Guid Id, PaymentMethod Method, PaymentStatus Status, long Amount, string? ProviderTxnId, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt,
    string? FailureReason);

public record AdminRefundDto(Guid Id, long Amount, RefundDestination Destination, RefundStatus Status, string Reason, string? ProviderRef, DateTimeOffset CreatedAt);

public record AdminShipmentEventDto(ShipmentStatus Status, string Description, string? Location, DateTimeOffset At);

public record AdminShipmentDto(string TrackingNo, string CarrierCode, ShipmentDirection Direction, ShipmentStatus Status, IReadOnlyList<AdminShipmentEventDto> Events);

public record AdminOrderDetailDto(AdminOrderRowDto Order, long Subtotal, long ShopDiscount, long PlatformDiscount, long ShippingFee, long ShippingDiscount,
    long CoinUsed, string? CancelReason, IReadOnlyList<AdminOrderLineDto> Lines, IReadOnlyList<AdminOrderHistoryDto> History,
    IReadOnlyList<AdminPaymentDto> Payments, IReadOnlyList<AdminRefundDto> Refunds, IReadOnlyList<AdminShipmentDto> Shipments, IReadOnlyList<string> Returns);

public record AdminOrderDetailQuery(string Code) : IRequest<AdminOrderDetailDto>;

public sealed class AdminOrderDetailHandler(IApplicationDbContext db) : IRequestHandler<AdminOrderDetailQuery, AdminOrderDetailDto>
{
    public async Task<AdminOrderDetailDto> Handle(AdminOrderDetailQuery request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var o = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.History).FirstOrDefaultAsync(x => x.Code == code, ct)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var shop = await db.Shops.IgnoreQueryFilters().Where(s => s.Id == o.ShopId).Select(s => s.Name).SingleAsync(ct);
        var buyer = await db.Users.IgnoreQueryFilters().Where(u => u.Id == o.BuyerId).Select(u => u.FullName).SingleAsync(ct);
        var actorIds = o.History.Where(h => h.ActorId != null).Select(h => h.ActorId!.Value).Distinct().ToList();
        var actors = await db.Users.IgnoreQueryFilters().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.CheckoutId == o.CheckoutId).OrderBy(p => p.CreatedAt)
            .Select(p => new AdminPaymentDto(p.Id, p.Method, p.Status, p.Amount, p.ProviderTxnId, p.CreatedAt, p.PaidAt, p.FailureReason)).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == o.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new AdminRefundDto(r.Id, r.Amount, r.Destination, r.Status, r.Reason, r.ProviderRef, r.CreatedAt)).ToListAsync(ct);
        var shipments = await db.Shipments.AsNoTracking().Include(s => s.Events).Where(s => s.OrderId == o.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var returns = await db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == o.Id).OrderBy(r => r.CreatedAt).Select(r => r.Code).ToListAsync(ct);
        return new AdminOrderDetailDto(
            new AdminOrderRowDto(o.Id, o.Code, shop, buyer, o.Status, o.PaymentStatus, o.PaymentMethod, o.GrandTotal, o.CreatedAt),
            o.Subtotal, o.ShopDiscount, o.PlatformDiscount, o.ShippingFee, o.ShippingDiscount, o.CoinUsed, o.CancelReason,
            o.Items.Select(i => new AdminOrderLineDto(i.NameSnapshot, i.VariantSnapshot, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
            o.History.OrderBy(h => h.OccurredAt).Select(h => new AdminOrderHistoryDto(h.FromStatus, h.ToStatus, h.ActorType,
                h.ActorId is { } a ? actors.GetValueOrDefault(a) : null, h.Reason, h.OccurredAt)).ToList(),
            payments, refunds,
            shipments.Select(s => new AdminShipmentDto(s.TrackingNo, s.CarrierCode, s.Direction, s.Status,
                s.Events.OrderBy(e => e.OccurredAt).Select(e => new AdminShipmentEventDto(e.Status, e.Description, e.Location, e.OccurredAt)).ToList())).ToList(),
            returns);
    }
}

public record AdminCancelOrderCommand(string Code, string Reason) : IRequest<Unit>;

public sealed class AdminCancelOrderValidator : AbstractValidator<AdminCancelOrderCommand>
{
    public AdminCancelOrderValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Can thiệp đơn phải ghi lý do.").MinimumLength(10).WithMessage("Lý do cần ít nhất 10 ký tự.").MaximumLength(300);
}

/// <summary>Huỷ đơn bởi sàn (spec VI.5): same canceller as buyers / shops (stock back, refund to the source), actor = Admin, reason required.</summary>
public sealed class AdminCancelOrderHandler(IApplicationDbContext db, OrderLocks locks, OrderCanceller canceller, ICurrentUser currentUser)
    : IRequestHandler<AdminCancelOrderCommand, Unit>
{
    public async Task<Unit> Handle(AdminCancelOrderCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var id = await db.Orders.Where(o => o.Code == code).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(id, ct);
        await canceller.CancelAsync(order, OrderActor.Admin, currentUser.UserId, $"Sàn huỷ: {request.Reason.Trim()}", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record ResolveFailedRefundCommand(Guid RefundId, bool ToWallet, string Reason) : IRequest<RefundStatus>;

public sealed class ResolveFailedRefundValidator : AbstractValidator<ResolveFailedRefundCommand>
{
    public ResolveFailedRefundValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Can thiệp hoàn tiền phải ghi lý do.").MaximumLength(300);
}

/// <summary>
/// Hoàn tiền thủ công (spec VI.5): a gateway refund that failed is retried at the gateway, or sent to the buyer's Ví
/// ShopHub instead. Either way a new refund row is written (the failed one stays as evidence) and the ledger follows
/// through the usual order event.
/// </summary>
public sealed class ResolveFailedRefundHandler(IApplicationDbContext db, OrderLocks locks, IPaymentGatewayRegistry gateways, IOutbox outbox, IClock clock)
    : IRequestHandler<ResolveFailedRefundCommand, RefundStatus>
{
    public async Task<RefundStatus> Handle(ResolveFailedRefundCommand request, CancellationToken ct)
    {
        var failed = await db.Refunds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RefundId, ct) ?? throw new NotFoundException("Không tìm thấy lệnh hoàn tiền.");
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(failed.OrderId, ct);
        await db.LockAsync($"refund:{failed.Id}", ct);
        if (await db.Refunds.AnyAsync(r => r.Id == failed.Id && r.Status != RefundStatus.Failed, ct)
            || await db.Refunds.AnyAsync(r => r.OrderId == failed.OrderId && r.ReturnId == failed.ReturnId && r.Status == RefundStatus.Succeeded && r.CreatedAt > failed.CreatedAt, ct))
            throw new ConflictException("Lệnh hoàn tiền này không còn ở trạng thái lỗi (đã được xử lý).", "REFUND_NOT_FAILED");
        var now = clock.UtcNow;
        var note = $"Xử lý hoàn tiền lỗi: {request.Reason.Trim()}";
        Refund retry;
        if (request.ToWallet)
        {
            // Pending wallet refunds are completed by the finance sync together with the wallet credit
            retry = new Refund(order.Id, failed.PaymentId, failed.Amount, RefundDestination.Wallet, note, now);
        }
        else
        {
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == failed.PaymentId, ct)
                          ?? throw new ConflictException("Không có giao dịch gốc để hoàn qua cổng, hãy hoàn về Ví ShopHub.", "NO_PAYMENT");
            retry = new Refund(order.Id, payment.Id, failed.Amount, RefundDestination.Gateway, note, now);
            retry.Complete(await gateways.For(payment.Method).RefundAsync(payment, failed.Amount, note, ct), payment.ProviderTxnId, now);
        }
        if (failed.ReturnId is { } returnId) retry.LinkReturn(returnId);
        db.Refunds.Add(retry);
        if (retry.Status != RefundStatus.Failed && failed.ReturnId is null && order.PaymentStatus == OrderPaymentStatus.Paid) order.MarkRefunded();
        if (retry.Status != RefundStatus.Failed) outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Refunded, null));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return retry.Status;
    }
}

// =====================================================================================================================
// VI.4 catalog: brands, category tree moves, buyer reports, bulk lock
// =====================================================================================================================

public record AdminBrandDto(Guid Id, string Name, string Slug, string? LogoUrl, bool IsVerified, int ProductCount);

public record AdminBrandsQuery(string? Q, int Page = 1, int PageSize = 50) : IRequest<PagedResult<AdminBrandDto>>, IPagedRequest;

public sealed class AdminBrandsValidator : AbstractValidator<AdminBrandsQuery>
{
    public AdminBrandsValidator() => this.ApplyPagingRules();
}

public sealed class AdminBrandsHandler(IApplicationDbContext db) : IRequestHandler<AdminBrandsQuery, PagedResult<AdminBrandDto>>
{
    public async Task<PagedResult<AdminBrandDto>> Handle(AdminBrandsQuery request, CancellationToken ct)
    {
        var q = db.Brands.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim().ToLowerInvariant();
            q = q.Where(b => b.Name.ToLower().Contains(term));
        }
        return await q.OrderBy(b => b.Name).ThenBy(b => b.Id).ToPagedResultAsync(
            b => new AdminBrandDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.IsVerified, db.Products.Count(p => p.BrandId == b.Id)), request, ct);
    }
}

public record UpdateBrandCommand(Guid Id, string Name, string? LogoUrl, bool IsVerified) : IRequest<Unit>;

public sealed class UpdateBrandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên thương hiệu.").MaximumLength(100);
        RuleFor(x => x.LogoUrl).Must(u => u is null || u.StartsWith("https://", StringComparison.Ordinal) || u.StartsWith('/'))
            .WithMessage("Logo phải là đường dẫn https:// hoặc /...");
    }
}

public sealed class UpdateBrandHandler(IApplicationDbContext db, IOutbox outbox) : IRequestHandler<UpdateBrandCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBrandCommand request, CancellationToken ct)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct) ?? throw new NotFoundException("Không tìm thấy thương hiệu.");
        var slug = Slug.From(request.Name);
        if (await db.Brands.AnyAsync(b => b.Id != brand.Id && b.Slug == slug, ct)) throw new ConflictException("Đã có thương hiệu cùng tên.", "BRAND_EXISTS");
        brand.Update(request.Name.Trim(), slug, request.LogoUrl, request.IsVerified);
        // Search documents carry the brand name
        var products = await db.Products.Where(p => p.BrandId == brand.Id).Select(p => p.Id).Take(5_000).ToListAsync(ct);
        if (products.Count > 0) outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload(products));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record MoveCategoryCommand(Guid CategoryId, Guid? NewParentId, int SortOrder) : IRequest<Unit>;

/// <summary>Kéo thả cây danh mục (spec VI.4): the subtree moves with its levels recomputed; at most 3 levels, never under itself.</summary>
public sealed class MoveCategoryHandler(IApplicationDbContext db, IOutbox outbox) : IRequestHandler<MoveCategoryCommand, Unit>
{
    public async Task<Unit> Handle(MoveCategoryCommand request, CancellationToken ct)
    {
        var all = await db.Categories.ToListAsync(ct);
        var node = all.FirstOrDefault(c => c.Id == request.CategoryId) ?? throw new NotFoundException("Không tìm thấy danh mục.");
        var parent = request.NewParentId is { } pid ? all.FirstOrDefault(c => c.Id == pid) ?? throw new NotFoundException("Không tìm thấy danh mục cha.") : null;
        var subtree = new List<Category> { node };
        for (var i = 0; i < subtree.Count; i++) subtree.AddRange(all.Where(c => c.ParentId == subtree[i].Id));
        if (parent is not null && subtree.Any(c => c.Id == parent.Id)) throw new BusinessRuleException("Không thể chuyển danh mục vào chính nó hoặc danh mục con của nó.");
        // Depths taken before anything moves (moving the node first would change its own level)
        var depth = subtree.ToDictionary(c => c.Id, c => c.Level - node.Level);
        var newLevel = (parent?.Level ?? 0) + 1;
        if (newLevel + depth.Values.Max() > Category.MaxLevel) throw new BusinessRuleException("Danh mục chỉ có tối đa 3 cấp.");
        // Products sit on leaves: a leaf with products cannot become a parent
        if (parent is not null && await db.Products.IgnoreQueryFilters().AnyAsync(p => p.CategoryId == parent.Id, ct))
            throw new BusinessRuleException("Danh mục cha đang có sản phẩm, không thể nhận danh mục con.");
        foreach (var c in subtree) c.MoveTo(c.Id == node.Id ? parent?.Id : c.ParentId, newLevel + depth[c.Id]);
        node.SetSortOrder(request.SortOrder);
        var ids = subtree.Select(c => c.Id).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.CategoryId)).Select(p => p.Id).Take(10_000).ToListAsync(ct);
        if (products.Count > 0) outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload(products));
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record BulkBanProductsCommand(IReadOnlyList<Guid> ProductIds, string Reason) : IRequest<int>;

public sealed class BulkBanProductsValidator : AbstractValidator<BulkBanProductsCommand>
{
    public BulkBanProductsValidator()
    {
        RuleFor(x => x.ProductIds).NotEmpty().WithMessage("Chọn ít nhất một sản phẩm.")
            .Must(ids => ids.Count <= 100).WithMessage("Mỗi lần khoá tối đa 100 sản phẩm.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng ghi lý do khoá.").MaximumLength(500);
    }
}

/// <summary>Khoá sản phẩm hàng loạt (spec VI.4, trap 7: capped at 100 per call). Already banned / deleted ones are skipped.</summary>
public sealed class BulkBanProductsHandler(IApplicationDbContext db, IOutbox outbox) : IRequestHandler<BulkBanProductsCommand, int>
{
    public async Task<int> Handle(BulkBanProductsCommand request, CancellationToken ct)
    {
        var ids = request.ProductIds.Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id) && p.Status != ProductStatus.Banned).ToListAsync(ct);
        foreach (var p in products)
        {
            p.Ban(request.Reason.Trim());
            outbox.Enqueue(OutboxTypes.ProductEvent, new ProductEventPayload(p.Id, "BAN", request.Reason));
        }
        await db.SaveChangesAsync(ct);
        return products.Count;
    }
}

public record ReportProductCommand(Guid ProductId, ProductReportReason Reason, string? Details) : IRequest<Unit>;

public sealed class ReportProductValidator : AbstractValidator<ReportProductCommand>
{
    public ReportProductValidator()
    {
        RuleFor(x => x.Details).MaximumLength(1000).WithMessage("Mô tả tối đa 1000 ký tự.");
        RuleFor(x => x.Details).NotEmpty().When(x => x.Reason == ProductReportReason.Other).WithMessage("Vui lòng mô tả vi phạm.");
    }
}

/// <summary>Báo cáo sản phẩm vi phạm (spec II.4): one open report per buyer and product (unique index).</summary>
public sealed class ReportProductHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<ReportProductCommand, Unit>
{
    public async Task<Unit> Handle(ReportProductCommand request, CancellationToken ct)
    {
        var userId = (currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục."));
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId && p.Status == ProductStatus.Active, ct)) throw new NotFoundException("Không tìm thấy sản phẩm.");
        if (await db.ProductReports.AnyAsync(r => r.ProductId == request.ProductId && r.ReporterId == userId && r.Status == ProductReportStatus.Open, ct))
            throw new ConflictException("Bạn đã báo cáo sản phẩm này, sàn đang xem xét.", "ALREADY_REPORTED");
        db.ProductReports.Add(new ProductReport(request.ProductId, userId, request.Reason, request.Details, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_product_reports_open", StringComparison.Ordinal) == true)
        {
            throw new ConflictException("Bạn đã báo cáo sản phẩm này, sàn đang xem xét.", "ALREADY_REPORTED");
        }
        return Unit.Value;
    }
}

public record ProductReportDto(Guid Id, Guid ProductId, string ProductName, string ShopName, ProductReportReason Reason, string? Details, string ReporterName,
    ProductReportStatus Status, string? Resolution, DateTimeOffset CreatedAt, int OpenReportsOnProduct);

public record ProductReportsQuery(ProductReportStatus? Status, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ProductReportDto>>, IPagedRequest;

public sealed class ProductReportsValidator : AbstractValidator<ProductReportsQuery>
{
    public ProductReportsValidator() => this.ApplyPagingRules();
}

public sealed class ProductReportsHandler(IApplicationDbContext db) : IRequestHandler<ProductReportsQuery, PagedResult<ProductReportDto>>
{
    public async Task<PagedResult<ProductReportDto>> Handle(ProductReportsQuery request, CancellationToken ct)
    {
        var q = from r in db.ProductReports.AsNoTracking()
                join p in db.Products.IgnoreQueryFilters() on r.ProductId equals p.Id
                join s in db.Shops.IgnoreQueryFilters() on p.ShopId equals s.Id
                join u in db.Users.IgnoreQueryFilters() on r.ReporterId equals u.Id
                select new { r, r.Id, ProductName = p.Name, ShopName = s.Name, Reporter = u.FullName };
        if (request.Status is { } status) q = q.Where(x => x.r.Status == status);
        return await q.OrderByDescending(x => x.r.CreatedAt).ThenBy(x => x.Id).ToPagedResultAsync(
            x => new ProductReportDto(x.r.Id, x.r.ProductId, x.ProductName, x.ShopName, x.r.Reason, x.r.Details, x.Reporter, x.r.Status, x.r.Resolution,
                x.r.CreatedAt, db.ProductReports.Count(o => o.ProductId == x.r.ProductId && o.Status == ProductReportStatus.Open)), request, ct);
    }
}

public record ResolveProductReportCommand(Guid ReportId, bool Ban, string? Resolution) : IRequest<Unit>;

public sealed class ResolveProductReportValidator : AbstractValidator<ResolveProductReportCommand>
{
    public ResolveProductReportValidator() =>
        RuleFor(x => x.Resolution).NotEmpty().When(x => x.Ban).WithMessage("Khoá sản phẩm phải ghi lý do.");
}

/// <summary>Ban closes every open report of that product; dismiss closes only this one.</summary>
public sealed class ResolveProductReportHandler(IApplicationDbContext db, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : IRequestHandler<ResolveProductReportCommand, Unit>
{
    public async Task<Unit> Handle(ResolveProductReportCommand request, CancellationToken ct)
    {
        var report = await db.ProductReports.FirstOrDefaultAsync(r => r.Id == request.ReportId, ct) ?? throw new NotFoundException("Không tìm thấy báo cáo.");
        var adminId = (currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục."));
        var now = clock.UtcNow;
        if (request.Ban)
        {
            var product = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == report.ProductId, ct);
            if (product.Status != ProductStatus.Banned)
            {
                product.Ban(request.Resolution!.Trim());
                outbox.Enqueue(OutboxTypes.ProductEvent, new ProductEventPayload(product.Id, "BAN", request.Resolution));
            }
            foreach (var open in await db.ProductReports.Where(r => r.ProductId == report.ProductId && r.Status == ProductReportStatus.Open).ToListAsync(ct))
                open.Resolve(ProductReportStatus.Banned, adminId, request.Resolution, now);
        }
        else report.Resolve(ProductReportStatus.Dismissed, adminId, request.Resolution, now);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
