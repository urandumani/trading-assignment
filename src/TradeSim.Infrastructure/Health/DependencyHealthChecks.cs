using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using TradeSim.Infrastructure.Messaging;

namespace TradeSim.Infrastructure.Health;

public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await redis.GetDatabase().PingAsync();
        return HealthCheckResult.Healthy();
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConnection rabbitMq) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await rabbitMq.GetAsync(cancellationToken);
        return connection.IsOpen
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Connection is closed or recovering.");
    }
}
