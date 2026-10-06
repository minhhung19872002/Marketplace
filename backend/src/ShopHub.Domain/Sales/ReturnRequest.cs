using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

public enum ReturnType
{
    RefundOnly,       // Chỉ hoàn tiền
    ReturnAndRefund,  // Trả hàng & hoàn tiền
}

public enum ReturnReason
{
    MissingItem,      // thiếu hàng
    WrongItem,        // sai hàng
    Damaged,          // hư hỏng
    NotAsDescribed,   // không giống mô tả
    Counterfeit,      // hàng giả
    Other,
}

public enum ReturnStatus
{
    Requested,         // chờ shop phản hồi
    PartialOffered,    // shop đề nghị hoàn một phần, chờ người mua
    Rejected,          // shop từ chối — người mua có thể khiếu nại
    Disputed,          // đang khiếu nại, chờ sàn phân xử
    AwaitingReturn,    // đã chấp thuận, chờ người mua gửi hàng về
    Returning,         // hàng đang trên đường về shop
    AwaitingShopCheck, // hàng đã về, chờ shop xác nhận
    Refunded,          // đã hoàn tiền
    Closed,            // kết thúc, không hoàn (shop thắng / người mua không khiếu nại)
    Cancelled,         // người mua huỷ yêu cầu
}

public enum ReturnParty
{
    Buyer,
    Shop,
    Admin,
}

/// <summary>
/// Return / refund request for some lines of a delivered order (spec 3.8). The refund is what the buyer actually
/// paid for those units after every allocated discount; xu used on them go back as xu.
/// </summary>
public class ReturnRequest : Entity
{
    private static readonly Dictionary<ReturnStatus, ReturnStatus[]> Allowed = new()
    {
        [ReturnStatus.Requested] = [ReturnStatus.PartialOffered, ReturnStatus.Rejected, ReturnStatus.AwaitingReturn, ReturnStatus.Refunded, ReturnStatus.Cancelled],
        [ReturnStatus.PartialOffered] = [ReturnStatus.Refunded, ReturnStatus.Disputed, ReturnStatus.Cancelled],
        [ReturnStatus.Rejected] = [ReturnStatus.Disputed, ReturnStatus.Closed, ReturnStatus.Cancelled],
        [ReturnStatus.Disputed] = [ReturnStatus.AwaitingReturn, ReturnStatus.Refunded, ReturnStatus.Closed],
        [ReturnStatus.AwaitingReturn] = [ReturnStatus.Returning, ReturnStatus.Cancelled],
        [ReturnStatus.Returning] = [ReturnStatus.AwaitingShopCheck],
        [ReturnStatus.AwaitingShopCheck] = [ReturnStatus.Refunded, ReturnStatus.Disputed],
        [ReturnStatus.Refunded] = [],
        [ReturnStatus.Closed] = [],
        [ReturnStatus.Cancelled] = [],
    };

    private ReturnRequest() { }

    public ReturnRequest(Guid orderId, Guid buyerId, Guid shopId, string code, ReturnType type, ReturnReason reason, string description,
        DateTimeOffset respondBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 10)
            throw new BusinessRuleException("Vui lòng mô tả vấn đề (ít nhất 10 ký tự).");
        OrderId = orderId;
        BuyerId = buyerId;
        ShopId = shopId;
        Code = code;
        Type = type;
        Reason = reason;
        Description = description.Trim();
        RespondBy = respondBy;
        CreatedAt = now;
        UpdatedAt = now;
        Status = ReturnStatus.Requested;
        History.Add(new ReturnHistory(Id, null, Status, ReturnParty.Buyer, "Người mua gửi yêu cầu", now));
    }

    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ShopId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public ReturnType Type { get; private set; }
    public ReturnReason Reason { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ReturnStatus Status { get; private set; }
    // Refund computed from the lines (what was paid for them), and what is finally refunded
    public long RequestedAmount { get; private set; }
    public long RequestedCoins { get; private set; }
    public long? OfferedAmount { get; private set; }
    public long? RefundAmount { get; private set; }
    public long? RefundCoins { get; private set; }
    public string? ShopNote { get; private set; }
    // Deadline for whoever must act next (shop answer, dispute, shop check)
    public DateTimeOffset? RespondBy { get; private set; }
    public bool Restock { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public uint Version { get; private set; }

    public List<ReturnItem> Items { get; private set; } = [];
    public List<ReturnEvidence> Evidence { get; private set; } = [];
    public List<ReturnHistory> History { get; private set; } = [];

    public bool IsOpen => Status is not (ReturnStatus.Refunded or ReturnStatus.Closed or ReturnStatus.Cancelled);

    public static string Label(ReturnStatus status) => status switch
    {
        ReturnStatus.Requested => "Chờ shop phản hồi",
        ReturnStatus.PartialOffered => "Shop đề nghị hoàn một phần",
        ReturnStatus.Rejected => "Shop từ chối",
        ReturnStatus.Disputed => "Đang khiếu nại",
        ReturnStatus.AwaitingReturn => "Chờ gửi hàng trả",
        ReturnStatus.Returning => "Hàng đang được trả về",
        ReturnStatus.AwaitingShopCheck => "Chờ shop kiểm hàng",
        ReturnStatus.Refunded => "Đã hoàn tiền",
        ReturnStatus.Closed => "Đã đóng",
        ReturnStatus.Cancelled => "Đã huỷ yêu cầu",
        _ => status.ToString(),
    };

    public void SetAmounts(long money, long coins)
    {
        if (money < 0 || coins < 0) throw new BusinessRuleException("Số tiền hoàn không được âm.");
        RequestedAmount = money;
        RequestedCoins = coins;
    }

    public void Move(ReturnStatus to, ReturnParty by, string? note, DateTimeOffset now, DateTimeOffset? respondBy = null)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(to))
            throw new BusinessRuleException($"Không thể chuyển yêu cầu {Code} từ \"{Label(Status)}\" sang \"{Label(to)}\".");
        History.Add(new ReturnHistory(Id, Status, to, by, note, now));
        Status = to;
        RespondBy = respondBy;
        UpdatedAt = now;
        if (by == ReturnParty.Shop && note is not null) ShopNote = note;
    }

    public void OfferPartial(long amount)
    {
        if (amount <= 0 || amount >= RequestedAmount) throw new BusinessRuleException("Số tiền đề nghị phải lớn hơn 0 và nhỏ hơn số tiền yêu cầu hoàn.");
        OfferedAmount = amount;
    }

    public void SetRestock(bool restock) => Restock = restock;

    /// <summary>Money (and xu) actually given back; the final word on the amounts.</summary>
    public void MarkRefunded(long amount, long coins, DateTimeOffset now)
    {
        if (amount < 0 || amount > RequestedAmount || coins < 0 || coins > RequestedCoins)
            throw new BusinessRuleException("Số tiền hoàn vượt quá số đã thanh toán cho các sản phẩm trả.");
        RefundAmount = amount;
        RefundCoins = coins;
        RefundedAt = now;
    }

    public void AddEvidence(ReturnParty party, ReturnEvidenceType type, Guid? assetId, string url, string? note)
    {
        if (Evidence.Count(e => e.Party == party) >= 10) throw new BusinessRuleException("Tối đa 10 bằng chứng mỗi bên.");
        Evidence.Add(new ReturnEvidence(Id, party, type, assetId, url, note));
    }
}

/// <summary>A line (and quantity) being returned. Open lines are unique per order line (one open return per line).</summary>
public class ReturnItem : Entity
{
    private ReturnItem() { }

    public ReturnItem(Guid returnId, Guid orderItemId, int quantity, long refundAmount, long refundCoins)
    {
        if (quantity < 1) throw new BusinessRuleException("Số lượng trả phải từ 1 trở lên.");
        ReturnId = returnId;
        OrderItemId = orderItemId;
        Quantity = quantity;
        RefundAmount = refundAmount;
        RefundCoins = refundCoins;
        IsOpen = true;
    }

    public Guid ReturnId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public int Quantity { get; private set; }
    public long RefundAmount { get; private set; }
    public long RefundCoins { get; private set; }
    public bool IsOpen { get; private set; }

    public void Close() => IsOpen = false;
}

public enum ReturnEvidenceType
{
    Image,
    Video,
}

public class ReturnEvidence : Entity
{
    private ReturnEvidence() { }

    internal ReturnEvidence(Guid returnId, ReturnParty party, ReturnEvidenceType type, Guid? assetId, string url, string? note)
    {
        ReturnId = returnId;
        Party = party;
        Type = type;
        AssetId = assetId;
        Url = url;
        Note = note;
    }

    public Guid ReturnId { get; private set; }
    public ReturnParty Party { get; private set; }
    public ReturnEvidenceType Type { get; private set; }
    public Guid? AssetId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? Note { get; private set; }
}

public class ReturnHistory : Entity
{
    private ReturnHistory() { }

    internal ReturnHistory(Guid returnId, ReturnStatus? from, ReturnStatus to, ReturnParty by, string? note, DateTimeOffset at)
    {
        ReturnId = returnId;
        FromStatus = from;
        ToStatus = to;
        By = by;
        Note = note;
        OccurredAt = at;
    }

    public Guid ReturnId { get; private set; }
    public ReturnStatus? FromStatus { get; private set; }
    public ReturnStatus ToStatus { get; private set; }
    public ReturnParty By { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public enum DisputeDecision
{
    FavorBuyer,
    FavorShop,
}

/// <summary>The buyer escalates a refused return to the platform; an admin decides with a written reason.</summary>
public class Dispute : Entity
{
    private Dispute() { }

    public Dispute(Guid returnId, Guid openedBy, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng nêu lý do khiếu nại.");
        ReturnId = returnId;
        OpenedBy = openedBy;
        Reason = reason.Trim();
        OpenedAt = now;
    }

    public Guid ReturnId { get; private set; }
    public Guid OpenedBy { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; private set; }
    public Guid? AdminId { get; private set; }
    public DisputeDecision? Decision { get; private set; }
    public string? DecisionReason { get; private set; }
    public long? RefundAmount { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public void Decide(Guid adminId, DisputeDecision decision, string reason, long? refundAmount, DateTimeOffset now)
    {
        if (ClosedAt is not null) throw new BusinessRuleException("Khiếu nại này đã được phân xử.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Phân xử cần ghi rõ lý do.");
        AdminId = adminId;
        Decision = decision;
        DecisionReason = reason.Trim();
        RefundAmount = refundAmount;
        ClosedAt = now;
    }
}
