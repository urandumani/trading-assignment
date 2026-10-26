using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.MarketData;

public sealed record MarketSnapshot(
    bool MarketOpen,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? NextOpen,
    DateTimeOffset AsOf,
    IReadOnlyList<InstrumentQuote> Instruments);

public sealed record InstrumentQuote(
    string Symbol,
    string Name,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    decimal DayOpen,
    decimal DayHigh,
    decimal DayLow,
    bool Halted,
    DateTimeOffset? HaltedUntil,
    DateTimeOffset? UpdatedAt,
    bool Stale);

public sealed record InstrumentSummary(string Symbol, string Name, string Volatility, decimal MaxMovePercent, int MinQuantity);

public sealed record PriceHistory(string Symbol, string Interval, IReadOnlyList<Candle> Candles);

public sealed record Candle(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close);

public sealed record OrderBookView(
    string Symbol,
    decimal Price,
    decimal BestBid,
    decimal BestAsk,
    decimal Spread,
    DateTimeOffset AsOf,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);

public sealed class MarketDataQueries(
    IAppDbContext db,
    InstrumentCatalog catalog,
    IPriceReader prices,
    MarketHours hours,
    TimeProvider clock)
{
    public const int MaxCandles = 1000;
    public const int MaxDepth = 50;

    public async Task<MarketSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var instruments = await catalog.GetAllAsync(cancellationToken);
        var latest = await prices.GetAllAsync(cancellationToken);

        var isOpen = hours.IsOpen(now);
        var quotes = instruments.Select(i => ToQuote(i, latest.GetValueOrDefault(i.Symbol), now, isOpen)).ToList();

        var closesAt = hours.AlwaysOpen ? null : hours.SessionContaining(now)?.Close;
        DateTimeOffset? nextOpen = isOpen ? null : hours.NextSession(now).Open;

        return new MarketSnapshot(isOpen, closesAt, nextOpen, now, quotes);
    }

    public async Task<IReadOnlyList<InstrumentSummary>> GetInstrumentsAsync(CancellationToken cancellationToken = default) =>
        (await catalog.GetAllAsync(cancellationToken))
            .Select(i => new InstrumentSummary(i.Symbol, i.Name, i.Volatility, i.MaxMovePct, i.MinQuantity))
            .ToList();

    public async Task<PriceHistory> GetPriceHistoryAsync(
        string symbol, string interval, DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken cancellationToken = default)
    {
        var step = CandleInterval.Parse(interval);
        if (limit is < 1 or > MaxCandles)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"limit must be between 1 and {MaxCandles}.");
        }

        var end = (to ?? clock.GetUtcNow()).ToUniversalTime();
        var start = (from ?? DateTimeOffset.UnixEpoch).ToUniversalTime();
        if (start >= end)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "from must be earlier than to.");
        }

        var instrument = await catalog.GetAsync(symbol, cancellationToken);

        var rows = await db.Database.SqlQuery<CandleRow>($"""
            SELECT date_bin({step}, minute, TIMESTAMPTZ '2000-01-01') AS bucket,
                   (array_agg(open ORDER BY minute))[1]       AS open,
                   max(high)                                   AS high,
                   min(low)                                    AS low,
                   (array_agg(close ORDER BY minute DESC))[1] AS close
            FROM price_candles
            WHERE instrument_id = {instrument.Id} AND minute >= {start} AND minute < {end}
            GROUP BY bucket
            ORDER BY bucket DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

        var candles = rows
            .OrderBy(r => r.Bucket)
            .Select(r => new Candle(r.Bucket, Round(r.Open), Round(r.High), Round(r.Low), Round(r.Close)))
            .ToList();

        return new PriceHistory(instrument.Symbol, interval.ToLowerInvariant(), candles);
    }

    public async Task<OrderBookView> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default)
    {
        if (depth is < 1 or > MaxDepth)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"depth must be between 1 and {MaxDepth}.");
        }

        var instrument = await catalog.GetAsync(symbol, cancellationToken);
        var now = clock.GetUtcNow();
        var quote = ToQuote(instrument, await prices.GetAsync(instrument.Symbol, cancellationToken), now, hours.IsOpen(now));

        var resting = await db.Orders
            .AsNoTracking()
            .Where(o => o.InstrumentId == instrument.Id && o.Status == OrderStatus.Open && o.Type == OrderType.Limit)
            .GroupBy(o => new { o.Side, o.LimitPrice })
            .Select(g => new { g.Key.Side, Price = g.Key.LimitPrice!.Value, Quantity = g.Sum(o => o.Quantity) })
            .ToListAsync(cancellationToken);

        var seed = (int)(now.ToUnixTimeSeconds() % int.MaxValue) ^ instrument.Symbol.Aggregate(17, (hash, c) => hash * 31 + c);
        var book = OrderBookBuilder.Build(
            quote.Price,
            depth,
            resting.Select(r => new RestingOrders(r.Side == OrderSide.Buy, r.Price, r.Quantity)),
            seed);

        return new OrderBookView(instrument.Symbol, book.Price, book.BestBid, book.BestAsk, book.BestAsk - book.BestBid, now, book.Bids, book.Asks);
    }

    private static InstrumentQuote ToQuote(InstrumentDetails instrument, PriceTick? tick, DateTimeOffset now, bool marketOpen)
    {
        if (tick is null || tick.FromFallback)
        {
            var price = tick?.Price ?? RandomWalk.ToTick(instrument.LastSavedPrice);
            return new InstrumentQuote(instrument.Symbol, instrument.Name, price, 0m, 0m, price, price, price,
                Halted: false, HaltedUntil: null, UpdatedAt: null, Stale: true);
        }

        var change = tick.Price - tick.DayOpen;
        var changePercent = tick.DayOpen == 0 ? 0 : Math.Round(change / tick.DayOpen * 100, 2);
        return new InstrumentQuote(
            instrument.Symbol, instrument.Name, tick.Price, change, changePercent,
            tick.DayOpen, tick.DayHigh, tick.DayLow, tick.Halted, tick.HaltedUntil, tick.Timestamp,
            Stale: marketOpen && !tick.IsLive(now));
    }

    private static decimal Round(decimal price) => Math.Round(price, 2);

    internal sealed record CandleRow(DateTimeOffset Bucket, decimal Open, decimal High, decimal Low, decimal Close);
}
