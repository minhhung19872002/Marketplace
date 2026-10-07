using System.Security.Cryptography;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;

namespace ShopHub.Infrastructure.Finance;

/// <summary>
/// Order events → ledger (spec 3.9). Runs in its own scope and transaction (own connection): a failing posting rolls
/// back completely and the message is retried; the sync posts only differences, so a redelivery changes nothing.
/// </summary>
public sealed class FinanceOrderEventHandler(IServiceScopeFactory scopes) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Events that change money: collected, refunded, completed (accrual)
    private static readonly HashSet<string> MoneyEvents =
    [
        OrderEvents.Paid, OrderEvents.Delivered, OrderEvents.Completed, OrderEvents.Cancelled, OrderEvents.Returned, OrderEvents.Refunded,
        OrderEvents.ReturnRefunded,
    ];

    public string Type => OutboxTypes.OrderEvent;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<OrderEventPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        if (!MoneyEvents.Contains(e.Event)) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderLedger>().SyncAsync(e.OrderId, ct);
    }
}

/// <summary>
/// Bank payout stand-in: accepts the transfer and returns a reference, like a bank API would (never logs the account
/// number). Account numbers ending in 0000 are refused, to exercise the "bank refused → money back" path.
/// </summary>
public sealed class SimulatedBankPayout(ILogger<SimulatedBankPayout> logger) : IBankPayout
{
    public Task<BankTransferResult> TransferAsync(BankTransferRequest request, CancellationToken ct)
    {
        if (request.AccountNo.EndsWith("0000", StringComparison.Ordinal))
        {
            logger.LogInformation("Simulated bank refused withdrawal {WithdrawalId}", request.WithdrawalId);
            return Task.FromResult(new BankTransferResult(false, null, "Tài khoản nhận không hợp lệ (giả lập)."));
        }
        var reference = $"SIMBANK{DateTime.UtcNow:yyMMddHHmmss}{RandomNumberGenerator.GetHexString(6)}";
        logger.LogInformation("Simulated bank paid withdrawal {WithdrawalId}: {Amount} ({Reference})", request.WithdrawalId, request.Amount, reference);
        return Task.FromResult(new BankTransferResult(true, reference, null));
    }
}

/// <summary>Hangfire entry for <see cref="SettlementService"/>.</summary>
public sealed class SettlementJob(SettlementService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
}

/// <summary>Hangfire entry for <see cref="LedgerCheckService"/>.</summary>
public sealed class LedgerCheckJob(LedgerCheckService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunJobAsync() => service.RepairAsync(CancellationToken.None);
}

/// <summary>
/// The statement files the simulated providers would send: the gateway's successful transactions (from its own book
/// <c>sys.simulated_payments</c>) and the carrier's delivered COD parcels — what the admin reconciles against.
/// </summary>
public sealed class SimulatedProviderStatements(Persistence.ShopHubDbContext db) : IProviderStatements
{
    public async Task<string> GatewayStatementCsvAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Set<Domain.SystemConfig.SimulatedPayment>().Where(p => p.Outcome == Domain.SystemConfig.SimulatedPaymentOutcome.Succeeded
                                                                     && p.CreatedAt >= from && p.CreatedAt < to).OrderBy(p => p.CreatedAt), ct);
        var sb = new System.Text.StringBuilder("txn_id,payment_id,amount,refunded_amount,paid_at\n");
        foreach (var p in rows) sb.Append($"{p.TxnId},{p.PaymentId},{p.Amount},{p.RefundedAmount},{p.CreatedAt:O}\n");
        return sb.ToString();
    }

    public async Task<string> CarrierCodStatementCsvAsync(string carrierCode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var simulated = db.Carriers.Where(c => c.Code == carrierCode && c.Provider == Commerce.SimulatedCarrier.ProviderName).Select(c => c.Code);
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Shipments.Where(s => simulated.Contains(s.CarrierCode) && s.Direction == Domain.Logistics.ShipmentDirection.Outbound && s.Status == Domain.Logistics.ShipmentStatus.Delivered
                                    && s.CodAmount > 0 && s.LastEventAt >= from && s.LastEventAt < to).OrderBy(s => s.LastEventAt), ct);
        var sb = new System.Text.StringBuilder("tracking_no,cod_amount,shipping_fee,delivered_at\n");
        foreach (var s in rows) sb.Append($"{s.TrackingNo},{s.CodAmount},{s.Fee},{s.LastEventAt:O}\n");
        return sb.ToString();
    }
}
