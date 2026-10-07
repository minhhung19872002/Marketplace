using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

public enum PaymentMethod
{
    Cod,
    // SimulatedGateway: the system's own fake payment page that calls the webhook like a real gateway
    Simulated,
    // Ví ShopHub: paid from the buyer's wallet balance at once (6-digit PIN)
    Wallet,
    // Real gateways (sandbox or production), switched on by their keys in .env
    VnPay,
    MoMo,
    ZaloPay,
}

/// <summary>
/// How the buyer pays at the gateway (spec IV): its default page, or one of the ways the gateway offers — cards, QR,
/// instalments, pay later. ShopHub only asks the gateway for it; the gateway (and its lending partner) runs the credit.
/// </summary>
public enum PaymentOption
{
    Default,
    DomesticCard,       // thẻ ATM / tài khoản ngân hàng nội địa
    InternationalCard,  // thẻ quốc tế Visa / Master / JCB
    QrCode,             // quét mã QR ngân hàng
    Installment,        // trả góp qua thẻ tín dụng / công ty tài chính của cổng
    PayLater,           // mua trước trả sau của cổng
}

public static class PaymentMethods
{
    /// <summary>Paid through a payment gateway (redirect + IPN), as opposed to COD or Ví ShopHub.</summary>
    public static bool IsOnline(this PaymentMethod method) =>
        method is PaymentMethod.Simulated or PaymentMethod.VnPay or PaymentMethod.MoMo or PaymentMethod.ZaloPay;

    public static string Label(this PaymentOption option) => option switch
    {
        PaymentOption.DomesticCard => "Thẻ ATM / tài khoản ngân hàng",
        PaymentOption.InternationalCard => "Thẻ quốc tế (Visa, Mastercard, JCB)",
        PaymentOption.QrCode => "Quét mã QR",
        PaymentOption.Installment => "Trả góp",
        PaymentOption.PayLater => "Mua trước trả sau",
        _ => "Thanh toán qua cổng",
    };
}

public enum CheckoutStatus
{
    // Online payment not received yet (stock and vouchers are held until PaymentExpiresAt)
    AwaitingPayment,
    // COD placed, or online payment received
    Placed,
    // Payment window passed / cancelled before payment: everything held was released
    Expired,
}

/// <summary>One press of "Đặt hàng": prices are fixed here and N orders (one per shop) hang off it.</summary>
public class CheckoutSession : Entity
{
    private CheckoutSession() { }

    public CheckoutSession(Guid userId, string idempotencyKey, string addressSnapshot, PaymentMethod method, DateTimeOffset now)
    {
        UserId = userId;
        IdempotencyKey = idempotencyKey;
        AddressSnapshot = addressSnapshot;
        PaymentMethod = method;
        CreatedAt = now;
        Status = method == PaymentMethod.Cod ? CheckoutStatus.Placed : CheckoutStatus.AwaitingPayment;
    }

    public Guid UserId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    // jsonb: receiver, phone, full address text and codes at the time of ordering
    public string AddressSnapshot { get; private set; } = "{}";
    public PaymentMethod PaymentMethod { get; private set; }
    // The way chosen at the gateway (cards, instalments…); "Thanh toán lại" asks for the same
    public PaymentOption PaymentOption { get; private set; }

    public void ChooseOption(PaymentOption option) => PaymentOption = option;
    public long Subtotal { get; private set; }
    public long ShippingFee { get; private set; }
    public long ShippingDiscount { get; private set; }
    public long DiscountTotal { get; private set; }
    public long CoinUsed { get; private set; }
    public long GrandTotal { get; private set; }
    public Guid? PlatformVoucherId { get; private set; }
    public Guid? FreeshipVoucherId { get; private set; }
    public CheckoutStatus Status { get; private set; }
    public DateTimeOffset? PaymentExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }

    public void SetTotals(long subtotal, long shippingFee, long shippingDiscount, long discountTotal, long coinUsed, long grandTotal,
        Guid? platformVoucherId, Guid? freeshipVoucherId, DateTimeOffset? paymentExpiresAt)
    {
        Subtotal = subtotal;
        ShippingFee = shippingFee;
        ShippingDiscount = shippingDiscount;
        DiscountTotal = discountTotal;
        CoinUsed = coinUsed;
        GrandTotal = grandTotal;
        PlatformVoucherId = platformVoucherId;
        FreeshipVoucherId = freeshipVoucherId;
        PaymentExpiresAt = paymentExpiresAt;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        if (Status != CheckoutStatus.AwaitingPayment) throw new BusinessRuleException("Đơn hàng không ở trạng thái chờ thanh toán.");
        Status = CheckoutStatus.Placed;
        PaidAt = now;
    }

    public void MarkExpired()
    {
        if (Status != CheckoutStatus.AwaitingPayment) throw new BusinessRuleException("Đơn hàng không ở trạng thái chờ thanh toán.");
        Status = CheckoutStatus.Expired;
    }
}

public enum OrderStatus
{
    PendingPayment,       // Chờ thanh toán
    PendingConfirmation,  // Chờ xác nhận
    ReadyToShip,          // Chờ lấy hàng
    Shipping,             // Đang giao
    Delivered,            // Đã giao
    Completed,            // Hoàn thành
    Cancelled,            // Đã huỷ
    DeliveryFailed,       // Giao thất bại
    Returning,            // Đang hoàn về
    Returned,             // Đã hoàn về
}

public enum OrderPaymentStatus
{
    Unpaid,
    Paid,
    Refunded,
}

public enum OrderActor
{
    Buyer,
    Seller,
    Admin,
    System,
    Carrier,
    Gateway,
}

public class Order : Entity
{
    private Order() { }

    // The shop's service programmes when the order was placed: they decide the service fee at settlement
    public bool FreeshipXtra { get; private set; }
    public bool VoucherXtra { get; private set; }

    public void MarkXtra(bool freeshipXtra, bool voucherXtra)
    {
        FreeshipXtra = freeshipXtra;
        VoucherXtra = voucherXtra;
    }

    public Order(Guid checkoutId, Guid buyerId, Guid shopId, string code, PaymentMethod paymentMethod, string carrierCode,
        string? buyerNote, DateTimeOffset now)
    {
        CheckoutId = checkoutId;
        BuyerId = buyerId;
        ShopId = shopId;
        Code = code;
        PaymentMethod = paymentMethod;
        CarrierCode = carrierCode;
        BuyerNote = buyerNote;
        CreatedAt = now;
        // COD skips "Chờ thanh toán"; the state machine records the initial state in the history
        Status = paymentMethod == PaymentMethod.Cod ? OrderStatus.PendingConfirmation : OrderStatus.PendingPayment;
        PaymentStatus = OrderPaymentStatus.Unpaid;
    }

    public Guid CheckoutId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ShopId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    // Only OrderStateMachine changes this (source-scan rule)
    public OrderStatus Status { get; internal set; }
    public OrderPaymentStatus PaymentStatus { get; internal set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public string CarrierCode { get; private set; } = string.Empty;
    public long Subtotal { get; private set; }
    public long ShopDiscount { get; private set; }
    public long PlatformDiscount { get; private set; }
    public long ShippingFee { get; private set; }
    public long ShippingDiscount { get; private set; }
    public long CoinUsed { get; private set; }
    public long GrandTotal { get; private set; }
    public Guid? ShopVoucherId { get; private set; }
    public int ExpectedDeliveryDays { get; private set; }
    public string? BuyerNote { get; private set; }
    // Internal note of the shop staff (never shown to the buyer)
    public string? SellerNote { get; private set; }
    public string? CancelReason { get; internal set; }
    public OrderActor? CancelledBy { get; internal set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; internal set; }
    public DateTimeOffset? ConfirmedAt { get; internal set; }
    public DateTimeOffset? ShippedAt { get; internal set; }
    public DateTimeOffset? DeliveredAt { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public DateTimeOffset? CancelledAt { get; internal set; }
    public DateTimeOffset? AutoCompleteAt { get; internal set; }
    public uint Version { get; private set; }

    public List<OrderItem> Items { get; private set; } = [];
    public List<OrderStatusHistory> History { get; private set; } = [];
    // One parcel per ship-from warehouse; empty on orders placed before đa kho (one parcel from the default pickup)
    public List<OrderPackage> Packages { get; private set; } = [];

    /// <summary>When the order completes by itself if the buyer never presses "Đã nhận được hàng".</summary>
    public void ScheduleAutoComplete(DateTimeOffset at)
    {
        if (Status != OrderStatus.Delivered) throw new BusinessRuleException("Chỉ đơn đã giao mới hẹn tự hoàn thành.");
        AutoCompleteAt = at;
    }

    public void SetSellerNote(string? note) => SellerNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    /// <summary>The money the buyer paid online for this order went back (cancelled after paying, returned parcel).</summary>
    public void MarkRefunded()
    {
        if (PaymentStatus != OrderPaymentStatus.Paid) throw new BusinessRuleException("Đơn chưa thanh toán thì không có gì để hoàn.");
        PaymentStatus = OrderPaymentStatus.Refunded;
    }

    public void SetTotals(long subtotal, long shopDiscount, long platformDiscount, long shippingFee, long shippingDiscount, long coinUsed,
        Guid? shopVoucherId, int expectedDeliveryDays)
    {
        Subtotal = subtotal;
        ShopDiscount = shopDiscount;
        PlatformDiscount = platformDiscount;
        ShippingFee = shippingFee;
        ShippingDiscount = shippingDiscount;
        CoinUsed = coinUsed;
        GrandTotal = subtotal - shopDiscount - platformDiscount + shippingFee - shippingDiscount - coinUsed;
        if (GrandTotal < 0) throw new BusinessRuleException("Tổng thanh toán không được âm.");
        ShopVoucherId = shopVoucherId;
        ExpectedDeliveryDays = expectedDeliveryDays;
    }
}

/// <summary>
/// A parcel of the order (spec 3.5: one per ship-from warehouse when the shop runs several): its warehouse, chargeable
/// weight, the carrier fee quoted for it, and its share of the order's free-shipping discount (largest remainder).
/// </summary>
public class OrderPackage : Entity
{
    private OrderPackage() { }

    public OrderPackage(Guid orderId, int no, Guid warehouseId, int weightG, long shippingFee, long shippingDiscount)
    {
        if (no < 1 || weightG < 0 || shippingFee < 0 || shippingDiscount < 0 || shippingDiscount > shippingFee)
            throw new BusinessRuleException("Thông tin kiện hàng không hợp lệ.");
        OrderId = orderId;
        No = no;
        WarehouseId = warehouseId;
        WeightG = weightG;
        ShippingFee = shippingFee;
        ShippingDiscount = shippingDiscount;
    }

    public Guid OrderId { get; private set; }
    public int No { get; private set; }
    public Guid WarehouseId { get; private set; }
    public int WeightG { get; private set; }
    public long ShippingFee { get; private set; }
    public long ShippingDiscount { get; private set; }
}

/// <summary>Snapshot of the line at ordering time — later price / name edits never change an old order.</summary>
public class OrderItem : Entity
{
    private OrderItem() { }

    public OrderItem(Guid orderId, Guid skuId, Guid productId, string nameSnapshot, string? variantSnapshot, string? imageSnapshot,
        long unitPrice, long originalPrice, int quantity)
    {
        OrderId = orderId;
        SkuId = skuId;
        ProductId = productId;
        NameSnapshot = nameSnapshot;
        VariantSnapshot = variantSnapshot;
        ImageSnapshot = imageSnapshot;
        UnitPrice = unitPrice;
        OriginalPrice = originalPrice;
        Quantity = quantity;
        LineTotal = checked(unitPrice * quantity);
    }

    public Guid OrderId { get; private set; }
    public Guid SkuId { get; private set; }
    public Guid ProductId { get; private set; }
    public string NameSnapshot { get; private set; } = string.Empty;
    public string? VariantSnapshot { get; private set; }
    public string? ImageSnapshot { get; private set; }
    public long UnitPrice { get; private set; }
    public long OriginalPrice { get; private set; }
    public int Quantity { get; private set; }
    public long LineTotal { get; private set; }

    // Price programme the unit price came from (shop discount / flash sale) and its row — flash quota goes back on cancel
    public Promo.PriceProgramKind? PriceSource { get; private set; }
    public Guid? PriceRefId { get; private set; }
    // A free gift line (quà tặng kèm) of a gift programme
    public Guid? GiftPromotionId { get; private set; }

    public List<OrderItemDiscount> Discounts { get; private set; } = [];
    public int PackageNo { get; private set; } = 1;

    public void InPackage(int packageNo)
    {
        if (packageNo < 1) throw new BusinessRuleException("Số kiện không hợp lệ.");
        PackageNo = packageNo;
    }

    public void FromPriceProgram(Promo.PriceProgramKind kind, Guid refId)
    {
        PriceSource = kind;
        PriceRefId = refId;
    }

    public void AsGiftOf(Guid promotionId) => GiftPromotionId = promotionId;

    /// <summary>What the buyer actually paid for this line after every allocated discount.</summary>
    public long PaidAmount => LineTotal - Discounts.Sum(d => d.Amount);
}

public enum DiscountSource
{
    Shop,      // shop voucher — the shop bears it
    Platform,  // platform voucher — the platform bears it
    Combo,
    Flash,
    Coin,
}

/// <summary>A discount allocated down to one order line (largest remainder) — needed for partial refunds and settlement.</summary>
public class OrderItemDiscount : Entity
{
    private OrderItemDiscount() { }

    public OrderItemDiscount(Guid orderItemId, DiscountSource source, Guid? refId, long amount)
    {
        if (amount < 0) throw new BusinessRuleException("Số tiền giảm không được âm.");
        OrderItemId = orderItemId;
        Source = source;
        RefId = refId;
        Amount = amount;
    }

    public Guid OrderItemId { get; private set; }
    public DiscountSource Source { get; private set; }
    public Guid? RefId { get; private set; }
    public long Amount { get; private set; }
}

public class OrderStatusHistory : Entity
{
    private OrderStatusHistory() { }

    internal OrderStatusHistory(Guid orderId, OrderStatus? from, OrderStatus to, OrderActor actor, Guid? actorId, string? reason, DateTimeOffset at)
    {
        OrderId = orderId;
        FromStatus = from;
        ToStatus = to;
        ActorType = actor;
        ActorId = actorId;
        Reason = reason;
        OccurredAt = at;
    }

    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public OrderActor ActorType { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
