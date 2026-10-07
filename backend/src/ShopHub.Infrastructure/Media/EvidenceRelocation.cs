using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Media;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Media;

/// <summary>
/// One-time move (L075) of return / dispute evidence uploaded before the private bucket existed: each object is copied
/// from the public <c>sh-reviews</c> bucket to the private <c>sh-returns</c>, the public copy deleted (its old link stops
/// working) and the stored public link cleared. Safe to run on every start: only assets still in the old bucket move.
/// </summary>
public sealed class EvidenceRelocation(ShopHubDbContext db, IObjectStorage storage, ILogger<EvidenceRelocation> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var assets = await db.MediaAssets.Where(a => a.Purpose == "evidence" && a.Bucket == Buckets.Reviews).ToListAsync(ct);
        foreach (var a in assets)
        {
            foreach (var key in MediaUrls.ObjectKeys(a))
            {
                if (!await storage.ExistsAsync(Buckets.Reviews, key, ct)) continue;
                await storage.CopyAsync(Buckets.Reviews, key, Buckets.Returns, ct);
                await storage.DeleteAsync(Buckets.Reviews, key, ct);
            }
            a.MoveTo(Buckets.Returns);
            await db.ReturnEvidence.Where(e => e.AssetId == a.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.Url, string.Empty), ct);
            await db.SaveChangesAsync(ct);
        }
        if (assets.Count > 0) logger.LogInformation("Moved {Count} return evidence file(s) to the private bucket", assets.Count);
        return assets.Count;
    }
}
