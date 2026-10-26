using System.Text.Json;
using StackExchange.Redis;
using TradeSim.Application.MarketData;

namespace TradeSim.Infrastructure.MarketData;

public sealed class RedisPriceReader(IConnectionMultiplexer redis) : IPriceReader
{
    public async Task<IReadOnlyDictionary<string, PriceTick>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(RedisPricePublisher.PricesKey).WaitAsync(cancellationToken);
        return entries.ToDictionary(entry => entry.Name.ToString(), entry => Deserialize(entry.Value!));
    }

    public async Task<PriceTick?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().HashGetAsync(RedisPricePublisher.PricesKey, symbol).WaitAsync(cancellationToken);
        return value.HasValue ? Deserialize(value!) : null;
    }

    private static PriceTick Deserialize(string json) => JsonSerializer.Deserialize<PriceTick>(json, JsonSerializerOptions.Web)!;
}
