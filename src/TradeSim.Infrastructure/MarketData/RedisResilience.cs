using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using StackExchange.Redis;

namespace TradeSim.Infrastructure.MarketData;

public static class RedisResilience
{
    public const string PipelineName = "redis";

    private static readonly PredicateBuilder<object> RedisFailures = new PredicateBuilder()
        .Handle<RedisException>()
        .Handle<RedisTimeoutException>()
        .Handle<TimeoutRejectedException>()
        .Handle<TimeoutException>();

    public static IServiceCollection AddRedisResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline(PipelineName, (pipeline, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RedisResilience));

            pipeline
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = RedisFailures,
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = RedisFailures,
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    BreakDuration = TimeSpan.FromSeconds(15),
                    OnOpened = args =>
                    {
                        logger.LogWarning("Redis circuit opened for {Break}: using fallbacks ({Error})",
                            args.BreakDuration, args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("Redis circuit closed: Redis is reachable again");
                        return ValueTask.CompletedTask;
                    },
                })
                .AddTimeout(TimeSpan.FromMilliseconds(500));
        });

        return services;
    }

    public static bool IsRedisUnavailable(Exception exception) =>
        exception is RedisException or TimeoutException or TimeoutRejectedException or BrokenCircuitException;
}
