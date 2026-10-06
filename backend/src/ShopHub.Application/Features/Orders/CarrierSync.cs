using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Logistics;

namespace ShopHub.Application.Features.Orders;

/// <summary>
/// Catches up on webhooks a real carrier failed to deliver (job <c>logistics.carrier-sync</c>): open parcels of GHN /
/// GHTK with no news for LOGISTICS.SYNC_STALE_MINUTES are asked to the carrier and every event goes through the same
/// processor as a webhook — events already applied are ignored by their id.
/// </summary>
public sealed class CarrierSyncService(
    IApplicationDbContext db,
    IEnumerable<ICarrier> carriers,
    ShipmentEventProcessor processor,
    ISystemParameters parameters,
    IClock clock,
    ILogger<CarrierSyncService> logger)
{
    private static readonly ShipmentStatus[] Closed = [ShipmentStatus.Delivered, ShipmentStatus.Returned, ShipmentStatus.Cancelled];

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var real = carriers.Where(c => c.Provider != "SIMULATED").ToDictionary(c => c.Provider, StringComparer.OrdinalIgnoreCase);
        if (real.Count == 0) return 0;
        var stale = clock.UtcNow.AddMinutes(-await parameters.GetIntAsync(ParameterKeys.LogisticsSyncStaleMinutes, ct));
        var providers = real.Keys.ToList();
        var channels = await db.Carriers.AsNoTracking().Where(c => providers.Contains(c.Provider)).ToListAsync(ct);
        var codes = channels.Select(c => c.Code).ToList();
        var open = await db.Shipments.AsNoTracking()
            .Where(s => codes.Contains(s.CarrierCode) && !Closed.Contains(s.Status) && s.LastEventAt < stale)
            .OrderBy(s => s.LastEventAt).Select(s => new { s.TrackingNo, s.CarrierCode }).Take(200).ToListAsync(ct);

        var applied = 0;
        foreach (var s in open)
        {
            var channel = channels.First(c => c.Code == s.CarrierCode);
            try
            {
                foreach (var e in await real[channel.Provider].TrackAsync(channel, s.TrackingNo, ct))
                {
                    if (await processor.ApplyAsync(e, ct) == "APPLIED") applied++;
                    db.ClearTracking();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ClearTracking();
                logger.LogWarning(ex, "Carrier sync of {TrackingNo} failed", s.TrackingNo);
            }
        }
        if (applied > 0) logger.LogInformation("Carrier sync: {Count} missed event(s) applied", applied);
        return applied;
    }
}
