using System.Text.Json;
using StackExchange.Redis;
using TradeSim.Application.MarketData;

namespace TradeSim.Infrastructure.MarketData;

public sealed class RedisPricePublisher(IConnectionMultiplexer redis) : IPricePublisher
{
    public const string PricesKey = "prices:last";
    public static readonly RedisChannel TicksChannel = RedisChannel.Literal("ticks");

    public async Task PublishAsync(PriceTick tick, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(tick, JsonSerializerOptions.Web);

        await redis.GetDatabase().HashSetAsync(PricesKey, tick.Symbol, json).WaitAsync(cancellationToken);
        await redis.GetSubscriber().PublishAsync(TicksChannel, json).WaitAsync(cancellationToken);
    }
}
