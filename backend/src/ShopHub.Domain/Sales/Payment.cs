using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

public enum PaymentStatus
{
    Initiated,  // KHỞI TẠO
    Succeeded,  // THÀNH CÔNG
    Failed,     // THẤT BẠI
    Expired,    // HẾT HẠN
    Refunded,   // HOÀN
}

/// <summary>One payment attempt for a checkout (a retry after a failure is a new attempt).</summary>
public class Payment : Entity
{
    private Payment() { }

    public Payment(Guid checkoutId, PaymentMethod method, long amount, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (amount <= 0) throw new BusinessRuleException("Số tiền thanh toán phải lớn hơn 0.");
        CheckoutId = checkoutId;
        Method = method;
        Amount = amount;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        Status = PaymentStatus.Initiated;
    }

    public Guid CheckoutId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? ProviderTxnId { get; private set; }
    public long Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    // jsonb: last raw payload from the gateway (no card data — the gateway never sends it)
    public string? Raw { get; private set; }
    public string? FailureReason { get; private set; }
    // Where the buyer pays (gateway page / QR) — created once per attempt
    public string? RedirectUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => Status is PaymentStatus.Initiated or PaymentStatus.Failed;

    public void SetRedirect(string url) => RedirectUrl = url;

    public void Succeed(string providerTxnId, string raw, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded) return;
        ProviderTxnId = providerTxnId;
        Raw = raw;
        Status = PaymentStatus.Succeeded;
        PaidAt = now;
    }

    public void Fail(string? providerTxnId, string reason, string raw)
    {
        if (Status != PaymentStatus.Initiated) return;
        ProviderTxnId = providerTxnId;
        FailureReason = reason;
        Raw = raw;
        Status = PaymentStatus.Failed;
    }

    public void Expire()
    {
        if (IsOpen) Status = PaymentStatus.Expired;
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        Status = PaymentStatus.Refunded;
        RefundedAt = now;
    }
}

/// <summary>Each gateway notification is stored once (unique provider + event id): a replay is a no-op.</summary>
public class PaymentWebhookEvent : Entity
{
    private PaymentWebhookEvent() { }

    public PaymentWebhookEvent(string provider, string eventId, string payload, DateTimeOffset now)
    {
        Provider = provider;
        EventId = eventId;
        Payload = payload;
        ReceivedAt = now;
    }

    public string Provider { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? Result { get; private set; }

    public void Processed(string result, DateTimeOffset now)
    {
        Result = result;
        ProcessedAt = now;
    }
}
