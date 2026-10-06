using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Finance;

namespace ShopHub.Application.Features.Finance;

public static class LedgerKinds
{
    public const string OrderSync = "ORDER_SYNC";
    public const string Release = "RELEASE";
    public const string WalletPay = "WALLET_PAY";
    public const string Topup = "TOPUP";
    public const string WithdrawRequest = "WITHDRAW_REQUEST";
    public const string WithdrawPaid = "WITHDRAW_PAID";
    public const string WithdrawReversed = "WITHDRAW_REVERSED";
}

public static class LedgerRefs
{
    public const string Order = "order";
    public const string Topup = "topup";
    public const string Withdrawal = "withdrawal";
}

/// <summary>An account is identified by who owns it and what it holds (one row per owner and type).</summary>
public readonly record struct AccountKey(LedgerOwnerType OwnerType, Guid OwnerId, LedgerAccountType Type)
{
    public static AccountKey Platform(LedgerAccountType type) => new(LedgerOwnerType.Platform, Ledger.PlatformId, type);
    public static AccountKey Shop(Guid shopId, LedgerAccountType type) => new(LedgerOwnerType.Shop, shopId, type);
    public static AccountKey Wallet(Guid userId) => new(LedgerOwnerType.Buyer, userId, LedgerAccountType.BuyerWallet);
}

public record LedgerLine(AccountKey Account, LedgerDirection Direction, long Amount);

/// <summary>
/// Double-entry ledger (spec 3.9). Every posting is balanced (Σ debits = Σ credits); the balance shown anywhere is the
/// sum of the entries, kept as a cached column updated by a conditional UPDATE in the same transaction so an account
/// that may not go negative (shop balances, wallets) cannot be overdrawn even by parallel requests — the CHECK
/// constraint is the backstop and <see cref="LedgerCheckService"/> recomputes the cache from the entries.
/// </summary>
public sealed class Ledger(IApplicationDbContext db, IClock clock)
{
    public static readonly Guid PlatformId = Guid.Empty;

    private readonly Dictionary<AccountKey, Guid> _ids = [];

    public async Task<Guid> AccountIdAsync(AccountKey key, CancellationToken ct)
    {
        if (_ids.TryGetValue(key, out var cached)) return cached;
        var id = await FindAsync(key, ct);
        if (id is null)
        {
            var allowNegative = key.OwnerType == LedgerOwnerType.Platform;
            await db.ExecuteSqlAsync($"""
                INSERT INTO finance.ledger_accounts (id, owner_type, owner_id, type, balance, allow_negative, created_at)
                VALUES ({Guid.NewGuid()}, {key.OwnerType.ToString()}, {key.OwnerId}, {key.Type.ToString()}, 0, {allowNegative}, {clock.UtcNow})
                ON CONFLICT (owner_type, owner_id, type) DO NOTHING
                """, ct);
            id = await FindAsync(key, ct) ?? throw new InvalidOperationException("Ledger account was not created.");
        }
        _ids[key] = id.Value;
        return id.Value;
    }

    private Task<Guid?> FindAsync(AccountKey key, CancellationToken ct) =>
        db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OwnerType == key.OwnerType && a.OwnerId == key.OwnerId && a.Type == key.Type)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

    public async Task<long> BalanceAsync(AccountKey key, CancellationToken ct) =>
        await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OwnerType == key.OwnerType && a.OwnerId == key.OwnerId && a.Type == key.Type)
            .Select(a => (long?)a.Balance).FirstOrDefaultAsync(ct) ?? 0;

    /// <summary>
    /// Writes one balanced transaction. Must run inside the caller's database transaction; the caller saves.
    /// Throws <see cref="ConflictException"/> <c>INSUFFICIENT_BALANCE</c> when a non-negative account would go below zero.
    /// </summary>
    public async Task<LedgerTransaction> PostAsync(string kind, string refType, Guid refId, string description, IReadOnlyList<LedgerLine> lines,
        string? dedupeKey, CancellationToken ct)
    {
        if (lines.Count == 0) throw new ArgumentException("A posting needs at least one line.", nameof(lines));
        if (lines.Any(l => l.Amount <= 0)) throw new ArgumentException("Ledger amounts must be positive.", nameof(lines));
        var debits = lines.Where(l => l.Direction == LedgerDirection.Debit).Sum(l => l.Amount);
        var credits = lines.Where(l => l.Direction == LedgerDirection.Credit).Sum(l => l.Amount);
        if (debits != credits) throw new InvalidOperationException($"Unbalanced posting {kind} {refType}:{refId}: debits {debits} ≠ credits {credits}.");

        var now = clock.UtcNow;
        var tx = new LedgerTransaction(kind, refType, refId, description, dedupeKey, now);
        var deltas = new Dictionary<Guid, long>();
        foreach (var line in lines)
        {
            var accountId = await AccountIdAsync(line.Account, ct);
            tx.Entries.Add(new LedgerEntry(tx.Id, accountId, line.Direction, line.Amount, refType, refId, description, now));
            var grows = LedgerAccount.IsDebitNormal(line.Account.Type) == (line.Direction == LedgerDirection.Debit);
            deltas[accountId] = deltas.GetValueOrDefault(accountId) + (grows ? line.Amount : -line.Amount);
        }

        // Fixed order (account id) so two postings touching the same accounts never deadlock
        foreach (var (accountId, delta) in deltas.Where(d => d.Value != 0).OrderBy(d => d.Key))
        {
            var updated = await db.ExecuteSqlAsync($"""
                UPDATE finance.ledger_accounts SET balance = balance + {delta}
                WHERE id = {accountId} AND (allow_negative OR balance + {delta} >= 0)
                """, ct);
            if (updated == 0) throw new ConflictException("Số dư không đủ.", "INSUFFICIENT_BALANCE");
        }
        db.LedgerTransactions.Add(tx);
        return tx;
    }

    /// <summary>What has been posted so far against one reference, per account (credits − debits), saved or not.</summary>
    public async Task<Dictionary<AccountKey, long>> PostedForAsync(string refType, Guid refId, CancellationToken ct)
    {
        var saved = await (from e in db.LedgerEntries.AsNoTracking()
                           join a in db.LedgerAccounts.AsNoTracking() on e.AccountId equals a.Id
                           where e.RefType == refType && e.RefId == refId
                           select new { e.Id, Key = new AccountKey(a.OwnerType, a.OwnerId, a.Type), e.Direction, e.Amount }).ToListAsync(ct);
        var seen = saved.Select(e => e.Id).ToHashSet();
        // Entries added in this unit of work and not saved yet count too
        var pending = db.LedgerTransactions.Local.SelectMany(t => t.Entries)
            .Where(e => e.RefType == refType && e.RefId == refId && !seen.Contains(e.Id))
            .Select(e => new { e.Id, Key = _ids.First(kv => kv.Value == e.AccountId).Key, e.Direction, e.Amount });
        return saved.Concat(pending).GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Direction == LedgerDirection.Credit ? e.Amount : -e.Amount));
    }

    /// <summary>
    /// Brings what is posted for a reference to <paramref name="target"/> (credits − debits per account) by posting
    /// only the difference. Re-running it changes nothing: safe for at-least-once delivery.
    /// </summary>
    public async Task<LedgerTransaction?> SyncAsync(string kind, string refType, Guid refId, string description, IReadOnlyDictionary<AccountKey, long> target,
        CancellationToken ct)
    {
        var posted = await PostedForAsync(refType, refId, ct);
        var lines = new List<LedgerLine>();
        foreach (var key in target.Keys.Union(posted.Keys))
        {
            var diff = target.GetValueOrDefault(key) - posted.GetValueOrDefault(key);
            if (diff > 0) lines.Add(new LedgerLine(key, LedgerDirection.Credit, diff));
            else if (diff < 0) lines.Add(new LedgerLine(key, LedgerDirection.Debit, -diff));
        }
        return lines.Count == 0 ? null : await PostAsync(kind, refType, refId, description, lines, null, ct);
    }
}
