using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Payments;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Commerce;

/// <summary>Carrier backed by the zone × weight table (logistics.shipping_rates).</summary>
public sealed class SimulatedCarrier(ShopHubDbContext db) : ICarrier
{
    public const string ProviderName = "SIMULATED";

    public string Provider => ProviderName;

    public async Task<long?> QuoteFeeAsync(Carrier carrier, ShippingZone zone, int chargeableWeightG, CancellationToken ct)
    {
        var rates = await db.ShippingRates.AsNoTracking().Where(r => r.CarrierId == carrier.Id && r.Zone == zone)
            .OrderBy(r => r.WeightFromG).ToListAsync(ct);
        var rate = rates.FirstOrDefault(r => r.Covers(chargeableWeightG));
        return rate?.FeeFor(chargeableWeightG);
    }
}

/// <summary>
/// The system's own fake payment gateway (spec IV): its page lets the tester press "Thành công" / "Thất bại" / "Bỏ đi",
/// and it notifies the shop with an HMAC-signed callback exactly like a real gateway would. Only enabled when
/// SH_PAYMENT_SIMULATED=true (never in production).
/// </summary>
public sealed class SimulatedGateway(ShopHubDbContext db, ShopHubSettings settings, IClock clock) : IPaymentGateway
{
    public const string ProviderName = "simulated";
    public const string SignatureHeader = "X-Sim-Signature";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private byte[] Key => HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(settings.JwtSecret), 32,
        info: Encoding.UTF8.GetBytes("shophub-simulated-gateway"));

    public PaymentMethod Method => PaymentMethod.Simulated;
    public string Provider => ProviderName;

    public Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new GatewayPaymentStart($"/cong-thanh-toan/{request.PaymentId}"));

    public record CallbackBody(string EventId, Guid PaymentId, string TxnId, long Amount, string Status, string? Reason);

    public string Sign(string body) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public GatewayCallback? VerifyCallback(IReadOnlyDictionary<string, string> headers, string body)
    {
        var signature = headers.FirstOrDefault(h => string.Equals(h.Key, SignatureHeader, StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrEmpty(signature)) return null;
        var expected = Encoding.ASCII.GetBytes(Sign(body));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()))) return null;
        CallbackBody? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CallbackBody>(body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (parsed is null || string.IsNullOrEmpty(parsed.EventId)) return null;
        return new GatewayCallback(parsed.EventId, parsed.PaymentId, parsed.TxnId, parsed.Amount, parsed.Status == "SUCCESS", parsed.Reason, body);
    }

    public async Task<GatewayQueryResult> QueryAsync(Payment payment, CancellationToken ct)
    {
        var txn = await db.Set<SimulatedPayment>().AsNoTracking().Where(t => t.PaymentId == payment.Id)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        return txn is null
            ? new GatewayQueryResult(GatewayTxnStatus.Pending, null, payment.Amount, "{}")
            : new GatewayQueryResult(txn.Outcome == SimulatedPaymentOutcome.Failed ? GatewayTxnStatus.Failed : GatewayTxnStatus.Succeeded,
                txn.TxnId, txn.Amount, JsonSerializer.Serialize(new { txn.TxnId, txn.Amount, Outcome = txn.Outcome.ToString() }, Json));
    }

    public async Task<bool> RefundAsync(Payment payment, long amount, string reason, CancellationToken ct)
    {
        var txn = await db.Set<SimulatedPayment>().FirstOrDefaultAsync(t => t.PaymentId == payment.Id && t.Outcome == SimulatedPaymentOutcome.Succeeded, ct);
        if (txn is null) return false;
        txn.Refund(clock.UtcNow);
        return true;
    }

    public string Acknowledge(bool accepted) => accepted ? """{"code":"00","message":"Confirm Success"}""" : """{"code":"97","message":"Invalid Checksum"}""";
}

public record SimulatedPaymentView(Guid PaymentId, long Amount, string Description, DateTimeOffset ExpiresAt, PaymentStatus Status, Guid CheckoutId);

/// <summary>What the fake gateway page does: show the amount, record the outcome on the gateway side and notify the shop.</summary>
public sealed class SimulatedGatewayDesk(ShopHubDbContext db, SimulatedGateway gateway, PaymentWebhookIntake intake, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SimulatedPaymentView> ViewAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.Method == PaymentMethod.Simulated, ct)
                      ?? throw new NotFoundException("Không tìm thấy giao dịch.");
        var codes = await db.Orders.AsNoTracking().Where(o => o.CheckoutId == payment.CheckoutId).OrderBy(o => o.Code).Select(o => o.Code).ToListAsync(ct);
        return new SimulatedPaymentView(payment.Id, payment.Amount, $"Thanh toán đơn hàng {string.Join(", ", codes)}", payment.ExpiresAt, payment.Status,
            payment.CheckoutId);
    }

    /// <summary>"Thành công" / "Thất bại": record on the gateway side, then send the signed callback (like the real thing).</summary>
    public async Task<SimulatedPaymentView> CompleteAsync(Guid paymentId, bool success, CancellationToken ct)
    {
        var view = await ViewAsync(paymentId, ct);
        if (view.Status != PaymentStatus.Initiated) throw new ConflictException("Giao dịch này đã được xử lý.", "PAYMENT_DONE");
        if (view.ExpiresAt <= clock.UtcNow) throw new ConflictException("Giao dịch đã hết hạn.", "PAYMENT_EXPIRED");

        var txnId = $"SIM{clock.UtcNow:yyMMddHHmmss}{RandomNumberGenerator.GetHexString(6)}";
        db.Set<SimulatedPayment>().Add(new SimulatedPayment(paymentId, txnId, view.Amount,
            success ? SimulatedPaymentOutcome.Succeeded : SimulatedPaymentOutcome.Failed, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var body = JsonSerializer.Serialize(new SimulatedGateway.CallbackBody(Guid.NewGuid().ToString("N"), paymentId, txnId, view.Amount,
            success ? "SUCCESS" : "FAILED", success ? null : "Khách hàng huỷ hoặc thẻ bị từ chối (giả lập)."), Json);
        await intake.HandleAsync(SimulatedGateway.ProviderName,
            new Dictionary<string, string> { [SimulatedGateway.SignatureHeader] = gateway.Sign(body) }, body, ct);
        return await ViewAsync(paymentId, ct);
    }
}

public sealed class PaymentGatewayRegistry(IEnumerable<IPaymentGateway> gateways) : IPaymentGatewayRegistry
{
    public IPaymentGateway For(PaymentMethod method) =>
        gateways.FirstOrDefault(g => g.Method == method) ?? throw new ConflictException("Phương thức thanh toán này chưa được hỗ trợ.", "NO_GATEWAY");

    public IPaymentGateway? ForProvider(string provider) =>
        gateways.FirstOrDefault(g => string.Equals(g.Provider, provider, StringComparison.OrdinalIgnoreCase));

    public bool Supports(PaymentMethod method) => gateways.Any(g => g.Method == method);
}
