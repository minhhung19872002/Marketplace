using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> b)
    {
        // The cached balance may never go below zero for shop / buyer accounts: overdrawing fails in the database itself
        b.ToTable("ledger_accounts", "finance", t => t.HasCheckConstraint("ck_ledger_accounts_balance", "allow_negative OR balance >= 0"));
        b.HasKey(a => a.Id);
        b.Property(a => a.OwnerType).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(a => new { a.OwnerType, a.OwnerId, a.Type }).IsUnique().HasDatabaseName("ux_ledger_accounts_owner");
    }
}

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> b)
    {
        b.ToTable("ledger_transactions", "finance");
        b.HasKey(t => t.Id);
        b.Property(t => t.Kind).HasMaxLength(40).IsRequired();
        b.Property(t => t.RefType).HasMaxLength(30).IsRequired();
        b.Property(t => t.Description).HasMaxLength(300).IsRequired();
        b.Property(t => t.DedupeKey).HasMaxLength(120);
        b.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL").HasDatabaseName("ux_ledger_transactions_dedupe");
        b.HasIndex(t => new { t.RefType, t.RefId }).HasDatabaseName("ix_ledger_transactions_ref");
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries", "finance", t => t.HasCheckConstraint("ck_ledger_entries_amount", "amount > 0"));
        b.HasKey(e => e.Id);
        b.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.RefType).HasMaxLength(30).IsRequired();
        b.Property(e => e.Description).HasMaxLength(300).IsRequired();
        b.HasOne<LedgerAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AccountId, e.PostedAt }).IsDescending(false, true).HasDatabaseName("ix_ledger_entries_account");
        b.HasIndex(e => new { e.RefType, e.RefId }).HasDatabaseName("ix_ledger_entries_ref");
    }
}

internal sealed class FeeRuleConfiguration : IEntityTypeConfiguration<FeeRule>
{
    public void Configure(EntityTypeBuilder<FeeRule> b)
    {
        b.ToTable("fee_rules", "finance", t =>
        {
            t.HasCheckConstraint("ck_fee_rules_rate", "rate_bp BETWEEN 0 AND 5000");
            t.HasCheckConstraint("ck_fee_rules_window", "valid_to IS NULL OR valid_to > valid_from");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.FeeType).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Note).HasMaxLength(200);
        b.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        // One open-ended rule per scope at a time
        b.HasIndex(r => new { r.CategoryId, r.FeeType }).IsUnique().HasFilter("valid_to IS NULL").AreNullsDistinct(false)
            .HasDatabaseName("ux_fee_rules_open");
    }
}

internal sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> b)
    {
        b.ToTable("settlements", "finance");
        b.HasKey(s => s.Id);
        b.Property(s => s.Code).HasMaxLength(30).IsRequired();
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.FileUrl).HasMaxLength(500);
        b.HasOne<Shop>().WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SettlementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => s.Code).IsUnique().HasDatabaseName("ux_settlements_code");
        b.HasIndex(s => new { s.ShopId, s.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_settlements_shop");
    }
}

internal sealed class SettlementItemConfiguration : IEntityTypeConfiguration<SettlementItem>
{
    public void Configure(EntityTypeBuilder<SettlementItem> b)
    {
        b.ToTable("settlement_items", "finance");
        b.HasKey(i => i.Id);
        b.Ignore(i => i.Gross);
        b.HasOne<Order>().WithMany().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        // An order is released once — two parallel release runs cannot both pay it
        b.HasIndex(i => i.OrderId).IsUnique().HasDatabaseName("ux_settlement_items_order");
        b.HasIndex(i => new { i.ShopId, i.ReleasedAt }).HasDatabaseName("ix_settlement_items_shop");
    }
}

internal sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> b)
    {
        b.ToTable("withdrawals", "finance", t => t.HasCheckConstraint("ck_withdrawals_amount", "amount > 0"));
        b.HasKey(w => w.Id);
        b.Property(w => w.OwnerType).HasConversion<string>().HasMaxLength(20);
        b.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(w => w.BankCode).HasMaxLength(20).IsRequired();
        b.Property(w => w.AccountLast4).HasMaxLength(4).IsRequired();
        b.Property(w => w.AccountName).HasMaxLength(100).IsRequired();
        b.Property(w => w.RejectReason).HasMaxLength(300);
        b.Property(w => w.BankRef).HasMaxLength(100);
        b.Property(w => w.Version).IsRowVersion();
        b.HasIndex(w => new { w.OwnerType, w.OwnerId, w.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_withdrawals_owner");
        b.HasIndex(w => new { w.Status, w.CreatedAt }).HasDatabaseName("ix_withdrawals_status");
    }
}

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> b)
    {
        b.ToTable("wallets", "finance");
        b.HasKey(w => w.Id);
        b.Property(w => w.PinHash).HasMaxLength(200).IsRequired();
        b.Property(w => w.Version).IsRowVersion();
        b.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(w => w.UserId).IsUnique().HasDatabaseName("ux_wallets_user");
    }
}

internal sealed class WalletTopupConfiguration : IEntityTypeConfiguration<WalletTopup>
{
    public void Configure(EntityTypeBuilder<WalletTopup> b)
    {
        b.ToTable("wallet_topups", "finance", t => t.HasCheckConstraint("ck_wallet_topups_amount", "amount > 0"));
        b.HasKey(t => t.Id);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.UserId, t.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_wallet_topups_user");
    }
}

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_accounts", "finance");
        b.HasKey(a => a.Id);
        b.Property(a => a.BankCode).HasMaxLength(20).IsRequired();
        b.Property(a => a.AccountNoEncrypted).HasMaxLength(500).IsRequired();
        b.Property(a => a.AccountNoLast4).HasMaxLength(4).IsRequired();
        b.Property(a => a.AccountName).HasMaxLength(100).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => a.UserId).HasDatabaseName("ix_bank_accounts_user");
    }
}
