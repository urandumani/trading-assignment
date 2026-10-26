using Npgsql;
using Polly;
using Polly.Registry;
using TradeSim.Application.MarketData;
using TradeSim.Domain.Market;

namespace TradeSim.Infrastructure.MarketData;

public sealed class ResilientPriceReader(
    RedisPriceReader redis,
    NpgsqlDataSource database,
    ResiliencePipelineProvider<string> pipelines) : IPriceReader
{
    private readonly ResiliencePipeline _pipeline = pipelines.GetPipeline(RedisResilience.PipelineName);

    public async Task<IReadOnlyDictionary<string, PriceTick>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async token => await redis.GetAllAsync(token), cancellationToken);
        }
        catch (Exception ex) when (RedisResilience.IsRedisUnavailable(ex))
        {
            return await ReadSavedPricesAsync(symbol: null, cancellationToken);
        }
    }

    public async Task<PriceTick?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async token => await redis.GetAsync(symbol, token), cancellationToken);
        }
        catch (Exception ex) when (RedisResilience.IsRedisUnavailable(ex))
        {
            return (await ReadSavedPricesAsync(symbol, cancellationToken)).GetValueOrDefault(symbol);
        }
    }

    private async Task<IReadOnlyDictionary<string, PriceTick>> ReadSavedPricesAsync(string? symbol, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(
            "SELECT symbol, last_price FROM instruments WHERE $1::text IS NULL OR symbol = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)symbol ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var prices = new Dictionary<string, PriceTick>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var price = RandomWalk.ToTick(reader.GetDecimal(1));
            prices[reader.GetString(0)] = new PriceTick(reader.GetString(0), price, price, price, price,
                Halted: false, HaltedUntil: null, Timestamp: DateTimeOffset.UnixEpoch, FromFallback: true);
        }

        return prices;
    }
}
