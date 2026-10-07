using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Media;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

using ShopHub.Domain.Sales;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Logistics;
namespace ShopHub.Infrastructure.Persistence;

public class ShopHubDbContext(DbContextOptions<ShopHubDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<AdminDivision> AdminDivisions => Set<AdminDivision>();
    public DbSet<SimulatedSms> SimulatedSms => Set<SimulatedSms>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sku> Skus => Set<Sku>();
    public DbSet<VariantTier> VariantTiers => Set<VariantTier>();
    public DbSet<VariantOption> VariantOptions => Set<VariantOption>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopKyc> ShopKycs => Set<ShopKyc>();
    public DbSet<ShopWarehouse> ShopWarehouses => Set<ShopWarehouse>();
    public DbSet<ShopStaff> ShopStaff => Set<ShopStaff>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<ShopCategory> ShopCategories => Set<ShopCategory>();
    public DbSet<ShopCategoryProduct> ShopCategoryProducts => Set<ShopCategoryProduct>();
    public DbSet<ShopDecoration> ShopDecorations => Set<ShopDecoration>();
    public DbSet<ShopBankAccount> ShopBankAccounts => Set<ShopBankAccount>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<ShopFollower> ShopFollowers => Set<ShopFollower>();
    public DbSet<ProductView> ProductViews => Set<ProductView>();
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderPackage> OrderPackages => Set<OrderPackage>();
    public DbSet<ShopShippingChannel> ShopShippingChannels => Set<ShopShippingChannel>();
    public DbSet<Domain.Promo.CampaignRegistration> CampaignRegistrations => Set<Domain.Promo.CampaignRegistration>();
    public DbSet<OrderItemDiscount> OrderItemDiscounts => Set<OrderItemDiscount>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherClaim> VoucherClaims => Set<VoucherClaim>();
    public DbSet<VoucherUsage> VoucherUsages => Set<VoucherUsage>();
    public DbSet<VoucherUserCounter> VoucherUserCounters => Set<VoucherUserCounter>();
    public DbSet<CoinEntry> CoinLedger => Set<CoinEntry>();
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<ShippingRate> ShippingRates => Set<ShippingRate>();
    public DbSet<SimulatedPayment> SimulatedPayments => Set<SimulatedPayment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<OrderCancelRequest> OrderCancelRequests => Set<OrderCancelRequest>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ShopPenalty> ShopPenalties => Set<ShopPenalty>();
    public DbSet<Domain.Engage.CartAdd> CartAdds => Set<Domain.Engage.CartAdd>();
    public DbSet<Domain.Catalog.ProductReport> ProductReports => Set<Domain.Catalog.ProductReport>();
    public DbSet<Domain.SystemConfig.CmsPage> CmsPages => Set<Domain.SystemConfig.CmsPage>();
    public DbSet<Domain.SystemConfig.MessageTemplate> MessageTemplates => Set<Domain.SystemConfig.MessageTemplate>();
    public DbSet<Domain.SystemConfig.BackgroundTask> BackgroundTasks => Set<Domain.SystemConfig.BackgroundTask>();
    public DbSet<Domain.SystemConfig.BackupRun> BackupRuns => Set<Domain.SystemConfig.BackupRun>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewMedia> ReviewMedia => Set<ReviewMedia>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<ReturnEvidence> ReturnEvidence => Set<ReturnEvidence>();
    public DbSet<ReturnHistory> ReturnHistory => Set<ReturnHistory>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<FeeRule> FeeRules => Set<FeeRule>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementItem> SettlementItems => Set<SettlementItem>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTopup> WalletTopups => Set<WalletTopup>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionProduct> PromotionProducts => Set<PromotionProduct>();
    public DbSet<PromotionSku> PromotionSkus => Set<PromotionSku>();
    public DbSet<PriceProgram> PricePrograms => Set<PriceProgram>();
    public DbSet<FlashSaleSlot> FlashSaleSlots => Set<FlashSaleSlot>();
    public DbSet<FlashSaleItem> FlashSaleItems => Set<FlashSaleItem>();
    public DbSet<FlashSaleBuyer> FlashSaleBuyers => Set<FlashSaleBuyer>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<QuickReply> QuickReplies => Set<QuickReply>();
    public DbSet<ShopChatSettings> ShopChatSettings => Set<ShopChatSettings>();
    public DbSet<ChatReport> ChatReports => Set<ChatReport>();
    public DbSet<NotificationPref> NotificationPrefs => Set<NotificationPref>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();

    // Unique index name → what the user is told when a parallel request already took the value
    private static readonly Dictionary<string, string> UniqueMessages = new()
    {
        ["ux_users_phone"] = "Số điện thoại đã được đăng ký.",
        ["ux_users_email"] = "Email đã được đăng ký.",
        ["ux_users_username"] = "Tên đăng nhập đã được sử dụng.",
        ["ux_roles_code"] = "Mã vai trò đã tồn tại.",
        ["ux_addresses_default"] = "Chỉ có một địa chỉ mặc định. Vui lòng thử lại.",
        ["ux_shops_slug"] = "Tên shop đã được sử dụng.",
        ["ux_shops_name"] = "Tên shop đã được sử dụng.",
        ["ux_shop_staff_shop_user"] = "Người này đã là nhân viên của shop.",
        ["ux_brands_slug"] = "Thương hiệu đã tồn tại.",
        ["ux_categories_slug"] = "Đã có danh mục trùng tên.",
        ["ux_wishlists_user_product"] = "Sản phẩm đã có trong danh sách yêu thích.",
        ["ux_shop_followers_shop_user"] = "Bạn đã theo dõi shop này.",
        ["ux_variant_options_tier_value"] = "Phân loại có lựa chọn bị trùng.",
        ["ux_voucher_code"] = "Mã voucher đã tồn tại.",
        ["ux_voucher_claims"] = "Bạn đã lưu voucher này.",
        ["ux_cart_items_cart_sku"] = "Sản phẩm đã có trong giỏ, vui lòng thử lại.",
        ["ux_carts_user"] = "Giỏ hàng đang được cập nhật, vui lòng thử lại.",
        ["ux_carts_guest"] = "Giỏ hàng đang được cập nhật, vui lòng thử lại.",
        ["ux_cancel_requests_open"] = "Đơn hàng đã có yêu cầu huỷ đang chờ shop xử lý.",
        ["ux_shipments_tracking"] = "Mã vận đơn bị trùng, vui lòng thử lại.",
        ["ux_review_item"] = "Sản phẩm này của đơn đã được đánh giá.",
        ["ux_review_reports"] = "Bạn đã báo cáo đánh giá này.",
        ["ux_return_items_open"] = "Sản phẩm này đang có yêu cầu trả hàng chưa xử lý xong.",
        ["ux_returns_code"] = "Mã yêu cầu trả hàng bị trùng, vui lòng thử lại.",
        ["ux_disputes_return"] = "Yêu cầu trả hàng này đã được khiếu nại.",
        ["ux_ledger_accounts_owner"] = "Tài khoản sổ cái đang được tạo, vui lòng thử lại.",
        ["ux_ledger_transactions_dedupe"] = "Bút toán này đã được ghi.",
        ["ux_fee_rules_open"] = "Đã có biểu phí đang áp dụng cho phạm vi này.",
        ["ux_settlement_items_order"] = "Đơn hàng này đã được giải ngân.",
        ["ux_settlements_code"] = "Mã kỳ giải ngân bị trùng, vui lòng thử lại.",
        ["ux_wallets_user"] = "Ví ShopHub của bạn đã được tạo.",
        ["ux_promotion_skus"] = "Phân loại này đã có trong chương trình.",
        ["ux_promotion_products"] = "Sản phẩm này đã có trong chương trình.",
        ["ux_flash_sale_items_slot_sku"] = "Phân loại này đã đăng ký khung Flash Sale này.",
        ["ux_campaigns_slug"] = "Đường dẫn chiến dịch đã được dùng.",
        ["ux_check_ins_user_day"] = "Hôm nay bạn đã điểm danh rồi.",
        ["ux_conversations_pair"] = "Cuộc trò chuyện đang được tạo, vui lòng thử lại.",
        ["ux_quick_replies_shortcut"] = "Phím tắt này đã được dùng.",
        ["ux_shop_chat_settings_shop"] = "Cài đặt chat đang được lưu, vui lòng thử lại.",
        ["ux_notification_prefs"] = "Cài đặt thông báo đang được lưu, vui lòng thử lại.",
    };

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } ck)
        {
            throw new ConflictException(ck.ConstraintName switch
            {
                "ck_skus_stock" or "ck_skus_reserved" => "Tồn kho không đủ hoặc thấp hơn số đang giữ cho đơn.",
                "ck_vouchers_quota" => "Voucher đã hết lượt sử dụng.",
                "ck_ledger_accounts_balance" => "Số dư không đủ.",
                "ck_flash_sale_items_sold" => "Suất Flash Sale đã hết.",
                _ => "Dữ liệu vi phạm ràng buộc, vui lòng kiểm tra lại.",
            }, "CHECK_VIOLATION");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } ex2)
        {
            throw new ConflictException(ex2.ConstraintName == "ex_price_programs_sku_period"
                ? "Có phân loại đang nằm trong một chương trình giá khác (giảm giá / Flash Sale) trùng thời gian."
                : "Dữ liệu trùng thời gian với bản ghi khác.", "EXCLUSION_VIOLATION") { Constraint = ex2.ConstraintName };
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new ConflictException(
                pg.ConstraintName is { } name && UniqueMessages.TryGetValue(name, out var message) ? message : "Dữ liệu bị trùng, vui lòng kiểm tra lại.",
                "UNIQUE_VIOLATION") { Constraint = pg.ConstraintName };
        }
    }

    public async Task<T> InLockedTransactionAsync<T>(string lockKey, Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        await LockAsync(lockKey, ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct) => new AppTransaction(await Database.BeginTransactionAsync(ct));

    public bool InTransaction => Database.CurrentTransaction is not null;

    public Task LockAsync(string lockKey, CancellationToken ct) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

    public Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct) => Database.ExecuteSqlInterpolatedAsync(sql, ct);

    public void ClearTracking() => ChangeTracker.Clear();

    public async Task<int> SaveOwnChangesAsync(CancellationToken ct, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < attempts)
            {
                foreach (var entry in ex.Entries)
                {
                    var current = await entry.GetDatabaseValuesAsync(ct);
                    if (current is null) throw;   // the row is gone: a real conflict
                    entry.OriginalValues.SetValues(current);
                }
                // The audit rows of the failed try would be written twice: the next try writes them again
                foreach (var log in ChangeTracker.Entries<Domain.Iam.AuditLog>().Where(e => e.State == EntityState.Added).ToList()) log.State = EntityState.Detached;
            }
        }
    }

    private sealed class AppTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct) => inner.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct) => inner.RollbackAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopHubDbContext).Assembly);

        modelBuilder.HasDbFunction(typeof(Search.SearchFunctions).GetMethod(nameof(Search.SearchFunctions.Unaccent))!)
            .HasName("immutable_unaccent").HasSchema("public");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Ids are generated client-side (Entity ctor) so EF must treat a new child found in a collection as Added,
            // never as an existing row to UPDATE; the DB default only serves inserts made outside EF (SQL scripts)
            if (typeof(Entity).IsAssignableFrom(clrType))
                modelBuilder.Entity(clrType).Property(nameof(Entity.Id)).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedNever();

            // Soft-deleted rows disappear from every query unless IgnoreQueryFilters() is used
            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var param = Expression.Parameter(clrType, "e");
                var body = Expression.Equal(
                    Expression.Property(param, nameof(ISoftDeletable.DeletedAt)),
                    Expression.Constant(null, typeof(DateTimeOffset?)));
                modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body, param));
            }
        }
    }
}
