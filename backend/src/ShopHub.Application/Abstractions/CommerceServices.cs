using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Abstractions;

// ---------- shipping (spec V) ----------

/// <summary>
/// A shipping provider. SIMULATED reads the zone × weight table; GHN/GHTK sandboxes come in Phase 11.
/// Shipment creation, labels and tracking are added with the order flow (Phase 6).
/// </summary>
public interface ICarrier
{
    string Provider { get; }

    /// <summary>Fee for one parcel, or null when the carrier does not serve that zone / weight.</summary>
    Task<long?> QuoteFeeAsync(Carrier carrier, ShippingZone zone, int chargeableWeightG, CancellationToken ct);
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
