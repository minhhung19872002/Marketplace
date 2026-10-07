using ShopHub.Application.Common;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Abstractions;

// ---------- shipping (spec V) ----------

/// <summary>A real carrier did not answer or refused the request (network, credentials, unknown address…).</summary>
public sealed class CarrierUnavailableException(string message) : ConflictException(message, "CARRIER_UNAVAILABLE");

/// <summary>A real payment gateway did not answer or refused to create the payment.</summary>
public sealed class GatewayUnavailableException(string message) : ConflictException(message, "GATEWAY_UNAVAILABLE");

/// <summary>A webhook as received: headers, query string and raw body (some providers sign the query, some the body).</summary>
public record InboundWebhook(IReadOnlyDictionary<string, string> Headers, IReadOnlyDictionary<string, string> Query, string Body)
{
    public static InboundWebhook FromBody(IReadOnlyDictionary<string, string> headers, string body) => new(headers, new Dictionary<string, string>(), body);

    public string? Header(string name) => Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    public string? QueryValue(string name) => Query.FirstOrDefault(q => string.Equals(q.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>Administrative division codes (Tổng cục Thống kê) of one end of a route.</summary>
public record RoutePoint(string ProvinceCode, string DistrictCode, string WardCode);

/// <summary>A party of a parcel: who hands it over / receives it.</summary>
public record CarrierAddress(string Name, string Phone, string Street, RoutePoint Point);

public record CarrierItem(string Name, int Quantity, int WeightG);

public record CarrierQuote(ShippingZone Zone, int ChargeableWeightG, RoutePoint From, RoutePoint To, long ParcelValue);

public record CarrierParcel(Guid OrderId, string OrderCode, string FromProvince, string ToProvince, int WeightG, long CodAmount,
    PickupMethod PickupMethod, string? PickupSlot, CarrierAddress? Sender = null, CarrierAddress? Receiver = null, long ParcelValue = 0,
    IReadOnlyList<CarrierItem>? Items = null, string? Note = null);

/// <summary>A verified status notification from a carrier.</summary>
public record CarrierEvent(string EventId, string TrackingNo, ShipmentStatus Status, string? Location, string Description, DateTimeOffset OccurredAt,
    string Raw);

/// <summary>
/// A shipping provider. SIMULATED reads the zone × weight table, issues tracking numbers and pushes status events
/// through the same webhook path a real carrier uses; GHN and GHTK call their APIs (sandbox or production).
/// </summary>
public interface ICarrier
{
    string Provider { get; }

    /// <summary>Fee for one parcel, or null when the carrier does not serve that route / weight.</summary>
    Task<long?> QuoteFeeAsync(Carrier carrier, CarrierQuote quote, CancellationToken ct);

    /// <summary>Book the pickup / drop-off and get the tracking number.</summary>
    Task<string> CreateShipmentAsync(Carrier carrier, CarrierParcel parcel, CancellationToken ct);

    Task CancelShipmentAsync(Carrier carrier, string trackingNo, CancellationToken ct);

    /// <summary>Check the carrier's signature / secret and parse its notification; null when not authentic.</summary>
    CarrierEvent? VerifyWebhook(InboundWebhook webhook);

    /// <summary>The carrier's own label (PDF), or null when ShopHub prints its own.</summary>
    Task<byte[]?> GetLabelAsync(Carrier carrier, string trackingNo, CancellationToken ct) => Task.FromResult<byte[]?>(null);

    /// <summary>The parcel's history as the carrier knows it (catching up on missed webhooks); empty when not supported.</summary>
    Task<IReadOnlyList<CarrierEvent>> TrackAsync(Carrier carrier, string trackingNo, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CarrierEvent>>([]);
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

// ReturnPath: the page the buyer comes back to (display only — never proof of payment); ClientIp: required by some gateways
public record GatewayPaymentRequest(Guid PaymentId, Guid CheckoutId, long Amount, string Description, DateTimeOffset ExpiresAt, string ReturnPath = "/",
    string? ClientIp = null, DateTimeOffset? CreatedAt = null, PaymentOption Option = PaymentOption.Default);

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

    // Shown at checkout / wallet top-up
    string DisplayName { get; }

    /// <summary>
    /// Ways of paying this gateway can be asked for (its default page always). Instalments / pay later appear only when
    /// the merchant contract's code for them is configured — ShopHub never builds credit itself (spec IV).
    /// </summary>
    IReadOnlyList<PaymentOption> Options => [PaymentOption.Default];

    Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct);

    /// <summary>Check the signature and parse the notification; null when it is not authentic.</summary>
    GatewayCallback? VerifyCallback(InboundWebhook webhook);

    /// <summary>Ask the gateway what happened to a payment (reconciling stale "initiated" payments).</summary>
    Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct);

    Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct);

    /// <summary>Body the gateway expects in reply to its notification.</summary>
    string Acknowledge(bool accepted);

    /// <summary>Reply body knowing how ShopHub handled it (WebhookResults: PAID, DUPLICATE, AMOUNT_MISMATCH…).</summary>
    string Acknowledge(bool accepted, string result) => Acknowledge(accepted);

    /// <summary>HTTP status the gateway expects in reply (MoMo: 204).</summary>
    int AcknowledgeStatus(bool accepted) => accepted ? 200 : 400;
}

public interface IPaymentGatewayRegistry
{
    IPaymentGateway For(PaymentMethod method);

    IPaymentGateway? ForProvider(string provider);

    bool Supports(PaymentMethod method);

    /// <summary>Every configured online gateway (real first, simulated last) — also the ones an admin switched off.</summary>
    IReadOnlyList<IPaymentGateway> Online { get; }

    /// <summary>
    /// Gateways new payments may use: configured and not switched off by an admin (PAYMENT.DISABLED_METHODS). Existing
    /// payments keep querying / refunding through <see cref="For"/> whatever the switch says.
    /// </summary>
    Task<IReadOnlyList<IPaymentGateway>> EnabledAsync(CancellationToken ct);
}
