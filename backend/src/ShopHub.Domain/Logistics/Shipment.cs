using ShopHub.Domain.Common;

namespace ShopHub.Domain.Logistics;

public enum ShipmentDirection
{
    Outbound,  // ĐI: shop → buyer
    Return,    // VỀ: buyer → shop (returns, Phase 7) or failed delivery coming back
}

public enum ShipmentStatus
{
    Created,         // vận đơn đã tạo, chờ lấy hàng
    Picked,          // đã lấy hàng
    InTransit,       // đang trung chuyển
    OutForDelivery,  // đang giao
    Delivered,       // đã giao
    Failed,          // giao thất bại
    Returning,       // đang hoàn về
    Returned,        // đã hoàn về shop
    Cancelled,       // huỷ vận đơn
}

public enum PickupMethod
{
    Pickup,   // lấy hàng tận nơi (khung giờ)
    DropOff,  // tự mang ra bưu cục
}

public class Shipment : Entity
{
    private static readonly Dictionary<ShipmentStatus, ShipmentStatus[]> Next = new()
    {
        [ShipmentStatus.Created] = [ShipmentStatus.Picked, ShipmentStatus.Cancelled],
        [ShipmentStatus.Picked] = [ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentStatus.Failed],
        [ShipmentStatus.InTransit] = [ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentStatus.Failed],
        [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.Failed, ShipmentStatus.InTransit],
        [ShipmentStatus.Failed] = [ShipmentStatus.OutForDelivery, ShipmentStatus.Returning],
        [ShipmentStatus.Returning] = [ShipmentStatus.Returned],
        [ShipmentStatus.Delivered] = [],
        [ShipmentStatus.Returned] = [],
        [ShipmentStatus.Cancelled] = [],
    };

    private Shipment() { }

    public Shipment(Guid orderId, string carrierCode, string trackingNo, ShipmentDirection direction, long fee, long codAmount, int weightG,
        PickupMethod pickupMethod, string? pickupSlot, DateTimeOffset expectedDeliveryAt, DateTimeOffset now)
    {
        if (codAmount < 0 || fee < 0) throw new BusinessRuleException("Số tiền vận đơn không được âm.");
        OrderId = orderId;
        CarrierCode = carrierCode;
        TrackingNo = trackingNo;
        Direction = direction;
        Fee = fee;
        CodAmount = codAmount;
        WeightG = weightG;
        PickupMethod = pickupMethod;
        PickupSlot = pickupSlot;
        ExpectedDeliveryAt = expectedDeliveryAt;
        CreatedAt = now;
        LastEventAt = now;
        Status = ShipmentStatus.Created;
        Events.Add(new ShipmentEvent(Id, ShipmentStatus.Created, null, "Đã tạo vận đơn, chờ lấy hàng", $"created:{trackingNo}", now, null));
    }

    public Guid OrderId { get; private set; }
    public string CarrierCode { get; private set; } = string.Empty;
    public string TrackingNo { get; private set; } = string.Empty;
    public ShipmentDirection Direction { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public long Fee { get; private set; }
    // Cash to collect from the buyer on delivery (COD), 0 for prepaid orders
    public long CodAmount { get; private set; }
    public int WeightG { get; private set; }
    public PickupMethod PickupMethod { get; private set; }
    public string? PickupSlot { get; private set; }
    public DateTimeOffset ExpectedDeliveryAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastEventAt { get; private set; }
    public DateTimeOffset? LabelPrintedAt { get; private set; }
    public uint Version { get; private set; }

    public List<ShipmentEvent> Events { get; private set; } = [];

    public bool IsFinal => Status is ShipmentStatus.Delivered or ShipmentStatus.Returned or ShipmentStatus.Cancelled;

    public static bool CanMove(ShipmentStatus from, ShipmentStatus to) => Next.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>Apply a carrier event; replays and out-of-order events that would go backwards are ignored (false).</summary>
    public bool Apply(ShipmentStatus status, string? location, string description, string externalId, DateTimeOffset occurredAt, string? raw)
    {
        if (Events.Any(e => e.ExternalId == externalId) || status == Status || !CanMove(Status, status)) return false;
        Status = status;
        LastEventAt = occurredAt;
        Events.Add(new ShipmentEvent(Id, status, location, description, externalId, occurredAt, raw));
        return true;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Created) throw new BusinessRuleException("Chỉ huỷ được vận đơn chưa lấy hàng.");
        Apply(ShipmentStatus.Cancelled, null, "Đã huỷ vận đơn", $"cancel:{TrackingNo}", now, null);
    }

    public void MarkPrinted(DateTimeOffset now) => LabelPrintedAt ??= now;

    public static string Label(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created => "Chờ lấy hàng",
        ShipmentStatus.Picked => "Đã lấy hàng",
        ShipmentStatus.InTransit => "Đang trung chuyển",
        ShipmentStatus.OutForDelivery => "Đang giao hàng",
        ShipmentStatus.Delivered => "Giao hàng thành công",
        ShipmentStatus.Failed => "Giao hàng thất bại",
        ShipmentStatus.Returning => "Đang hoàn về",
        ShipmentStatus.Returned => "Đã hoàn về người gửi",
        ShipmentStatus.Cancelled => "Đã huỷ vận đơn",
        _ => status.ToString(),
    };
}

public class ShipmentEvent : Entity
{
    private ShipmentEvent() { }

    internal ShipmentEvent(Guid shipmentId, ShipmentStatus status, string? location, string description, string externalId, DateTimeOffset occurredAt,
        string? raw)
    {
        ShipmentId = shipmentId;
        Status = status;
        Location = location;
        Description = description;
        ExternalId = externalId;
        OccurredAt = occurredAt;
        Raw = raw;
    }

    public Guid ShipmentId { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string? Location { get; private set; }
    public string Description { get; private set; } = string.Empty;
    // Carrier's event id: a replayed notification is ignored
    public string ExternalId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Raw { get; private set; }
}
