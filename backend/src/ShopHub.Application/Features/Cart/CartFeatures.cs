using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Cart;

/// <summary>Whose cart: the signed-in user, otherwise the guest token from the cookie (may be null = no cart yet).</summary>
public record CartOwner(Guid? UserId, string? GuestToken);

public record CartLineDto(
    Guid SkuId,
    Guid ProductId,
    string Name,
    string? ImageUrl,
    string? Variant,
    long Price,
    long OriginalPrice,
    long? PreviousPrice,
    int Quantity,
    int Available,
    bool IsSelected,
    bool CanBuy,
    string? Problem,
    // When the line was put in the cart — the header's "Sản phẩm mới thêm" across every shop (E2)
    DateTimeOffset AddedAt,
    // "Flash Sale" / "Giảm giá" when a price programme gives the price above (add-on deals and combos show at checkout)
    string? PriceLabel = null);

public record CartShopDto(Guid ShopId, string ShopName, string ShopSlug, bool IsMall, bool OnVacation, IReadOnlyList<CartLineDto> Lines);

public record CartDto(IReadOnlyList<CartShopDto> Shops, int LineCount, int TotalQuantity, int SelectedQuantity, long SelectedSubtotal);

/// <summary>Loads / creates carts and turns them into the validated view (stock, price, availability per line).</summary>
public sealed class CartStore(IApplicationDbContext db, Marketing.PriceBook prices, PurchaseLimits purchaseLimits, IClock clock)
{
    public async Task<Domain.Sales.Cart?> FindAsync(CartOwner owner, CancellationToken ct)
    {
        if (owner.UserId is { } userId)
            return await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (!string.IsNullOrEmpty(owner.GuestToken))
            return await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.GuestToken == owner.GuestToken, ct);
        return null;
    }

    public async Task<Domain.Sales.Cart> GetOrCreateAsync(CartOwner owner, CancellationToken ct)
    {
        var cart = await FindAsync(owner, ct);
        if (cart is not null) return cart;
        cart = owner.UserId is { } userId
            ? Domain.Sales.Cart.ForUser(userId, clock.UtcNow)
            : Domain.Sales.Cart.ForGuest(owner.GuestToken ?? throw new InvalidOperationException("Guest cart needs a token."), clock.UtcNow);
        db.Carts.Add(cart);
        return cart;
    }

    public async Task<CartDto> ViewAsync(CartOwner owner, CancellationToken ct)
    {
        var cart = await FindAsync(owner, ct);
        if (cart is null || cart.Items.Count == 0) return new CartDto([], 0, 0, 0, 0);
        var lines = await LinesAsync(owner.UserId, cart.Items, ct);
        var shops = lines.GroupBy(l => l.Shop.Id)
            .Select(g => new CartShopDto(g.Key, g.First().Shop.Name, g.First().Shop.Slug, g.First().Shop.Type == ShopType.Mall,
                g.First().Shop.Status == ShopStatus.Vacation,
                g.OrderByDescending(l => l.Item.AddedAt).ThenBy(l => l.Item.SkuId).Select(l => l.Dto).ToList()))
            // Most recently touched shop first, like the buyer site
            .OrderByDescending(s => lines.Where(l => l.Shop.Id == s.ShopId).Max(l => l.Item.AddedAt))
            .ToList();
        var selected = lines.Where(l => l.Dto.IsSelected && l.Dto.CanBuy).ToList();
        return new CartDto(shops, lines.Count, lines.Sum(l => l.Dto.Quantity), selected.Sum(l => l.Dto.Quantity),
            selected.Sum(l => l.Dto.Price * l.Dto.Quantity));
    }

    public sealed record LineView(CartItem Item, Shop Shop, CartLineDto Dto);

    private async Task<List<LineView>> LinesAsync(Guid? userId, IReadOnlyCollection<CartItem> items, CancellationToken ct)
    {
        var skuIds = items.Select(i => i.SkuId).ToList();
        var rows = await (from s in db.Skus.IgnoreQueryFilters().AsNoTracking()
                          join p in db.Products.IgnoreQueryFilters().AsNoTracking() on s.ProductId equals p.Id
                          join sh in db.Shops.AsNoTracking() on p.ShopId equals sh.Id
                          where skuIds.Contains(s.Id)
                          select new
                          {
                              Sku = s,
                              p.Id,
                              p.Name,
                              p.Status,
                              Shop = sh,
                              Option1 = db.VariantOptions.Where(o => o.Id == s.Option1Id).Select(o => o.Value).FirstOrDefault(),
                              Option2 = db.VariantOptions.Where(o => o.Id == s.Option2Id).Select(o => o.Value).FirstOrDefault(),
                              OptionImage = db.VariantOptions.Where(o => o.Id == s.Option1Id).Select(o => o.ImageUrl).FirstOrDefault(),
                              Image = p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                          }).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Sku.Id);
        var effective = await prices.ForSkusAsync(skuIds, clock.UtcNow, ct);
        // Units of each limited product in the cart (all its variants) against what the buyer may still buy
        var limits = await purchaseLimits.ForAsync(userId, rows.Select(r => r.Id).Distinct().ToList(), ct);
        var inCart = items.Where(i => byId.ContainsKey(i.SkuId)).GroupBy(i => byId[i.SkuId].Id).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var result = new List<LineView>();
        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.SkuId, out var r)) continue;
            var available = Math.Max(0, r.Sku.Available);
            var problem = r.Status != ProductStatus.Active || !r.Sku.IsActive ? "Sản phẩm đã ngừng bán."
                : r.Shop.Status == ShopStatus.Vacation ? "Shop đang tạm nghỉ, chưa thể đặt hàng."
                : r.Shop.Status != ShopStatus.Active ? "Shop đã ngừng hoạt động."
                : available == 0 ? "Hết hàng."
                : item.Quantity > available ? $"Chỉ còn {available} sản phẩm, vui lòng giảm số lượng."
                : limits.TryGetValue(r.Id, out var limit) && inCart[r.Id] + limit.Bought > limit.Max ? PurchaseLimits.Message(r.Name, limit)
                : null;
            var variant = string.Join(", ", new[] { r.Option1, r.Option2 }.Where(v => !string.IsNullOrEmpty(v)));
            var price = effective.GetValueOrDefault(item.SkuId);
            var unit = price?.Price ?? r.Sku.Price;
            var label = price?.Kind switch { null => null, Domain.Promo.PriceProgramKind.Discount => "Giảm giá", _ => "Flash Sale" };
            result.Add(new LineView(item, r.Shop, new CartLineDto(item.SkuId, r.Id, r.Name, r.OptionImage ?? r.Image,
                variant.Length == 0 ? null : variant, unit, Math.Max(r.Sku.OriginalPrice, r.Sku.Price),
                item.PriceAtAdd != r.Sku.Price ? item.PriceAtAdd : null, item.Quantity, available, item.IsSelected, problem is null, problem, item.AddedAt, label)));
        }
        return result;
    }
}

internal static class CartRules
{
    /// <summary>The SKU must be sellable right now; returns it with its product.</summary>
    public static async Task<(Sku Sku, Product Product)> SellableSkuAsync(IApplicationDbContext db, Guid skuId, CancellationToken ct)
    {
        var row = await (from s in db.Skus
                         join p in db.Products on s.ProductId equals p.Id
                         where s.Id == skuId
                         select new { s, p }).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        if (row.p.Status != ProductStatus.Active || !row.s.IsActive) throw new ConflictException("Sản phẩm đã ngừng bán.", "NOT_SELLABLE");
        var shop = await db.Shops.AsNoTracking().Where(x => x.Id == row.p.ShopId).Select(x => x.Status).SingleAsync(ct);
        if (shop == ShopStatus.Vacation) throw new ConflictException("Shop đang tạm nghỉ, chưa thể đặt hàng.", "SHOP_VACATION");
        if (shop != ShopStatus.Active) throw new ConflictException("Shop đã ngừng hoạt động.", "SHOP_INACTIVE");
        return (row.s, row.p);
    }

    /// <summary>Units of the product (all variants) currently in the cart.</summary>
    public static async Task<int> ProductQuantityAsync(IApplicationDbContext db, Domain.Sales.Cart cart, Guid productId, CancellationToken ct)
    {
        var skuIds = cart.Items.Select(i => i.SkuId).ToList();
        var ofProduct = await db.Skus.IgnoreQueryFilters().Where(s => skuIds.Contains(s.Id) && s.ProductId == productId).Select(s => s.Id).ToListAsync(ct);
        return cart.Items.Where(i => ofProduct.Contains(i.SkuId)).Sum(i => i.Quantity);
    }

    public static void EnsureAvailable(Sku sku, int quantity)
    {
        if (sku.Available <= 0) throw new ConflictException("Sản phẩm đã hết hàng.", "OUT_OF_STOCK");
        if (quantity > sku.Available) throw new ConflictException($"Chỉ còn {sku.Available} sản phẩm.", "NOT_ENOUGH_STOCK");
    }
}

// ---------- queries / commands ----------

public record GetCartQuery(CartOwner Owner) : IRequest<CartDto>;

public sealed class GetCartHandler(CartStore store) : IRequestHandler<GetCartQuery, CartDto>
{
    public Task<CartDto> Handle(GetCartQuery request, CancellationToken ct) => store.ViewAsync(request.Owner, ct);
}

public record AddCartItemCommand(CartOwner Owner, Guid SkuId, int Quantity) : IRequest<CartDto>;

public sealed class AddCartItemValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, CartItem.MaxQuantity).WithMessage($"Số lượng từ 1 đến {CartItem.MaxQuantity}.");
    }
}

public sealed class AddCartItemHandler(IApplicationDbContext db, CartStore store, PurchaseLimits limits, ISystemParameters parameters, IClock clock)
    : IRequestHandler<AddCartItemCommand, CartDto>
{
    public async Task<CartDto> Handle(AddCartItemCommand request, CancellationToken ct)
    {
        var (sku, product) = await CartRules.SellableSkuAsync(db, request.SkuId, ct);
        var maxLines = (int)await parameters.GetIntAsync(ParameterKeys.CartMaxLines, ct);
        var cart = await store.GetOrCreateAsync(request.Owner, ct);
        var already = cart.Find(sku.Id)?.Quantity ?? 0;
        CartRules.EnsureAvailable(sku, already + request.Quantity);
        await limits.EnsureAsync(request.Owner.UserId, product.Id, product.Name,
            await CartRules.ProductQuantityAsync(db, cart, product.Id, ct) + request.Quantity, ct);
        cart.Add(sku.Id, request.Quantity, sku.Price, maxLines, clock.UtcNow);
        // The event behind the "thêm vào giỏ" step of the conversion funnel (the cart only keeps its current state)
        db.CartAdds.Add(new Domain.Engage.CartAdd(request.Owner.UserId, request.Owner.UserId is null ? request.Owner.GuestToken : null, sku.ProductId,
            sku.Id, request.Quantity, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return await store.ViewAsync(request.Owner, ct);
    }
}

/// <summary>Change quantity, tick/untick, or switch to another variant (<paramref name="NewSkuId"/>) of the same product.</summary>
public record UpdateCartItemCommand(CartOwner Owner, Guid SkuId, int? Quantity, bool? Selected, Guid? NewSkuId) : IRequest<CartDto>;

public sealed class UpdateCartItemValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, CartItem.MaxQuantity).When(x => x.Quantity is not null)
            .WithMessage($"Số lượng từ 1 đến {CartItem.MaxQuantity}.");
    }
}

public sealed class UpdateCartItemHandler(IApplicationDbContext db, CartStore store, PurchaseLimits limits, IClock clock) : IRequestHandler<UpdateCartItemCommand, CartDto>
{
    public async Task<CartDto> Handle(UpdateCartItemCommand request, CancellationToken ct)
    {
        var cart = await store.FindAsync(request.Owner, ct);
        var line = cart?.Find(request.SkuId) ?? throw new NotFoundException("Sản phẩm không có trong giỏ hàng.");
        var now = clock.UtcNow;

        if (request.NewSkuId is { } newSkuId && newSkuId != request.SkuId)
        {
            var currentProduct = await db.Skus.IgnoreQueryFilters().Where(s => s.Id == request.SkuId).Select(s => s.ProductId).SingleAsync(ct);
            var (target, product) = await CartRules.SellableSkuAsync(db, newSkuId, ct);
            if (product.Id != currentProduct) throw new ConflictException("Chỉ đổi được sang phân loại khác của cùng sản phẩm.", "OTHER_PRODUCT");
            CartRules.EnsureAvailable(target, request.Quantity ?? line.Quantity);
            cart!.ChangeSku(request.SkuId, newSkuId, target.Price, now);
            line = cart.Find(newSkuId)!;
        }
        if (request.Quantity is { } quantity)
        {
            var sku = await db.Skus.AsNoTracking().SingleAsync(s => s.Id == line.SkuId, ct);
            // Lowering the quantity is always allowed (it fixes "chỉ còn N"); raising needs the stock
            if (quantity > line.Quantity)
            {
                CartRules.EnsureAvailable(sku, quantity);
                var name = await db.Products.Where(p => p.Id == sku.ProductId).Select(p => p.Name).SingleAsync(ct);
                await limits.EnsureAsync(request.Owner.UserId, sku.ProductId, name,
                    await CartRules.ProductQuantityAsync(db, cart!, sku.ProductId, ct) - line.Quantity + quantity, ct);
            }
            line.SetQuantity(quantity, now);
            line.AcknowledgePrice(sku.Price);
        }
        if (request.Selected is { } selected) line.SetSelected(selected);
        cart!.Touch(now);
        await db.SaveChangesAsync(ct);
        return await store.ViewAsync(request.Owner, ct);
    }
}

public record RemoveCartItemsCommand(CartOwner Owner, IReadOnlyList<Guid> SkuIds) : IRequest<CartDto>;

public sealed class RemoveCartItemsHandler(IApplicationDbContext db, CartStore store, IClock clock) : IRequestHandler<RemoveCartItemsCommand, CartDto>
{
    public async Task<CartDto> Handle(RemoveCartItemsCommand request, CancellationToken ct)
    {
        var cart = await store.FindAsync(request.Owner, ct);
        if (cart is null) return new CartDto([], 0, 0, 0, 0);
        cart.Remove(request.SkuIds, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await store.ViewAsync(request.Owner, ct);
    }
}

/// <summary>Tick / untick everything, or one shop's block.</summary>
public record SelectCartItemsCommand(CartOwner Owner, Guid? ShopId, bool Selected) : IRequest<CartDto>;

public sealed class SelectCartItemsHandler(IApplicationDbContext db, CartStore store, IClock clock) : IRequestHandler<SelectCartItemsCommand, CartDto>
{
    public async Task<CartDto> Handle(SelectCartItemsCommand request, CancellationToken ct)
    {
        var cart = await store.FindAsync(request.Owner, ct);
        if (cart is null) return new CartDto([], 0, 0, 0, 0);
        var skuIds = cart.Items.Select(i => i.SkuId).ToList();
        var inShop = request.ShopId is { } shopId
            ? (await (from s in db.Skus.IgnoreQueryFilters()
                      join p in db.Products.IgnoreQueryFilters() on s.ProductId equals p.Id
                      where skuIds.Contains(s.Id) && p.ShopId == shopId
                      select s.Id).ToListAsync(ct)).ToHashSet()
            : skuIds.ToHashSet();
        foreach (var item in cart.Items.Where(i => inShop.Contains(i.SkuId))) item.SetSelected(request.Selected);
        cart.Touch(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await store.ViewAsync(request.Owner, ct);
    }
}

/// <summary>Called right after sign-in: the guest cart's lines move into the account's cart and the guest cart is removed.</summary>
public record MergeGuestCartCommand(Guid UserId, string GuestToken) : IRequest<Unit>;

public sealed class MergeGuestCartHandler(IApplicationDbContext db, CartStore store, ISystemParameters parameters, IClock clock)
    : IRequestHandler<MergeGuestCartCommand, Unit>
{
    public async Task<Unit> Handle(MergeGuestCartCommand request, CancellationToken ct)
    {
        await db.InLockedTransactionAsync($"cart:{request.UserId}", async () =>
        {
            var guest = await store.FindAsync(new CartOwner(null, request.GuestToken), ct);
            if (guest is null) return Unit.Value;
            if (guest.Items.Count > 0)
            {
                var cart = await store.GetOrCreateAsync(new CartOwner(request.UserId, null), ct);
                cart.MergeFrom(guest, (int)await parameters.GetIntAsync(ParameterKeys.CartMaxLines, ct), clock.UtcNow);
            }
            db.Carts.Remove(guest);
            await db.SaveChangesAsync(ct);
            return Unit.Value;
        }, ct);
        return Unit.Value;
    }
}
