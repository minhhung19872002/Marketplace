using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Admin;

// ---------- Product moderation ----------

public record ReviewQueueRowDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    Guid ShopId,
    string ShopName,
    IReadOnlyList<string> CategoryPath,
    long MinPrice,
    long MaxPrice,
    ProductStatus Status,
    string? Flags,
    DateTimeOffset? SubmittedAt);

public record ListProductsForReviewQuery(
    ProductStatus Status = ProductStatus.PendingReview,
    string? Q = null,
    bool FlaggedOnly = false,
    int Page = 1,
    int PageSize = PagingLimits.DefaultPageSize) : IRequest<PagedResult<ReviewQueueRowDto>>, IPagedRequest;

public sealed class ListProductsForReviewValidator : AbstractValidator<ListProductsForReviewQuery>
{
    public ListProductsForReviewValidator() => this.ApplyPagingRules();
}

public sealed class ListProductsForReviewHandler(IApplicationDbContext db)
    : IRequestHandler<ListProductsForReviewQuery, PagedResult<ReviewQueueRowDto>>
{
    public async Task<PagedResult<ReviewQueueRowDto>> Handle(ListProductsForReviewQuery request, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking().Where(p => p.Status == request.Status);
        if (request.FlaggedOnly) products = products.Where(p => p.Flags != null);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToLower();
            products = products.Where(p => p.Name.ToLower().Contains(q));
        }

        // Oldest submission first: first come, first reviewed
        var page = await products
            .OrderBy(p => p.SubmittedAt).ThenBy(p => p.Id)
            .ToPagedResultAsync(p => new
            {
                p.Id, p.Name, p.ShopId, p.CategoryId, p.MinPrice, p.MaxPrice, p.Status, p.Flags, p.SubmittedAt,
                Image = p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                ShopName = db.Shops.Where(s => s.Id == p.ShopId).Select(s => s.Name).FirstOrDefault(),
            }, request, ct);

        var rows = new List<ReviewQueueRowDto>();
        foreach (var r in page.Items)
            rows.Add(new ReviewQueueRowDto(r.Id, r.Name, r.Image, r.ShopId, r.ShopName ?? "", await ProductLoader.CategoryPathAsync(db, r.CategoryId, ct),
                r.MinPrice, r.MaxPrice, r.Status, r.Flags, r.SubmittedAt));
        return new PagedResult<ReviewQueueRowDto>(rows, page.TotalCount, page.Page, page.PageSize);
    }
}

public record GetProductForAdminQuery(Guid ProductId) : IRequest<SellerProductDetailDto>;

public sealed class GetProductForAdminHandler(IApplicationDbContext db) : IRequestHandler<GetProductForAdminQuery, SellerProductDetailDto>
{
    public async Task<SellerProductDetailDto> Handle(GetProductForAdminQuery request, CancellationToken ct)
    {
        var shopId = await db.Products.Where(p => p.Id == request.ProductId).Select(p => (Guid?)p.ShopId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        var product = await ProductLoader.LoadForEditAsync(db, shopId, request.ProductId, ct);
        return ProductLoader.ToDetail(product, await ProductLoader.CategoryPathAsync(db, product.CategoryId, ct));
    }
}

public enum ModerationAction
{
    Approve,
    Reject,
    Ban,
    Unban,
}

public record ModerateProductCommand(Guid ProductId, ModerationAction Action, string? Reason) : IRequest<ProductStatus>;

public sealed class ModerateProductValidator : AbstractValidator<ModerateProductCommand>
{
    public ModerateProductValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng ghi lý do.")
            .When(x => x.Action is ModerationAction.Reject or ModerationAction.Ban);
}

public sealed class ModerateProductHandler(IApplicationDbContext db, IOutbox outbox, IClock clock)
    : IRequestHandler<ModerateProductCommand, ProductStatus>
{
    public async Task<ProductStatus> Handle(ModerateProductCommand request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        switch (request.Action)
        {
            case ModerationAction.Approve:
                product.Approve(clock.UtcNow);
                break;
            case ModerationAction.Reject:
                product.Reject(request.Reason!);
                break;
            case ModerationAction.Ban:
                product.Ban(request.Reason!);
                break;
            case ModerationAction.Unban:
                product.Unban();
                break;
        }
        outbox.Enqueue(OutboxTypes.ProductEvent, new ProductEventPayload(product.Id, request.Action.ToString().ToUpperInvariant(), request.Reason));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Sản phẩm vừa được người khác xử lý. Vui lòng tải lại.", "STALE_VERSION");
        }
        return product.Status;
    }
}

// ---------- Shops ----------

public record AdminShopRowDto(
    Guid Id,
    string Name,
    ShopType Type,
    ShopStatus Status,
    bool IsPreferred,
    string OwnerName,
    string? OwnerPhone,
    int ProductCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt);

public record ListShopsQuery(ShopStatus? Status = null, string? Q = null, int Page = 1, int PageSize = PagingLimits.DefaultPageSize)
    : IRequest<PagedResult<AdminShopRowDto>>, IPagedRequest;

public sealed class ListShopsValidator : AbstractValidator<ListShopsQuery>
{
    public ListShopsValidator() => this.ApplyPagingRules();
}

public sealed class ListShopsHandler(IApplicationDbContext db) : IRequestHandler<ListShopsQuery, PagedResult<AdminShopRowDto>>
{
    public async Task<PagedResult<AdminShopRowDto>> Handle(ListShopsQuery request, CancellationToken ct)
    {
        var shops = db.Shops.AsNoTracking();
        if (request.Status is { } status) shops = shops.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToLower();
            shops = shops.Where(s => s.Name.ToLower().Contains(q));
        }
        return await shops
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .ToPagedResultAsync(s => new AdminShopRowDto(s.Id, s.Name, s.Type, s.Status, s.IsPreferred,
                db.Users.IgnoreQueryFilters().Where(u => u.Id == s.OwnerId).Select(u => u.FullName).FirstOrDefault() ?? "",
                db.Users.IgnoreQueryFilters().Where(u => u.Id == s.OwnerId).Select(u => u.Phone).FirstOrDefault(),
                db.Products.Count(p => p.ShopId == s.Id), s.CreatedAt, s.ApprovedAt), request, ct);
    }
}

public record ShopKycDto(
    string LegalName,
    string? TaxCode,
    string? IdCardNumberMasked,
    KycStatus Status,
    string? IdCardFrontUrl,
    string? IdCardBackUrl,
    string? BusinessLicenseUrl,
    string? RejectReason,
    DateTimeOffset? ReviewedAt);

public record WarehouseDto(string ContactName, string Phone, string Address);

public record AdminShopDetailDto(
    Guid Id,
    string Name,
    string Description,
    ShopType Type,
    ShopStatus Status,
    bool IsPreferred,
    string? RejectReason,
    string? LockReason,
    Guid OwnerId,
    string OwnerName,
    string? OwnerPhone,
    ShopKycDto? Kyc,
    WarehouseDto? Warehouse,
    string? BankName,
    string? BankAccountLast4,
    DateTimeOffset CreatedAt);

public record GetShopForAdminQuery(Guid ShopId) : IRequest<AdminShopDetailDto>;

/// <summary>KYC files are only exposed as signed URLs valid for a few minutes, generated per request.</summary>
public sealed class GetShopForAdminHandler(IApplicationDbContext db, IObjectStorage storage, IDataEncryptor encryptor)
    : IRequestHandler<GetShopForAdminQuery, AdminShopDetailDto>
{
    private static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromMinutes(5);

    public async Task<AdminShopDetailDto> Handle(GetShopForAdminQuery request, CancellationToken ct)
    {
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ShopId, ct)
            ?? throw new NotFoundException("Không tìm thấy shop.");
        var owner = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == shop.OwnerId)
            .Select(u => new { u.FullName, u.Phone }).FirstOrDefaultAsync(ct);
        var kyc = await db.ShopKycs.AsNoTracking().FirstOrDefaultAsync(k => k.ShopId == shop.Id, ct);
        var warehouse = await db.ShopWarehouses.AsNoTracking().Where(w => w.ShopId == shop.Id && w.IsPickupDefault).FirstOrDefaultAsync(ct);
        var bank = await db.ShopBankAccounts.AsNoTracking().Where(b => b.ShopId == shop.Id && b.IsDefault).FirstOrDefaultAsync(ct);

        async Task<string?> Sign(string? key) => key is null ? null : await storage.SignedUrlAsync(Buckets.Kyc, KycObject(key), SignedUrlLifetime, ct);

        ShopKycDto? kycDto = null;
        if (kyc is not null)
        {
            string? masked = null;
            if (kyc.IdCardNumberEncrypted is { } enc)
            {
                var plain = encryptor.Decrypt(enc);
                masked = plain.Length > 4 ? $"{new string('*', plain.Length - 4)}{plain[^4..]}" : "****";
            }
            kycDto = new ShopKycDto(kyc.LegalName, kyc.TaxCode, masked, kyc.Status, await Sign(kyc.IdCardFrontKey),
                await Sign(kyc.IdCardBackKey), await Sign(kyc.BusinessLicenseKey), kyc.RejectReason, kyc.ReviewedAt);
        }

        WarehouseDto? warehouseDto = null;
        if (warehouse is not null)
        {
            var codes = new[] { warehouse.WardCode, warehouse.DistrictCode, warehouse.ProvinceCode };
            var names = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
            warehouseDto = new WarehouseDto(warehouse.ContactName, warehouse.Phone,
                string.Join(", ", new[] { warehouse.Street }.Concat(codes.Select(c => names.GetValueOrDefault(c) ?? c))));
        }

        return new AdminShopDetailDto(shop.Id, shop.Name, shop.Description, shop.Type, shop.Status, shop.IsPreferred, shop.RejectReason,
            shop.LockReason, shop.OwnerId, owner?.FullName ?? "", owner?.Phone, kycDto, warehouseDto, bank?.BankCode, bank?.AccountNoLast4,
            shop.CreatedAt);
    }

    // KYC images are stored as {key}_1600.webp, PDFs under their own key
    private static string KycObject(string key) => key.EndsWith(".pdf", StringComparison.Ordinal) ? key : Media.ImageSizes.Key(key, Media.ImageSizes.Document);
}

public enum ShopAdminAction
{
    Approve,
    Reject,
    Lock,
    Unlock,
}

public record ModerateShopCommand(Guid ShopId, ShopAdminAction Action, string? Reason) : IRequest<ShopStatus>;

public sealed class ModerateShopValidator : AbstractValidator<ModerateShopCommand>
{
    public ModerateShopValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng ghi lý do.")
            .When(x => x.Action is ShopAdminAction.Reject or ShopAdminAction.Lock);
}

public sealed class ModerateShopHandler(IApplicationDbContext db, IOutbox outbox, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ModerateShopCommand, ShopStatus>
{
    public async Task<ShopStatus> Handle(ModerateShopCommand request, CancellationToken ct)
    {
        try
        {
            // Re-run on a stale version: counters bump the row; another admin's decision fails the state checks below instead
            return await db.RetryOnStaleAsync(async () =>
            {
                var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == request.ShopId, ct) ?? throw new NotFoundException("Không tìm thấy shop.");
                var kyc = await db.ShopKycs.FirstOrDefaultAsync(k => k.ShopId == shop.Id, ct);
                var reviewer = currentUser.UserId ?? Guid.Empty;
                switch (request.Action)
                {
                    case ShopAdminAction.Approve:
                        shop.Approve(clock.UtcNow);
                        kyc?.Review(true, reviewer, null, clock.UtcNow);
                        // The payout account was checked together with the KYC file
                        foreach (var bank in await db.ShopBankAccounts.Where(b => b.ShopId == shop.Id && b.VerifiedAt == null).ToListAsync(ct))
                            bank.MarkVerified(clock.UtcNow);
                        break;
                    case ShopAdminAction.Reject:
                        shop.Reject(request.Reason!);
                        kyc?.Review(false, reviewer, request.Reason, clock.UtcNow);
                        break;
                    case ShopAdminAction.Lock:
                        shop.Lock(request.Reason!);
                        break;
                    case ShopAdminAction.Unlock:
                        shop.Unlock();
                        break;
                }
                outbox.Enqueue(OutboxTypes.ShopEvent, new ShopEventPayload(shop.Id, request.Action.ToString().ToUpperInvariant(), request.Reason));
                await db.SaveChangesAsync(ct);
                return shop.Status;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Shop vừa được người khác xử lý. Vui lòng tải lại.", "STALE_VERSION");
        }
    }
}

public record SetShopLabelsCommand(Guid ShopId, bool IsMall, bool IsPreferred) : IRequest<Unit>;

public sealed class SetShopLabelsHandler(IApplicationDbContext db) : IRequestHandler<SetShopLabelsCommand, Unit>
{
    public async Task<Unit> Handle(SetShopLabelsCommand request, CancellationToken ct)
    {
        await db.RetryOnStaleAsync(async () =>
        {
            var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == request.ShopId, ct) ?? throw new NotFoundException("Không tìm thấy shop.");
            if (shop.Status != ShopStatus.Active && shop.Status != ShopStatus.Vacation)
                throw new ConflictException("Chỉ cấp nhãn cho shop đang hoạt động.", "SHOP_NOT_ACTIVE");
            shop.SetLabels(request.IsMall, request.IsPreferred);
            await db.SaveChangesAsync(ct);
        });
        return Unit.Value;
    }
}
