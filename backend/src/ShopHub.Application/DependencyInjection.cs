using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Common;

namespace ShopHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        // A rule stops at its first failure: NotNull() then guards the Must(...) after it (no 500 on a null list)
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<Identity.OtpService>();
        services.AddScoped<Identity.SessionService>();
        services.AddScoped<Features.Seller.SellerAccess>();
        services.AddScoped<Features.Seller.ProductWriter>();

        services.AddScoped<Features.Cart.CartStore>();
        services.AddScoped<Features.Cart.PurchaseLimits>();
        services.AddScoped<Features.Checkout.ShippingCalculator>();
        services.AddScoped<Features.Checkout.VoucherEvaluator>();
        services.AddScoped<Features.Checkout.VoucherLedger>();
        services.AddScoped<Features.Checkout.CoinWallet>();
        services.AddScoped<Features.Checkout.CheckoutBuilder>();
        services.AddScoped<Features.Checkout.CheckoutReader>();
        services.AddScoped<Features.Checkout.PaymentStarter>();
        services.AddScoped<Features.Payments.PaymentProcessor>();
        services.AddScoped<Features.Payments.CheckoutReleaser>();
        services.AddScoped<Features.Payments.PaymentExpiryService>();
        services.AddScoped<Features.Payments.PaymentWebhookIntake>();
        services.AddScoped<Features.Orders.OrderLocks>();
        services.AddScoped<Features.Orders.OrderCanceller>();
        services.AddScoped<Features.Orders.ShipmentEventProcessor>();
        services.AddScoped<Features.Orders.CarrierWebhookIntake>();
        services.AddScoped<Features.Orders.OrderAutomationService>();
        services.AddScoped<Features.Orders.CarrierSyncService>();
        services.AddScoped<Features.Seller.ShopPenaltyService>();
        services.AddScoped<Features.Reports.AdminReports>();
        services.AddScoped<Features.Reports.SellerAnalytics>();
        services.AddScoped<Features.Admin.MessageTemplates>();
        services.AddScoped<Abstractions.IAccountDeletionGuard, Features.Account.OpenOrdersDeletionGuard>();
        services.AddScoped<Abstractions.IAccountDeletionGuard, Features.Account.WalletBalanceDeletionGuard>();
        services.AddScoped<Abstractions.IAccountDeletionGuard, Features.Account.ShopOwnerDeletionGuard>();
        services.AddScoped<Features.Reviews.ReviewRewards>();
        services.AddScoped<Features.Returns.ReturnRefunder>();
        services.AddScoped<Features.Returns.ReturnAutomationService>();
        services.AddScoped<Features.Finance.Ledger>();
        services.AddScoped<Features.Finance.FeeSchedule>();
        services.AddScoped<Features.Finance.OrderLedger>();
        services.AddScoped<Features.Finance.SettlementService>();
        services.AddScoped<Features.Finance.LedgerCheckService>();
        services.AddScoped<Features.Finance.WithdrawalService>();
        services.AddScoped<Features.Finance.WalletPins>();
        services.AddScoped<Features.Finance.TopupProcessor>();
        services.AddScoped<Features.Marketing.PriceBook>();
        services.AddScoped<Features.Storefront.CardPricing>();
        services.AddScoped<Features.Marketing.DealsBook>();
        services.AddScoped<Features.Marketing.FlashSaleQuota>();
        services.AddScoped<Features.Marketing.FlashSaleReconciler>();
        services.AddScoped<Features.Marketing.Membership>();
        services.AddScoped<Features.Marketing.CashbackService>();
        services.AddScoped<Features.Marketing.CoinExpiryService>();
        services.AddScoped<Features.Chat.ContactFilter>();
        services.AddScoped<Features.Chat.ChatAccess>();
        services.AddScoped<Features.Chat.ChatTyping>();
        services.AddScoped<Features.Chat.ChatPerformanceService>();
        services.AddScoped<Features.Chat.ReminderService>();
        return services;
    }
}
