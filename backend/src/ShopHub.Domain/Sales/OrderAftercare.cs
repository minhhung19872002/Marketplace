using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

public enum CancelRequestStatus
{
    Pending,
    Approved,
    Rejected,
    AutoApproved,  // the shop did not answer in time (spec 3.7: quá hạn thì tự chấp thuận)
}

/// <summary>Buyer asks to cancel after the shop confirmed; one open request per order (unique index).</summary>
public class OrderCancelRequest : Entity
{
    private OrderCancelRequest() { }

    public OrderCancelRequest(Guid orderId, Guid buyerId, string reason, DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Vui lòng chọn lý do huỷ.");
        OrderId = orderId;
        BuyerId = buyerId;
        Reason = reason.Trim();
        DueAt = dueAt;
        CreatedAt = now;
        Status = CancelRequestStatus.Pending;
    }

    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public CancelRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? RejectReason { get; private set; }

    public void Approve(Guid? by, bool automatic, DateTimeOffset now)
    {
        EnsurePending();
        Status = automatic ? CancelRequestStatus.AutoApproved : CancelRequestStatus.Approved;
        DecidedBy = by;
        DecidedAt = now;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Từ chối yêu cầu huỷ cần có lý do.");
        Status = CancelRequestStatus.Rejected;
        DecidedBy = by;
        RejectReason = reason.Trim();
        DecidedAt = now;
    }

    /// <summary>The order moved on (shipped / cancelled another way): the request is closed as rejected by the system.</summary>
    public void Close(string reason, DateTimeOffset now)
    {
        if (Status != CancelRequestStatus.Pending) return;
        Status = CancelRequestStatus.Rejected;
        RejectReason = reason;
        DecidedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != CancelRequestStatus.Pending) throw new BusinessRuleException("Yêu cầu huỷ này đã được xử lý.");
    }
}

public enum RefundDestination
{
    Gateway,   // về đúng nguồn thanh toán online
    Wallet,    // Ví ShopHub (Phase 8)
}

public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>Money given back for an order (cancel after paying; partial returns in Phase 7).</summary>
public class Refund : Entity
{
    private Refund() { }

    public Refund(Guid orderId, Guid? paymentId, long amount, RefundDestination destination, string reason, DateTimeOffset now)
    {
        if (amount <= 0) throw new BusinessRuleException("Số tiền hoàn phải lớn hơn 0.");
        OrderId = orderId;
        PaymentId = paymentId;
        Amount = amount;
        Destination = destination;
        Reason = reason;
        CreatedAt = now;
        Status = RefundStatus.Pending;
    }

    public Guid OrderId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? ReturnId { get; private set; }
    public long Amount { get; private set; }
    public RefundDestination Destination { get; private set; }
    public RefundStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? ProviderRef { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void LinkReturn(Guid returnId) => ReturnId = returnId;

    public void Complete(bool ok, string? providerRef, DateTimeOffset now)
    {
        Status = ok ? RefundStatus.Succeeded : RefundStatus.Failed;
        ProviderRef = providerRef;
        CompletedAt = now;
    }
}
