using Npgsql;
using NpgsqlTypes;
using TradeSim.Application.MarketData;

namespace TradeSim.Infrastructure.MarketData;

public sealed class MarketDataStore(NpgsqlDataSource dataSource) : IMarketDataStore
{
    public async Task<IReadOnlyList<InstrumentInfo>> GetInstrumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT id, symbol, max_move_pct, last_price FROM instruments ORDER BY symbol");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var instruments = new List<InstrumentInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            instruments.Add(new InstrumentInfo(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        }

        return instruments;
    }

    public async Task<IReadOnlyDictionary<Guid, DayRange>> GetDayRangesAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT DISTINCT ON (instrument_id)
                   instrument_id,
                   open,
                   max(high) OVER (PARTITION BY instrument_id),
                   min(low)  OVER (PARTITION BY instrument_id)
            FROM price_candles
            WHERE minute >= $1
            ORDER BY instrument_id, minute
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = since.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var ranges = new Dictionary<Guid, DayRange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ranges[reader.GetGuid(0)] = new DayRange(reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
        }

        return ranges;
    }

    public async Task SaveAsync(IReadOnlyList<CandleData> candles, IReadOnlyList<LastPrice> lastPrices, CancellationToken cancellationToken = default)
    {
        if (candles.Count == 0 && lastPrices.Count == 0)
        {
            return;
        }

        await using var batch = dataSource.CreateBatch();

        foreach (var candle in candles)
        {
            var command = new NpgsqlBatchCommand("""
                INSERT INTO price_candles (instrument_id, minute, open, high, low, close)
                VALUES ($1, $2, $3, $4, $5, $6)
                ON CONFLICT (instrument_id, minute) DO UPDATE
                SET high  = GREATEST(price_candles.high, EXCLUDED.high),
                    low   = LEAST(price_candles.low, EXCLUDED.low),
                    close = EXCLUDED.close
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = candle.InstrumentId });
            command.Parameters.Add(new NpgsqlParameter { Value = candle.Minute.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            command.Parameters.Add(new NpgsqlParameter { Value = candle.Open });
            command.Parameters.Add(new NpgsqlParameter { Value = candle.High });
            command.Parameters.Add(new NpgsqlParameter { Value = candle.Low });
            command.Parameters.Add(new NpgsqlParameter { Value = candle.Close });
            batch.BatchCommands.Add(command);
        }

        foreach (var lastPrice in lastPrices)
        {
            var command = new NpgsqlBatchCommand("UPDATE instruments SET last_price = $1 WHERE id = $2");
            command.Parameters.Add(new NpgsqlParameter { Value = lastPrice.Price });
            command.Parameters.Add(new NpgsqlParameter { Value = lastPrice.InstrumentId });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }
}
