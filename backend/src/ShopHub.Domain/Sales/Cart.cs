using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

/// <summary>
/// A cart belongs to a user, or to a guest token (cookie) until that guest signs in and the lines are merged.
/// Lines point at SKUs (one line per SKU), never at products.
/// </summary>
public class Cart : Entity
{
    private Cart() { }

    private Cart(Guid? userId, string? guestToken, DateTimeOffset now)
    {
        UserId = userId;
        GuestToken = guestToken;
        UpdatedAt = now;
    }

    public static Cart ForUser(Guid userId, DateTimeOffset now) => new(userId, null, now);

    public static Cart ForGuest(string guestToken, DateTimeOffset now) => new(null, guestToken, now);

    public Guid? UserId { get; private set; }
    public string? GuestToken { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public List<CartItem> Items { get; private set; } = [];

    public CartItem? Find(Guid skuId) => Items.FirstOrDefault(i => i.SkuId == skuId);

    /// <summary>Add to an existing line or create one. Quantity is capped by the caller (stock, per-line limit).</summary>
    public CartItem Add(Guid skuId, int quantity, long priceAtAdd, int maxLines, DateTimeOffset now)
    {
        if (quantity < 1) throw new BusinessRuleException("Số lượng phải từ 1 trở lên.");
        var line = Find(skuId);
        if (line is null)
        {
            if (Items.Count >= maxLines) throw new BusinessRuleException($"Giỏ hàng tối đa {maxLines} sản phẩm. Vui lòng xoá bớt trước khi thêm.");
            line = new CartItem(Id, skuId, quantity, priceAtAdd, now);
            Items.Add(line);
        }
        else
        {
            line.SetQuantity(line.Quantity + quantity, now);
            line.SetSelected(true);
        }
        UpdatedAt = now;
        return line;
    }

    public void Remove(IEnumerable<Guid> skuIds, DateTimeOffset now)
    {
        var set = skuIds.ToHashSet();
        Items.RemoveAll(i => set.Contains(i.SkuId));
        UpdatedAt = now;
    }

    /// <summary>Swap a line to another SKU of the same product (change variant in the cart).</summary>
    public void ChangeSku(Guid fromSkuId, Guid toSkuId, long priceAtAdd, DateTimeOffset now)
    {
        var line = Find(fromSkuId) ?? throw new BusinessRuleException("Sản phẩm không còn trong giỏ.");
        if (fromSkuId == toSkuId) return;
        var existing = Find(toSkuId);
        if (existing is not null)
        {
            existing.SetQuantity(existing.Quantity + line.Quantity, now);
            Items.Remove(line);
        }
        else
        {
            Items.Remove(line);
            Items.Add(new CartItem(Id, toSkuId, line.Quantity, priceAtAdd, now) { IsSelected = line.IsSelected });
        }
        UpdatedAt = now;
    }

    /// <summary>Guest lines move into this cart: same SKU → quantities add up (capped later against stock).</summary>
    public void MergeFrom(Cart guest, int maxLines, DateTimeOffset now)
    {
        foreach (var g in guest.Items)
        {
            var line = Find(g.SkuId);
            if (line is not null) line.SetQuantity(line.Quantity + g.Quantity, now);
            else if (Items.Count < maxLines) Items.Add(new CartItem(Id, g.SkuId, g.Quantity, g.PriceAtAdd, now) { IsSelected = g.IsSelected });
        }
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

public class CartItem : Entity
{
    public const int MaxQuantity = 999;

    private CartItem() { }

    internal CartItem(Guid cartId, Guid skuId, int quantity, long priceAtAdd, DateTimeOffset now)
    {
        CartId = cartId;
        SkuId = skuId;
        Quantity = Math.Clamp(quantity, 1, MaxQuantity);
        PriceAtAdd = priceAtAdd;
        IsSelected = true;
        AddedAt = now;
        UpdatedAt = now;
    }

    public Guid CartId { get; private set; }
    public Guid SkuId { get; private set; }
    public int Quantity { get; private set; }
    // Price when the buyer added it — the cart shows the old price struck through when it changed
    public long PriceAtAdd { get; private set; }
    public bool IsSelected { get; internal set; }
    public DateTimeOffset AddedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetQuantity(int quantity, DateTimeOffset now)
    {
        if (quantity < 1) throw new BusinessRuleException("Số lượng phải từ 1 trở lên.");
        Quantity = Math.Min(quantity, MaxQuantity);
        UpdatedAt = now;
    }

    public void SetSelected(bool selected) => IsSelected = selected;

    /// <summary>The buyer saw the new price (after a price change warning).</summary>
    public void AcknowledgePrice(long price) => PriceAtAdd = price;
}
