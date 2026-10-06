using Microsoft.EntityFrameworkCore;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

using ShopHub.Domain.Sales;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Logistics;
namespace ShopHub.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<SystemParameter> SystemParameters { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Address> Addresses { get; }
    DbSet<AdminDivision> AdminDivisions { get; }

    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<Category> Categories { get; }
    DbSet<CategoryAttribute> CategoryAttributes { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<Sku> Skus { get; }
    DbSet<VariantTier> VariantTiers { get; }
    DbSet<VariantOption> VariantOptions { get; }
    DbSet<ProductMedia> ProductMedia { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<Shop> Shops { get; }
    DbSet<ShopKyc> ShopKycs { get; }
    DbSet<ShopWarehouse> ShopWarehouses { get; }
    DbSet<ShopStaff> ShopStaff { get; }
    DbSet<ShopCategory> ShopCategories { get; }
    DbSet<ShopCategoryProduct> ShopCategoryProducts { get; }
    DbSet<ShopDecoration> ShopDecorations { get; }
    DbSet<ShopBankAccount> ShopBankAccounts { get; }

    DbSet<Wishlist> Wishlists { get; }
    DbSet<ShopFollower> ShopFollowers { get; }
    DbSet<ProductView> ProductViews { get; }
    DbSet<SearchLog> SearchLogs { get; }

    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<CheckoutSession> CheckoutSessions { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderItemDiscount> OrderItemDiscounts { get; }
    DbSet<OrderStatusHistory> OrderStatusHistory { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; }
    DbSet<Voucher> Vouchers { get; }
    DbSet<VoucherClaim> VoucherClaims { get; }
    DbSet<VoucherUsage> VoucherUsages { get; }
    DbSet<VoucherUserCounter> VoucherUserCounters { get; }
    DbSet<CoinEntry> CoinLedger { get; }
    DbSet<Carrier> Carriers { get; }
    DbSet<ShippingRate> ShippingRates { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentEvent> ShipmentEvents { get; }
    DbSet<OrderCancelRequest> OrderCancelRequests { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ShopPenalty> ShopPenalties { get; }
    DbSet<Domain.Engage.CartAdd> CartAdds { get; }
    DbSet<Domain.Catalog.ProductReport> ProductReports { get; }
    DbSet<Domain.SystemConfig.CmsPage> CmsPages { get; }
    DbSet<Domain.SystemConfig.MessageTemplate> MessageTemplates { get; }
    DbSet<Review> Reviews { get; }
    DbSet<ReviewMedia> ReviewMedia { get; }
    DbSet<ReviewReport> ReviewReports { get; }
    DbSet<ReturnRequest> ReturnRequests { get; }
    DbSet<ReturnItem> ReturnItems { get; }
    DbSet<ReturnEvidence> ReturnEvidence { get; }
    DbSet<ReturnHistory> ReturnHistory { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<LedgerAccount> LedgerAccounts { get; }
    DbSet<LedgerTransaction> LedgerTransactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<FeeRule> FeeRules { get; }
    DbSet<Settlement> Settlements { get; }
    DbSet<SettlementItem> SettlementItems { get; }
    DbSet<Withdrawal> Withdrawals { get; }
    DbSet<Wallet> Wallets { get; }
    DbSet<WalletTopup> WalletTopups { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<PromotionProduct> PromotionProducts { get; }
    DbSet<PromotionSku> PromotionSkus { get; }
    DbSet<PriceProgram> PricePrograms { get; }
    DbSet<FlashSaleSlot> FlashSaleSlots { get; }
    DbSet<FlashSaleItem> FlashSaleItems { get; }
    DbSet<FlashSaleBuyer> FlashSaleBuyers { get; }
    DbSet<Banner> Banners { get; }
    DbSet<Campaign> Campaigns { get; }
    DbSet<CheckIn> CheckIns { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<QuickReply> QuickReplies { get; }
    DbSet<ShopChatSettings> ShopChatSettings { get; }
    DbSet<ChatReport> ChatReports { get; }
    DbSet<NotificationPref> NotificationPrefs { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<Broadcast> Broadcasts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction holding a PostgreSQL advisory lock on <paramref name="lockKey"/>,
    /// so concurrent requests for the same key run one after another (check-then-insert without races).
    /// </summary>
    Task<T> InLockedTransactionAsync<T>(string lockKey, Func<Task<T>> work, CancellationToken ct);

    /// <summary>Explicit transaction for multi-step writes (checkout, payment callbacks, expiry).</summary>
    Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct);

    /// <summary>Transaction-scoped PostgreSQL advisory lock — must be called inside a transaction.</summary>
    Task LockAsync(string lockKey, CancellationToken ct);

    /// <summary>Raw parameterised SQL (conditional UPDATE / upsert) returning the affected row count.</summary>
    Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct);

    /// <summary>Forget tracked entities (after a rolled-back attempt).</summary>
    void ClearTracking();
}

public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
