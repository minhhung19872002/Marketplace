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
        return services;
    }
}
