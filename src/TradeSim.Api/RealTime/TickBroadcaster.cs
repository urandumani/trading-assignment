using StackExchange.Redis;
using TradeSim.Infrastructure.MarketData;

namespace TradeSim.Api.RealTime;

public sealed class TickBroadcaster(IConnectionMultiplexer redis, WebSocketHub hub, ILogger<TickBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await subscriber.SubscribeAsync(RedisPricePublisher.TicksChannel, (_, tick) => Broadcast(tick));
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Can't subscribe to ticks yet ({Error}); retrying in {Delay}", ex.Message, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await subscriber.UnsubscribeAsync(RedisPricePublisher.TicksChannel);
        }
    }

    private void Broadcast(RedisValue tick)
    {
        try
        {
            hub.BroadcastTick(tick!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ignoring a malformed tick");
        }
    }
}
