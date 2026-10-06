using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Abstractions;

// ---------- shipping (spec V) ----------

public record CarrierParcel(Guid OrderId, string OrderCode, string FromProvince, string ToProvince, int WeightG, long CodAmount,
    PickupMethod PickupMethod, string? PickupSlot);

/// <summary>A verified status notification from a carrier.</summary>
public record CarrierEvent(string EventId, string TrackingNo, ShipmentStatus Status, string? Location, string Description, DateTimeOffset OccurredAt,
    string Raw);

/// <summary>
/// A shipping provider. SIMULATED reads the zone × weight table, issues tracking numbers and pushes status events
/// through the same webhook path a real carrier uses; GHN/GHTK sandboxes come in Phase 11.
/// </summary>
public interface ICarrier
{
    string Provider { get; }

    /// <summary>Fee for one parcel, or null when the carrier does not serve that zone / weight.</summary>
    Task<long?> QuoteFeeAsync(Carrier carrier, ShippingZone zone, int chargeableWeightG, CancellationToken ct);

    /// <summary>Book the pickup / drop-off and get the tracking number.</summary>
    Task<string> CreateShipmentAsync(Carrier carrier, CarrierParcel parcel, CancellationToken ct);

    Task CancelShipmentAsync(Carrier carrier, string trackingNo, CancellationToken ct);

    /// <summary>Check the carrier's signature and parse its notification; null when not authentic.</summary>
    CarrierEvent? VerifyWebhook(IReadOnlyDictionary<string, string> headers, string body);
}

// ---------- shipping documents (ShopHub.Reporting) ----------

public enum LabelSize
{
    A6,
    A5,
}

public record ShippingLabelItem(string Name, string? Variant, int Quantity);

public record ShippingLabel(
    string TrackingNo,
    string CarrierName,
    string OrderCode,
    string SenderName,
    string SenderPhone,
    string SenderAddress,
    string ReceiverName,
    string ReceiverPhone,
    string ReceiverAddress,
    long CodAmount,
    int WeightG,
    IReadOnlyList<ShippingLabelItem> Items,
    string? BuyerNote,
    DateTimeOffset CreatedAt);

public record PickingLine(string? SellerSku, string Name, string? Variant, int Quantity, IReadOnlyList<string> OrderCodes);

public record OrderExportRow(
    string Code,
    DateTimeOffset CreatedAt,
    string Status,
    string BuyerName,
    string ReceiverPhone,
    string Address,
    string Items,
    long Subtotal,
    long ShopDiscount,
    long ShippingFee,
    long GrandTotal,
    string PaymentMethod,
    string? TrackingNo,
    string? Carrier);

public interface IShippingDocuments
{
    /// <summary>One page per parcel: barcode of the tracking number, order code, sender, receiver, COD, item list.</summary>
    byte[] RenderLabels(IReadOnlyList<ShippingLabel> labels, LabelSize size);

    /// <summary>Picking list grouping the same SKU across orders.</summary>
    byte[] RenderPickingList(string shopName, IReadOnlyList<PickingLine> lines, DateTimeOffset printedAt);

    byte[] ExportOrders(IReadOnlyList<OrderExportRow> rows);
}

// ---------- payment (spec IV) ----------

public record GatewayPaymentRequest(Guid PaymentId, Guid CheckoutId, long Amount, string Description, DateTimeOffset ExpiresAt);

public record GatewayPaymentStart(string RedirectUrl);

/// <summary>A verified gateway notification.</summary>
public record GatewayCallback(string EventId, Guid PaymentId, string? ProviderTxnId, long Amount, bool Success, string? FailureReason, string Raw);

public enum GatewayTxnStatus
{
    Pending,
    Succeeded,
    Failed,
}

public record GatewayQueryResult(GatewayTxnStatus Status, string? ProviderTxnId, long Amount, string Raw);

public interface IPaymentGateway
{
    PaymentMethod Method { get; }

    // Name used for webhook routes and event de-duplication ("simulated", "vnpay", "momo"…)
    string Provider { get; }

    Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct);

    /// <summary>Check the signature and parse the body; null when the notification is not authentic.</summary>
    GatewayCallback? VerifyCallback(IReadOnlyDictionary<string, string> headers, string body);

    /// <summary>Ask the gateway what happened to a payment (reconciling stale "initiated" payments).</summary>
    Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct);

    Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct);

    /// <summary>Body the gateway expects in reply to its notification.</summary>
    string Acknowledge(bool accepted);
}

public interface IPaymentGatewayRegistry
{
    IPaymentGateway For(PaymentMethod method);

    IPaymentGateway? ForProvider(string provider);

    bool Supports(PaymentMethod method);
}
