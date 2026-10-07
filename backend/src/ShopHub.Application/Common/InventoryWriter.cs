using ShopHub.Application.Abstractions;
using ShopHub.Domain.Catalog;

namespace ShopHub.Application.Common;

/// <summary>One change of a SKU's stock / reserved quantity and why (spec 3.3).</summary>
public record InventoryMove(Guid SkuId, int DeltaStock, int DeltaReserved, InventoryReason Reason, string RefType, Guid RefId, Guid? ActorId,
    string? Note, bool RequireActive = false);

/// <summary>
/// The only way stock / reserved change outside the product editor (code rule): one conditional UPDATE — never read
/// then write — that keeps <c>stock ≥ reserved ≥ 0</c>, and the <c>inventory_movements</c> row added in the same
/// transaction, so a quantity can never change without its trace (or the trace exist without the change).
/// </summary>
public sealed class InventoryWriter(IApplicationDbContext db, IClock clock)
{
    /// <summary>False when the condition does not hold (not enough available / held); nothing is changed then.</summary>
    public async Task<bool> TryMoveAsync(InventoryMove move, CancellationToken ct)
    {
        if (!db.InTransaction)
            throw new InvalidOperationException($"Inventory change of SKU {move.SkuId} ({move.Reason}) outside a transaction.");
        var (ds, dr) = (move.DeltaStock, move.DeltaReserved);
        var changed = await db.ExecuteSqlAsync($"""
            UPDATE catalog.skus SET stock = stock + {ds}, reserved = reserved + {dr}
            WHERE id = {move.SkuId} AND stock + {ds} >= reserved + {dr} AND reserved + {dr} >= 0 AND stock + {ds} >= 0
              AND (NOT {move.RequireActive} OR is_active)
            """, ct);
        if (changed == 0) return false;
        db.InventoryMovements.Add(new InventoryMovement(move.SkuId, ds, dr, move.Reason, move.RefType, move.RefId, move.ActorId, move.Note, clock.UtcNow));
        return true;
    }
}
