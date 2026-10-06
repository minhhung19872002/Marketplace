using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

public enum SimulatedPaymentOutcome
{
    Succeeded,
    Failed,
    Refunded,
}

/// <summary>
/// The SimulatedGateway's own ledger — what a real gateway would know on its side. Used to answer "query transaction"
/// when reconciling, exactly like asking VNPay/MoMo; never read by the shop's business logic directly.
/// </summary>
public class SimulatedPayment : Entity
{
    private SimulatedPayment() { }

    public SimulatedPayment(Guid paymentId, string txnId, long amount, SimulatedPaymentOutcome outcome, DateTimeOffset now)
    {
        PaymentId = paymentId;
        TxnId = txnId;
        Amount = amount;
        Outcome = outcome;
        CreatedAt = now;
    }

    public Guid PaymentId { get; private set; }
    public string TxnId { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public SimulatedPaymentOutcome Outcome { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    public void Refund(DateTimeOffset now)
    {
        Outcome = SimulatedPaymentOutcome.Refunded;
        RefundedAt = now;
    }
}
