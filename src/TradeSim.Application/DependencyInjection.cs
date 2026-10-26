using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TradeSim.Application.Auth;
using TradeSim.Application.Common;
using TradeSim.Application.MarketData;
using TradeSim.Application.Orders;
using TradeSim.Application.Portfolio;
using TradeSim.Application.Users;
using TradeSim.Domain.Users;

namespace TradeSim.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<LoginService>();
        services.AddScoped<UserProfileQuery>();

        services.AddMemoryCache();
        services.AddSingleton<InstrumentCatalog>();
        services.AddScoped<MarketDataQueries>();
        services.AddScoped<PriceBook>();

        services.AddSingleton<TradingMetrics>();
        services.AddScoped<PlaceOrderService>();
        services.AddScoped<CancelOrderService>();
        services.AddScoped<RestingOrderService>();
        services.AddScoped<OrderQueries>();
        services.AddScoped<PortfolioQueries>();
        services.AddScoped<PortfolioSnapshotService>();
        return services;
    }
}
