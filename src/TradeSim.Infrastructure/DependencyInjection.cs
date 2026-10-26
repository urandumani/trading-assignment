using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using TradeSim.Application.Abstractions;
using TradeSim.Application.MarketData;
using TradeSim.Domain.Market;
using TradeSim.Infrastructure.Health;
using TradeSim.Infrastructure.MarketData;
using TradeSim.Infrastructure.Outbox;
using TradeSim.Infrastructure.Messaging;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(configuration.GetRequiredConnectionString("Postgres")));
        services.AddDbContext<TradeSimDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<TradeSimDbContext>());
        services.AddScoped<IOutbox, EfOutbox>();

        var redisOptions = ConfigurationOptions.Parse(configuration.GetRequiredConnectionString("Redis"));
        redisOptions.AbortOnConnectFail = false;
        redisOptions.BacklogPolicy = BacklogPolicy.FailFast;
        redisOptions.AsyncTimeout = 1000;
        redisOptions.SyncTimeout = 1000;
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton(TimeProvider.System);

        var market = configuration.GetSection(MarketOptions.SectionName).Get<MarketOptions>() ?? new MarketOptions();
        var timeZone = market.TimeZone is { } id ? TimeZoneInfo.FindSystemTimeZoneById(id) : MarketHours.Cest;
        services.AddSingleton(new MarketHours(timeZone, market.AlwaysOpen));
        services.AddRedisResilience();
        services.AddSingleton<RedisPricePublisher>();
        services.AddSingleton<IPricePublisher, ResilientPricePublisher>();
        services.AddSingleton<RedisPriceReader>();
        services.AddSingleton<IPriceReader, ResilientPriceReader>();
        services.AddSingleton<IMarketDataStore, MarketDataStore>();

        return services;
    }

    public static IHealthChecksBuilder AddInfrastructureChecks(this IHealthChecksBuilder builder)
    {
        var timeout = TimeSpan.FromSeconds(3);
        return builder
            .AddCheck<PostgresHealthCheck>("postgres", timeout: timeout)
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded, timeout: timeout)
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded, timeout: timeout);
    }
}
