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
    public string DisplayName => "Thẻ / Ví điện tử (cổng thanh toán giả lập)";

    // The fake page stands in for any of them (demo / tests); a real gateway offers only what its contract allows
    public IReadOnlyList<PaymentOption> Options => Enum.GetValues<PaymentOption>();

    public Task<GatewayPaymentStart> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new GatewayPaymentStart($"/cong-thanh-toan/{request.PaymentId}"));

    public record CallbackBody(string EventId, Guid PaymentId, string TxnId, long Amount, string Status, string? Reason);

    public string Sign(string body) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public GatewayCallback? VerifyCallback(InboundWebhook webhook)
    {
        var body = webhook.Body;
        var signature = webhook.Header(SignatureHeader);
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
        return txn is not null && txn.Refund(amount, clock.UtcNow);
    }

    public string Acknowledge(bool accepted) => accepted ? """{"code":"00","message":"Confirm Success"}""" : """{"code":"97","message":"Invalid Checksum"}""";
}

// ReturnPath: where the buyer lands after the gateway (the order result page, or the wallet for a top-up)
public record SimulatedPaymentView(Guid PaymentId, long Amount, string Description, DateTimeOffset ExpiresAt, PaymentStatus Status, Guid CheckoutId,
    string ReturnPath, string? Way = null);

/// <summary>What the fake gateway page does: show the amount, record the outcome on the gateway side and notify the shop.</summary>
public sealed class SimulatedGatewayDesk(ShopHubDbContext db, SimulatedGateway gateway, PaymentWebhookIntake intake, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SimulatedPaymentView> ViewAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.Method == PaymentMethod.Simulated, ct)
                      ?? throw new NotFoundException("Không tìm thấy giao dịch.");
        if (payment.Purpose == PaymentPurpose.WalletTopup)
            return new SimulatedPaymentView(payment.Id, payment.Amount, "Nạp tiền vào Ví ShopHub", payment.ExpiresAt, payment.Status, payment.CheckoutId,
                $"/tai-khoan/vi?topup={payment.CheckoutId}");
        var codes = await db.Orders.AsNoTracking().Where(o => o.CheckoutId == payment.CheckoutId).OrderBy(o => o.Code).Select(o => o.Code).ToListAsync(ct);
        return new SimulatedPaymentView(payment.Id, payment.Amount, $"Thanh toán đơn hàng {string.Join(", ", codes)}", payment.ExpiresAt, payment.Status,
            payment.CheckoutId, $"/thanh-toan/ket-qua/{payment.CheckoutId}", payment.Option == PaymentOption.Default ? null : payment.Option.Label());
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
            InboundWebhook.FromBody(new Dictionary<string, string> { [SimulatedGateway.SignatureHeader] = gateway.Sign(body) }, body), ct);
        return await ViewAsync(paymentId, ct);
    }
}

public sealed class PaymentGatewayRegistry(IEnumerable<IPaymentGateway> gateways, ISystemParameters parameters) : IPaymentGatewayRegistry
{
    public IPaymentGateway For(PaymentMethod method) =>
        gateways.FirstOrDefault(g => g.Method == method) ?? throw new ConflictException("Phương thức thanh toán này chưa được hỗ trợ.", "NO_GATEWAY");

    public IPaymentGateway? ForProvider(string provider) =>
        gateways.FirstOrDefault(g => string.Equals(g.Provider, provider, StringComparison.OrdinalIgnoreCase));

    public bool Supports(PaymentMethod method) => gateways.Any(g => g.Method == method);

    // Real gateways first, the simulated one last
    public IReadOnlyList<IPaymentGateway> Online => gateways.OrderBy(g => g.Method == PaymentMethod.Simulated).ThenBy(g => g.Method).ToList();

    public async Task<IReadOnlyList<IPaymentGateway>> EnabledAsync(CancellationToken ct)
    {
        var raw = await parameters.GetStringAsync(Application.SystemConfig.ParameterKeys.PaymentDisabledMethods, ct);
        List<string> off;
        try
        {
            off = JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            off = [];
        }
        return Online.Where(g => !off.Contains(g.Method.ToString(), StringComparer.OrdinalIgnoreCase)).ToList();
    }
}
