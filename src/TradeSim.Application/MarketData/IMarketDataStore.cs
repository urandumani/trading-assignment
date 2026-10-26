namespace TradeSim.Application.MarketData;

public interface IMarketDataStore
{
    Task<IReadOnlyList<InstrumentInfo>> GetInstrumentsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DayRange>> GetDayRangesAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<CandleData> candles, IReadOnlyList<LastPrice> lastPrices, CancellationToken cancellationToken = default);
}

public sealed record InstrumentInfo(Guid Id, string Symbol, decimal MaxMovePct, decimal LastPrice);

public sealed record DayRange(decimal Open, decimal High, decimal Low);

public sealed record CandleData(Guid InstrumentId, DateTimeOffset Minute, decimal Open, decimal High, decimal Low, decimal Close);

public sealed record LastPrice(Guid InstrumentId, decimal Price);
