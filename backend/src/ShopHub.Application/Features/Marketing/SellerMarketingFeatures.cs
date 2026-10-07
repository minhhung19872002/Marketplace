using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

public record PromotionSkuDto(Guid SkuId, Guid ProductId, string ProductName, string? Variant, long Price, long BasePrice);

public record PromotionDto(Guid Id, PromotionType Type, string TypeLabel, string Name, DateTimeOffset StartAt, DateTimeOffset EndAt, PromotionStatus Status,
    string State, IReadOnlyList<Guid> ProductIds, IReadOnlyList<string> ProductNames, IReadOnlyList<PromotionSkuDto> Skus, int MinQuantity, int DiscountBp,
    long DiscountAmount, int MaxAddOnQuantity, long MinSpend, Guid? GiftSkuId, string? GiftName, int GiftQuantity);

public record PromotionSkuInput(Guid SkuId, long Price);

public record PromotionInput(PromotionType Type, string Name, DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<Guid>? ProductIds,
    IReadOnlyList<PromotionSkuInput>? Skus, int MinQuantity, int DiscountBp, long DiscountAmount, int MaxAddOnQuantity, long MinSpend, Guid? GiftSkuId,
    int GiftQuantity);

internal static class MarketingViews
{
    public static string State(DateTimeOffset start, DateTimeOffset end, bool stopped, DateTimeOffset now) =>
        stopped ? "Đã dừng" : now < start ? "Sắp diễn ra" : now >= end ? "Đã kết thúc" : "Đang diễn ra";

    /// <summary>Product name and "Đỏ, L" of each SKU.</summary>
    public static async Task<Dictionary<Guid, (Guid ProductId, string Name, string? Variant, long Price, Guid ShopId, Guid CategoryId, double Rating)>> SkusAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var rows = await (from s in db.Skus.AsNoTracking().IgnoreQueryFilters()
                          join p in db.Products.AsNoTracking().IgnoreQueryFilters() on s.ProductId equals p.Id
                          where skuIds.Contains(s.Id)
                          select new { s.Id, s.Option1Id, s.Option2Id, s.Price, ProductId = p.Id, p.Name, p.ShopId, p.CategoryId, p.RatingAvg }).ToListAsync(ct);
        var optionIds = rows.SelectMany(r => new[] { r.Option1Id, r.Option2Id }).Where(o => o is not null).Select(o => o!.Value).Distinct().ToList();
        var options = await db.VariantOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Value, ct);
        return rows.ToDictionary(r => r.Id, r =>
        {
            var variant = string.Join(", ", new[] { r.Option1Id, r.Option2Id }.Where(o => o is not null).Select(o => options.GetValueOrDefault(o!.Value)));
            return (r.ProductId, r.Name, variant.Length == 0 ? (string?)null : variant, r.Price, r.ShopId, r.CategoryId, r.RatingAvg);
        });
    }
}

// ---------- shop programmes ----------

public record ShopPromotionsQuery(Guid ShopId, PromotionType? Type = null) : IRequest<IReadOnlyList<PromotionDto>>;

public sealed class ShopPromotionsHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<ShopPromotionsQuery, IReadOnlyList<PromotionDto>>
{
    public async Task<IReadOnlyList<PromotionDto>> Handle(ShopPromotionsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var promos = await db.Promotions.AsNoTracking().Include(p => p.Products).Include(p => p.Skus)
            .Where(p => p.ShopId == request.ShopId && (request.Type == null || p.Type == request.Type))
            .OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);
        var skuIds = promos.SelectMany(p => p.Skus.Select(s => s.SkuId)).Concat(promos.Where(p => p.GiftSkuId != null).Select(p => p.GiftSkuId!.Value)).Distinct().ToList();
        var skus = await MarketingViews.SkusAsync(db, skuIds, ct);
        var productIds = promos.SelectMany(p => p.Products.Select(x => x.ProductId)).Distinct().ToList();
        var names = await db.Products.AsNoTracking().IgnoreQueryFilters().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var now = clock.UtcNow;
        return promos.Select(p => new PromotionDto(p.Id, p.Type, Promotion.Label(p.Type), p.Name, p.StartAt, p.EndAt, p.Status,
            MarketingViews.State(p.StartAt, p.EndAt, p.Status == PromotionStatus.Stopped, now),
            p.Products.Select(x => x.ProductId).ToList(), p.Products.Select(x => names.GetValueOrDefault(x.ProductId) ?? "").ToList(),
            p.Skus.Select(s => skus.TryGetValue(s.SkuId, out var k)
                ? new PromotionSkuDto(s.SkuId, k.ProductId, k.Name, k.Variant, s.Price, k.Price)
                : new PromotionSkuDto(s.SkuId, Guid.Empty, "", null, s.Price, 0)).ToList(),
            p.MinQuantity, p.DiscountBp, p.DiscountAmount, p.MaxAddOnQuantity, p.MinSpend, p.GiftSkuId,
            p.GiftSkuId is { } g && skus.TryGetValue(g, out var gift) ? gift.Name : null, p.GiftQuantity)).ToList();
    }
}

public record CreatePromotionCommand(Guid ShopId, PromotionInput Input) : IRequest<Guid>;

public sealed class CreatePromotionValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionValidator()
    {
        RuleFor(x => x.Input).NotNull().WithMessage("Thiếu thông tin chương trình.");
        RuleFor(x => x.Input.Name).NotEmpty().WithMessage("Vui lòng nhập tên chương trình.").MaximumLength(150).WithMessage("Tên tối đa 150 ký tự.")
            .When(x => x.Input is not null);
        RuleFor(x => x.Input.Skus).NotEmpty().WithMessage("Vui lòng chọn ít nhất một phân loại.")
            .When(x => x.Input is { Type: PromotionType.Discount or PromotionType.AddOn });
        RuleFor(x => x.Input.ProductIds).NotEmpty().WithMessage("Vui lòng chọn ít nhất một sản phẩm.")
            .When(x => x.Input is { Type: PromotionType.Combo or PromotionType.AddOn or PromotionType.Gift });
        RuleFor(x => x.Input.Skus!.Count).LessThanOrEqualTo(200).WithMessage("Tối đa 200 phân loại mỗi chương trình.").When(x => x.Input?.Skus is not null);
    }
}

/// <summary>
/// Chương trình giảm giá / combo / mua kèm / quà tặng of a shop (spec III.5, 3.10). Discount prices go into the price
/// programme table, where the exclusion constraint refuses a SKU already in another price programme at the same time.
/// </summary>
public sealed class CreatePromotionHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<CreatePromotionCommand, Guid>
{
    public async Task<Guid> Handle(CreatePromotionCommand request, CancellationToken ct)
    {
        var staff = await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var input = request.Input;
        var promo = new Promotion(request.ShopId, input.Type, input.Name, input.StartAt, input.EndAt, staff.UserId, clock.UtcNow);
        await PromotionContent.FillAsync(db, promo, input, ct);
        db.Promotions.Add(promo);
        await db.SaveChangesAsync(ct);
        return promo.Id;
    }
}

public record UpdatePromotionCommand(Guid ShopId, Guid PromotionId, PromotionInput Input) : IRequest<Unit>;

public sealed class UpdatePromotionValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionValidator() =>
        RuleFor(x => new CreatePromotionCommand(x.ShopId, x.Input)).SetValidator(new CreatePromotionValidator()).OverridePropertyName("input");
}

/// <summary>
/// "Sửa" a programme that has not started (D6, L097): name, window and content are replaced; its old price programmes
/// are retired first so the exclusion constraint judges only the new ones.
/// </summary>
public sealed class UpdatePromotionHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<UpdatePromotionCommand, Unit>
{
    public async Task<Unit> Handle(UpdatePromotionCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        var promo = await db.Promotions.Include(p => p.Products).Include(p => p.Skus)
                        .FirstOrDefaultAsync(p => p.Id == request.PromotionId && p.ShopId == request.ShopId, ct)
                    ?? throw new NotFoundException("Không tìm thấy chương trình.");
        if (!promo.CanEdit(now))
            throw new ConflictException("Chương trình đã bắt đầu hoặc đã dừng nên không sửa được — hãy dừng rồi tạo chương trình mới.", "PROMOTION_STARTED");
        if (request.Input.Type != promo.Type) throw new BusinessRuleException("Không đổi được loại chương trình.");
        foreach (var program in await db.PricePrograms.Where(p => p.Kind == PriceProgramKind.Discount && p.RefId == promo.Id && p.IsActive).ToListAsync(ct))
            program.Deactivate();
        promo.Reschedule(request.Input.Name, request.Input.StartAt, request.Input.EndAt, now);
        await db.SaveChangesAsync(ct);
        await PromotionContent.FillAsync(db, promo, request.Input, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

internal static class PromotionContent
{
    /// <summary>Products, SKU prices, the type's settings and (for a discount) the price programmes of <paramref name="promo"/>.</summary>
    public static async Task FillAsync(IApplicationDbContext db, Promotion promo, PromotionInput input, CancellationToken ct)
    {
        var shopId = promo.ShopId;
        var productIds = (input.ProductIds ?? []).Distinct().ToList();
        if (productIds.Count > 0)
        {
            var mine = await db.Products.Where(p => productIds.Contains(p.Id) && p.ShopId == shopId).Select(p => p.Id).ToListAsync(ct);
            if (mine.Count != productIds.Count) throw new NotFoundException("Có sản phẩm không thuộc shop của bạn.");
            foreach (var id in productIds) promo.Products.Add(new PromotionProduct(promo.Id, id));
        }

        var skuInputs = (input.Skus ?? []).GroupBy(s => s.SkuId).Select(g => g.First()).ToList();
        var skus = await MarketingViews.SkusAsync(db, skuInputs.Select(s => s.SkuId).Concat(input.GiftSkuId is { } gid ? [gid] : []).ToList(), ct);
        foreach (var s in skuInputs)
        {
            if (!skus.TryGetValue(s.SkuId, out var sku) || sku.ShopId != shopId) throw new NotFoundException("Có phân loại không thuộc shop của bạn.");
            if (s.Price >= sku.Price)
                throw new BusinessRuleException($"Giá ưu đãi của \"{sku.Name}{(sku.Variant is null ? "" : $" - {sku.Variant}")}\" phải thấp hơn giá bán {Money.Vnd(sku.Price)}.");
            promo.Skus.Add(new PromotionSku(promo.Id, s.SkuId, s.Price, null));
        }

        switch (input.Type)
        {
            case PromotionType.Combo:
                promo.ConfigureCombo(input.MinQuantity, input.DiscountBp, input.DiscountAmount);
                break;
            case PromotionType.AddOn:
                if (skuInputs.Any(s => productIds.Contains(skus[s.SkuId].ProductId)))
                    throw new BusinessRuleException("Sản phẩm mua kèm phải khác sản phẩm chính.");
                promo.ConfigureAddOn(input.MaxAddOnQuantity);
                break;
            case PromotionType.Gift:
                if (input.GiftSkuId is not { } giftSku || !skus.TryGetValue(giftSku, out var gift) || gift.ShopId != shopId)
                    throw new NotFoundException("Không tìm thấy quà tặng trong shop của bạn.");
                promo.ConfigureGift(input.MinSpend, giftSku, input.GiftQuantity);
                break;
        }

        if (input.Type == PromotionType.Discount)
            foreach (var s in promo.Skus)
                db.PricePrograms.Add(new PriceProgram(s.SkuId, shopId, PriceProgramKind.Discount, promo.Id, s.Price, promo.StartAt, promo.EndAt));
    }
}

public record StopPromotionCommand(Guid ShopId, Guid PromotionId) : IRequest<Unit>;

public sealed class StopPromotionHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<StopPromotionCommand, Unit>
{
    public async Task<Unit> Handle(StopPromotionCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var promo = await db.Promotions.FirstOrDefaultAsync(p => p.Id == request.PromotionId && p.ShopId == request.ShopId, ct)
                    ?? throw new NotFoundException("Không tìm thấy chương trình.");
        promo.Stop(clock.UtcNow);
        foreach (var program in await db.PricePrograms.Where(p => p.Kind == PriceProgramKind.Discount && p.RefId == promo.Id && p.IsActive).ToListAsync(ct))
            program.Deactivate();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ---------- Flash Sale (shop side) ----------

public record FlashItemInput(Guid SkuId, long FlashPrice, int Quota, int PerUserLimit);

public record FlashItemDto(Guid Id, Guid SlotId, Guid SkuId, Guid ProductId, string ProductName, string? Variant, long FlashPrice, long BasePrice, int Quota,
    int Sold, int PerUserLimit, FlashItemStatus Status, string? RejectReason);

public record FlashSlotDto(Guid Id, FlashSaleOwner Owner, Guid? ShopId, DateTimeOffset StartAt, DateTimeOffset EndAt, int MinDiscountBp, double MinRating,
    IReadOnlyList<Guid> CategoryIds, FlashSlotStatus Status, string State, IReadOnlyList<FlashItemDto> Items);

internal static class FlashViews
{
    public static async Task<List<FlashItemDto>> ItemsAsync(IApplicationDbContext db, IReadOnlyList<FlashSaleItem> items, CancellationToken ct)
    {
        var skus = await MarketingViews.SkusAsync(db, items.Select(i => i.SkuId).Distinct().ToList(), ct);
        return items.Select(i =>
        {
            var k = skus.GetValueOrDefault(i.SkuId);
            return new FlashItemDto(i.Id, i.SlotId, i.SkuId, i.ProductId, k.Name ?? "", k.Variant, i.FlashPrice, k.Price, i.Quota, i.Sold, i.PerUserLimit,
                i.Status, i.RejectReason);
        }).ToList();
    }

    /// <summary>An approved item goes live: its price programme (exclusion constraint) and its Redis counter.</summary>
    public static void GoLive(IApplicationDbContext db, FlashSaleSlot slot, FlashSaleItem item) =>
        db.PricePrograms.Add(new PriceProgram(item.SkuId, item.ShopId,
            slot.Owner == FlashSaleOwner.Platform ? PriceProgramKind.PlatformFlash : PriceProgramKind.ShopFlash, item.Id, item.FlashPrice, slot.StartAt, slot.EndAt));
}

public record ShopFlashSalesQuery(Guid ShopId) : IRequest<IReadOnlyList<FlashSlotDto>>;

/// <summary>The shop's own flash sales and its registrations to platform slots.</summary>
public sealed class ShopFlashSalesHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<ShopFlashSalesQuery, IReadOnlyList<FlashSlotDto>>
{
    public async Task<IReadOnlyList<FlashSlotDto>> Handle(ShopFlashSalesQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var items = await db.FlashSaleItems.AsNoTracking().Where(i => i.ShopId == request.ShopId).ToListAsync(ct);
        var slotIds = items.Select(i => i.SlotId).Distinct().ToList();
        var slots = await db.FlashSaleSlots.AsNoTracking().Where(s => slotIds.Contains(s.Id) || s.ShopId == request.ShopId)
            .OrderByDescending(s => s.StartAt).Take(100).ToListAsync(ct);
        var views = await FlashViews.ItemsAsync(db, items, ct);
        var now = clock.UtcNow;
        return slots.Select(s => new FlashSlotDto(s.Id, s.Owner, s.ShopId, s.StartAt, s.EndAt, s.MinDiscountBp, s.MinRating, s.CategoryIds, s.Status,
            MarketingViews.State(s.StartAt, s.EndAt, s.Status == FlashSlotStatus.Cancelled, now), views.Where(v => v.SlotId == s.Id).ToList())).ToList();
    }
}

public record CreateShopFlashSaleCommand(Guid ShopId, DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<FlashItemInput> Items) : IRequest<Guid>;

public sealed class CreateShopFlashSaleValidator : AbstractValidator<CreateShopFlashSaleCommand>
{
    public CreateShopFlashSaleValidator() =>
        RuleFor(x => x.Items).NotEmpty().WithMessage("Vui lòng chọn ít nhất một phân loại.").Must(i => i is null || i.Count <= 50)
            .WithMessage("Tối đa 50 phân loại mỗi khung.");
}

/// <summary>Flash Sale của shop: the shop opens its own slot; its items are live at once (no platform review).</summary>
public sealed class CreateShopFlashSaleHandler(IApplicationDbContext db, SellerAccess access, FlashSaleQuota quota, IClock clock)
    : IRequestHandler<CreateShopFlashSaleCommand, Guid>
{
    public async Task<Guid> Handle(CreateShopFlashSaleCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        if (request.EndAt <= now) throw new BusinessRuleException("Thời gian kết thúc đã qua.");
        var slot = new FlashSaleSlot(FlashSaleOwner.Shop, request.ShopId, request.StartAt, request.EndAt, 0, 0, [], now);
        var items = await FlashRegistration.BuildAsync(db, slot, request.ShopId, request.Items, enforceCriteria: false, now, ct);
        db.FlashSaleSlots.Add(slot);
        foreach (var item in items)
        {
            item.Approve(now);
            db.FlashSaleItems.Add(item);
            FlashViews.GoLive(db, slot, item);
        }
        await db.SaveChangesAsync(ct);
        foreach (var item in items) await quota.ReloadAsync(item.Id, ct);
        return slot.Id;
    }
}

public record UpdateShopFlashSaleCommand(Guid ShopId, Guid SlotId, DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<FlashItemInput> Items) : IRequest<Unit>;

public sealed class UpdateShopFlashSaleValidator : AbstractValidator<UpdateShopFlashSaleCommand>
{
    public UpdateShopFlashSaleValidator() =>
        RuleFor(x => x.Items).NotEmpty().WithMessage("Vui lòng chọn ít nhất một phân loại.").Must(i => i is null || i.Count <= 50)
            .WithMessage("Tối đa 50 phân loại mỗi khung.");
}

/// <summary>
/// "Sửa" the shop's own Flash Sale before it starts (D6, L097): window and items are replaced. Nothing has been sold
/// yet, so the old items and their price programmes are simply retired.
/// </summary>
public sealed class UpdateShopFlashSaleHandler(IApplicationDbContext db, SellerAccess access, FlashSaleQuota quota, IClock clock)
    : IRequestHandler<UpdateShopFlashSaleCommand, Unit>
{
    public async Task<Unit> Handle(UpdateShopFlashSaleCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        List<FlashSaleItem> items;
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            var slot = await db.FlashSaleSlots.FirstOrDefaultAsync(s => s.Id == request.SlotId && s.Owner == FlashSaleOwner.Shop && s.ShopId == request.ShopId, ct)
                       ?? throw new NotFoundException("Không tìm thấy Flash Sale.");
            if (!slot.CanEdit(now))
                throw new ConflictException("Flash Sale đã bắt đầu hoặc đã huỷ nên không sửa được.", "FLASH_SALE_STARTED");
            var old = await db.FlashSaleItems.Where(i => i.SlotId == slot.Id).ToListAsync(ct);
            var oldIds = old.Select(i => i.Id).ToList();
            foreach (var program in await db.PricePrograms.Where(p => p.Kind == PriceProgramKind.ShopFlash && oldIds.Contains(p.RefId) && p.IsActive).ToListAsync(ct))
                program.Deactivate();
            db.FlashSaleItems.RemoveRange(old);
            slot.Reschedule(request.StartAt, request.EndAt, now);
            await db.SaveChangesAsync(ct);

            items = await FlashRegistration.BuildAsync(db, slot, request.ShopId, request.Items, enforceCriteria: false, now, ct);
            foreach (var item in items)
            {
                item.Approve(now);
                db.FlashSaleItems.Add(item);
                FlashViews.GoLive(db, slot, item);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        foreach (var item in items) await quota.ReloadAsync(item.Id, ct);
        return Unit.Value;
    }
}

internal static class FlashRegistration
{
    /// <summary>Checks ownership, price below the normal price and — for platform slots — the slot's criteria.</summary>
    public static async Task<List<FlashSaleItem>> BuildAsync(IApplicationDbContext db, FlashSaleSlot slot, Guid shopId, IReadOnlyList<FlashItemInput> inputs,
        bool enforceCriteria, DateTimeOffset now, CancellationToken ct)
    {
        var distinct = inputs.GroupBy(i => i.SkuId).Select(g => g.First()).ToList();
        var skus = await MarketingViews.SkusAsync(db, distinct.Select(i => i.SkuId).ToList(), ct);
        var parents = enforceCriteria && slot.CategoryIds.Count > 0
            ? await db.Categories.AsNoTracking().IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct)
            : [];
        bool InCategories(Guid categoryId)
        {
            for (Guid? c = categoryId; c is not null; c = parents.GetValueOrDefault(c.Value))
                if (slot.CategoryIds.Contains(c.Value)) return true;
            return false;
        }

        var items = new List<FlashSaleItem>();
        foreach (var i in distinct)
        {
            if (!skus.TryGetValue(i.SkuId, out var sku) || sku.ShopId != shopId) throw new NotFoundException("Có phân loại không thuộc shop của bạn.");
            var label = sku.Variant is null ? sku.Name : $"{sku.Name} - {sku.Variant}";
            if (i.FlashPrice >= sku.Price) throw new BusinessRuleException($"Giá Flash Sale của \"{label}\" phải thấp hơn giá bán {Money.Vnd(sku.Price)}.");
            if (enforceCriteria)
            {
                var discountBp = (int)((sku.Price - i.FlashPrice) * 10_000 / sku.Price);
                if (discountBp < slot.MinDiscountBp)
                    throw new BusinessRuleException($"\"{label}\" phải giảm ít nhất {slot.MinDiscountBp / 100.0:0.#}% để đăng ký khung này.");
                if (sku.Rating < slot.MinRating)
                    throw new BusinessRuleException($"\"{label}\" cần đánh giá từ {slot.MinRating:0.#} sao trở lên.");
                if (slot.CategoryIds.Count > 0 && !InCategories(sku.CategoryId))
                    throw new BusinessRuleException($"\"{label}\" không thuộc ngành hàng của khung này.");
            }
            items.Add(new FlashSaleItem(slot.Id, i.SkuId, sku.ProductId, shopId, i.FlashPrice, i.Quota, i.PerUserLimit, now));
        }
        return items;
    }
}

public record OpenPlatformSlotsQuery(Guid ShopId) : IRequest<IReadOnlyList<FlashSlotDto>>;

/// <summary>Platform slots a shop can still register for (not started yet).</summary>
public sealed class OpenPlatformSlotsHandler(IApplicationDbContext db, SellerAccess access, IClock clock) : IRequestHandler<OpenPlatformSlotsQuery, IReadOnlyList<FlashSlotDto>>
{
    public async Task<IReadOnlyList<FlashSlotDto>> Handle(OpenPlatformSlotsQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var now = clock.UtcNow;
        var slots = await db.FlashSaleSlots.AsNoTracking()
            .Where(s => s.Owner == FlashSaleOwner.Platform && s.Status == FlashSlotStatus.Open && s.StartAt > now)
            .OrderBy(s => s.StartAt).Take(50).ToListAsync(ct);
        return slots.Select(s => new FlashSlotDto(s.Id, s.Owner, null, s.StartAt, s.EndAt, s.MinDiscountBp, s.MinRating, s.CategoryIds, s.Status,
            MarketingViews.State(s.StartAt, s.EndAt, false, now), [])).ToList();
    }
}

public record RegisterFlashItemsCommand(Guid ShopId, Guid SlotId, IReadOnlyList<FlashItemInput> Items) : IRequest<int>;

public sealed class RegisterFlashItemsValidator : AbstractValidator<RegisterFlashItemsCommand>
{
    public RegisterFlashItemsValidator() =>
        RuleFor(x => x.Items).NotEmpty().WithMessage("Vui lòng chọn ít nhất một phân loại.").Must(i => i is null || i.Count <= 50)
            .WithMessage("Tối đa 50 phân loại mỗi lần đăng ký.");
}

/// <summary>Đăng ký Flash Sale của sàn: items must meet the slot's criteria; the platform approves them.</summary>
public sealed class RegisterFlashItemsHandler(IApplicationDbContext db, SellerAccess access, ShopPenaltyService penalties, IClock clock)
    : IRequestHandler<RegisterFlashItemsCommand, int>
{
    public async Task<int> Handle(RegisterFlashItemsCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var penalty = await penalties.OfShopAsync(request.ShopId, ct);
        if (penalty.Level >= PenaltyLevel.CampaignBan)
            throw new ConflictException($"Shop có {penalty.Points} điểm phạt (từ {penalty.CampaignBanAt} điểm không được đăng ký chiến dịch của sàn).", "PENALTY_BAN");
        var now = clock.UtcNow;
        var slot = await db.FlashSaleSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SlotId && s.Owner == FlashSaleOwner.Platform, ct)
                   ?? throw new NotFoundException("Không tìm thấy khung Flash Sale.");
        if (slot.Status != FlashSlotStatus.Open || slot.StartAt <= now) throw new ConflictException("Khung này đã đóng đăng ký.", "SLOT_CLOSED");
        var items = await FlashRegistration.BuildAsync(db, slot, request.ShopId, request.Items, enforceCriteria: true, now, ct);
        db.FlashSaleItems.AddRange(items);
        await db.SaveChangesAsync(ct);
        return items.Count;
    }
}

// ---------- SKU picker ----------

public record PickSkuDto(Guid SkuId, Guid ProductId, string ProductName, string? Variant, long Price, int Available, double Rating);

public record ShopSkusQuery(Guid ShopId, string? Q) : IRequest<IReadOnlyList<PickSkuDto>>;

/// <summary>The shop's active SKUs (product name / SKU code search) to put into a programme.</summary>
public sealed class ShopSkusHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopSkusQuery, IReadOnlyList<PickSkuDto>>
{
    public async Task<IReadOnlyList<PickSkuDto>> Handle(ShopSkusQuery request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.MarketingManage, ct);
        var q = request.Q?.Trim().ToLower() ?? "";
        var ids = await (from s in db.Skus.AsNoTracking()
                         join p in db.Products.AsNoTracking() on s.ProductId equals p.Id
                         where p.ShopId == request.ShopId && p.Status == ProductStatus.Active && s.IsActive
                               && (q == "" || p.Name.ToLower().Contains(q) || (s.SellerSku != null && s.SellerSku.ToLower().Contains(q)))
                         orderby p.Name, s.Price, s.Id
                         select new { s.Id, Available = s.Stock - s.Reserved }).Take(100).ToListAsync(ct);
        var skus = await MarketingViews.SkusAsync(db, ids.Select(i => i.Id).ToList(), ct);
        return ids.Select(i =>
        {
            var k = skus[i.Id];
            return new PickSkuDto(i.Id, k.ProductId, k.Name, k.Variant, k.Price, i.Available, k.Rating);
        }).ToList();
    }
}
