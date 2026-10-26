using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using TradeSim.Application.MarketData;

namespace TradeSim.Infrastructure.MarketData;

public sealed class ResilientPricePublisher(RedisPricePublisher redis, ResiliencePipelineProvider<string> pipelines) : IPricePublisher
{
    private readonly ResiliencePipeline _pipeline = pipelines.GetPipeline(RedisResilience.PipelineName);

    public async Task PublishAsync(PriceTick tick, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(async token => await redis.PublishAsync(tick, token), cancellationToken);
        }
        catch (BrokenCircuitException)
        {
        }
    }
}
