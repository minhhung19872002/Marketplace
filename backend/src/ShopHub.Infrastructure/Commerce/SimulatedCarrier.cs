using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Logistics;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Commerce;

/// <summary>
/// Simulated shipping provider: fees from the zone × weight table, tracking numbers "SIM…", and status notifications
/// signed with HMAC-SHA256 — the same webhook contract a real carrier (GHN, GHTK) gets in Phase 11.
/// </summary>
public sealed class SimulatedCarrier(ShopHubDbContext db, ShopHubSettings settings) : ICarrier
{
    public const string ProviderName = "SIMULATED";
    public const string SignatureHeader = "X-Sim-Carrier-Signature";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private byte[] Key => HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(settings.JwtSecret), 32,
        info: Encoding.UTF8.GetBytes("shophub-simulated-carrier"));

    public string Provider => ProviderName;

    public async Task<long?> QuoteFeeAsync(Carrier carrier, ShippingZone zone, int chargeableWeightG, CancellationToken ct)
    {
        var rates = await db.ShippingRates.AsNoTracking().Where(r => r.CarrierId == carrier.Id && r.Zone == zone)
            .OrderBy(r => r.WeightFromG).ToListAsync(ct);
        var rate = rates.FirstOrDefault(r => r.Covers(chargeableWeightG));
        return rate?.FeeFor(chargeableWeightG);
    }

    public Task<string> CreateShipmentAsync(Carrier carrier, CarrierParcel parcel, CancellationToken ct) =>
        Task.FromResult($"SIM{RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000)}{RandomNumberGenerator.GetInt32(10, 100)}VN");

    public Task CancelShipmentAsync(Carrier carrier, string trackingNo, CancellationToken ct) => Task.CompletedTask;

    public record WebhookBody(string EventId, string TrackingNo, ShipmentStatus Status, string? Location, string Description, DateTimeOffset OccurredAt);

    public string Sign(string body) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public string Serialize(WebhookBody body) => JsonSerializer.Serialize(body, Json);

    public CarrierEvent? VerifyWebhook(IReadOnlyDictionary<string, string> headers, string body)
    {
        var signature = headers.FirstOrDefault(h => string.Equals(h.Key, SignatureHeader, StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrEmpty(signature)) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(body)), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant())))
            return null;
        try
        {
            var b = JsonSerializer.Deserialize<WebhookBody>(body, Json);
            return b is null || string.IsNullOrEmpty(b.EventId) ? null
                : new CarrierEvent(b.EventId, b.TrackingNo, b.Status, b.Location, b.Description, b.OccurredAt, body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The simulated carrier's "operations": every run moves each open SIMULATED shipment one step once its last event
/// is older than LOGISTICS.SIM_STEP_SECONDS — picked → in transit → out for delivery → delivered (or failed and
/// returned, for LOGISTICS.SIM_FAIL_PERCENT of parcels) — and notifies ShopHub through the signed webhook path.
/// </summary>
public sealed class CarrierSimulator(
    ShopHubDbContext db,
    SimulatedCarrier carrier,
    CarrierWebhookIntake intake,
    ISystemParameters parameters,
    IClock clock,
    ILogger<CarrierSimulator> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var step = await parameters.GetIntAsync(ParameterKeys.LogisticsSimStepSeconds, ct);
        var failPercent = await parameters.GetIntAsync(ParameterKeys.LogisticsSimFailPercent, ct);
        var due = now.AddSeconds(-step);
        var simulated = await db.Carriers.AsNoTracking().Where(c => c.Provider == SimulatedCarrier.ProviderName).Select(c => c.Code).ToListAsync(ct);
        var open = new[] { ShipmentStatus.Created, ShipmentStatus.Picked, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery, ShipmentStatus.Failed,
            ShipmentStatus.Returning };
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => simulated.Contains(s.CarrierCode) && open.Contains(s.Status) && s.LastEventAt <= due)
            .OrderBy(s => s.LastEventAt).ThenBy(s => s.Id).Take(500)
            .Select(s => new { s.TrackingNo, s.Status, s.Events.Count }).ToListAsync(ct);

        var moved = 0;
        foreach (var s in shipments)
        {
            // Stable per parcel (string.GetHashCode is randomised per process)
            var bucket = s.TrackingNo.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & int.MaxValue;
            var fails = bucket % 100 < failPercent;
            (ShipmentStatus Next, string Text, string Where)? next = s.Status switch
            {
                ShipmentStatus.Created => (ShipmentStatus.Picked, "Đã lấy hàng tại kho người gửi", "Bưu cục lấy hàng"),
                ShipmentStatus.Picked => (ShipmentStatus.InTransit, "Đang trung chuyển tới kho phân loại", "Kho phân loại"),
                ShipmentStatus.InTransit => (ShipmentStatus.OutForDelivery, "Đang giao hàng tới người nhận", "Bưu cục giao hàng"),
                ShipmentStatus.OutForDelivery when fails => (ShipmentStatus.Failed, "Giao không thành công: không liên lạc được người nhận", "Bưu cục giao hàng"),
                ShipmentStatus.OutForDelivery => (ShipmentStatus.Delivered, "Giao hàng thành công", "Địa chỉ người nhận"),
                ShipmentStatus.Failed => (ShipmentStatus.Returning, "Đang hoàn hàng về người gửi", "Kho phân loại"),
                ShipmentStatus.Returning => (ShipmentStatus.Returned, "Đã hoàn hàng về người gửi", "Kho người gửi"),
                _ => null,
            };
            if (next is null) continue;
            var body = carrier.Serialize(new SimulatedCarrier.WebhookBody($"{s.TrackingNo}-{s.Count}-{next.Value.Next}", s.TrackingNo, next.Value.Next,
                next.Value.Where, next.Value.Text, clock.UtcNow));
            try
            {
                var (_, result) = await intake.HandleAsync(SimulatedCarrier.ProviderName,
                    new Dictionary<string, string> { [SimulatedCarrier.SignatureHeader] = carrier.Sign(body) }, body, ct);
                if (result == "APPLIED") moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Simulated carrier could not push {Status} for {TrackingNo}", next.Value.Next, s.TrackingNo);
            }
            db.ChangeTracker.Clear();
        }
        return moved;
    }
}
