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
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<Identity.OtpService>();
        services.AddScoped<Identity.SessionService>();
        services.AddScoped<Features.Seller.SellerAccess>();
        services.AddScoped<Features.Seller.ProductWriter>();

        services.AddScoped<Features.Cart.CartStore>();
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
        return services;
    }
}
