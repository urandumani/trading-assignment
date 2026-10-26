using Microsoft.Extensions.Options;
using TradeSim.Application.MarketData;
using TradeSim.Application.Orders;
using TradeSim.Domain.Market;

namespace TradeSim.Worker.MarketData;

public sealed class MarketDataEngine(
    IMarketDataStore store,
    IPricePublisher publisher,
    IServiceScopeFactory scopeFactory,
    MarketHours hours,
    TimeProvider clock,
    IOptions<MarketDataOptions> options,
    ILogger<MarketDataEngine> logger) : BackgroundService
{
    private readonly MarketDataOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var states = await LoadStatesAsync(stoppingToken);
        await PublishCurrentPricesAsync(states, stoppingToken);
        logger.LogInformation(
            "Market data engine started for {Count} instruments ({Mode})",
            states.Count, hours.AlwaysOpen ? "market always open" : "09:30-16:00 CEST, weekdays");

        try
        {
            await Task.WhenAll(states.Select(s => RunInstrumentAsync(s, stoppingToken)).Append(RunPersistenceAsync(states, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await PersistAsync(states, CancellationToken.None);
    }

    private async Task<IReadOnlyList<InstrumentState>> LoadStatesAsync(CancellationToken cancellationToken)
    {
        var instruments = await store.GetInstrumentsAsync(cancellationToken);
        var states = instruments.Select(i => new InstrumentState(i)).ToList();

        var today = hours.LocalDate(clock.GetUtcNow());
        var ranges = await store.GetDayRangesAsync(hours.StartOfDay(today), cancellationToken);
        foreach (var state in states)
        {
            state.StartDay(today, ranges.GetValueOrDefault(state.Id));
        }

        return states;
    }

    private async Task PublishCurrentPricesAsync(IReadOnlyList<InstrumentState> states, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        try
        {
            foreach (var state in states)
            {
                await publisher.PublishAsync(state.ToTick(now), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Publishing the starting prices failed; they will appear with the first ticks");
        }
    }

    private async Task RunInstrumentAsync(InstrumentState state, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(_options.MinTickMs, _options.MaxTickMs + 1));
            await Task.Delay(delay, clock, cancellationToken);

            try
            {
                await TickAsync(state, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Tick failed for {Symbol}", state.Symbol);
            }
        }
    }

    private async Task TickAsync(InstrumentState state, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!hours.IsOpen(now))
        {
            return;
        }

        PriceTick tick;
        bool tripped, resumed = false;
        decimal previousPrice;

        lock (state.Lock)
        {
            var today = hours.LocalDate(now);
            if (state.SessionDate != today)
            {
                state.StartDay(today, range: null);
            }

            if (state.HaltedUntil is { } haltedUntil)
            {
                if (now < haltedUntil)
                {
                    return;
                }

                state.HaltedUntil = null;
                resumed = true;
            }

            previousPrice = state.Price;
            var step = RandomWalk.Next(state.ExactPrice, state.MaxMovePct, _options.JumpChance, Random.Shared);
            var breaker = CircuitBreaker.Apply(state.ExactPrice, step.Price);
            tripped = breaker.Tripped;

            state.ExactPrice = breaker.Price;
            state.Price = RandomWalk.ToTick(breaker.Price);
            state.DayHigh = Math.Max(state.DayHigh, state.Price);
            state.DayLow = Math.Min(state.DayLow, state.Price);
            if (tripped)
            {
                state.HaltedUntil = now.AddSeconds(_options.HaltSeconds);
            }

            AddToCandle(state, now);
            tick = state.ToTick(now);
        }

        if (resumed)
        {
            logger.LogInformation("{Symbol} resumed trading at {Price}", state.Symbol, tick.Price);
        }

        if (tripped)
        {
            logger.LogWarning(
                "Circuit breaker: {Symbol} moved more than 10% in one tick ({Previous} -> capped at {Price}); halted until {HaltedUntil:O}",
                state.Symbol, previousPrice, tick.Price, tick.HaltedUntil);
        }

        await publisher.PublishAsync(tick, cancellationToken);

        if (!tick.Halted)
        {
            await FillTriggeredOrdersAsync(state, tick.Price, cancellationToken);
        }
    }

    private async Task FillTriggeredOrdersAsync(InstrumentState state, decimal price, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RestingOrderService>()
                .FillTriggeredAsync(state.Id, state.Symbol, price, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Filling triggered orders failed for {Symbol}", state.Symbol);
        }
    }

    private static void AddToCandle(InstrumentState state, DateTimeOffset now)
    {
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);

        if (state.CurrentCandle is { } candle && candle.Minute == minute)
        {
            candle.Add(state.Price);
        }
        else
        {
            if (state.CurrentCandle is not null)
            {
                state.CompletedCandles.Add(state.CurrentCandle);
            }

            state.CurrentCandle = PriceCandle.Start(state.Id, minute, state.Price);
        }

        state.HasUnsavedChanges = true;
    }

    private async Task RunPersistenceAsync(IReadOnlyList<InstrumentState> states, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PersistIntervalSeconds), clock);
        bool? wasOpen = null;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var isOpen = hours.IsOpen(clock.GetUtcNow());
            if (isOpen != wasOpen)
            {
                LogMarketStatus(isOpen);
                wasOpen = isOpen;
            }

            try
            {
                await PersistAsync(states, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Saving market data failed");
            }
        }
    }

    private async Task PersistAsync(IReadOnlyList<InstrumentState> states, CancellationToken cancellationToken)
    {
        var candles = new List<CandleData>();
        var lastPrices = new List<LastPrice>();
        var saved = new List<(InstrumentState State, int CompletedCount)>();

        foreach (var state in states)
        {
            lock (state.Lock)
            {
                if (!state.HasUnsavedChanges)
                {
                    continue;
                }

                candles.AddRange(state.CompletedCandles.Append(state.CurrentCandle!).Select(ToData));
                lastPrices.Add(new LastPrice(state.Id, state.Price));
                saved.Add((state, state.CompletedCandles.Count));
                state.HasUnsavedChanges = false;
            }
        }

        try
        {
            await store.SaveAsync(candles, lastPrices, cancellationToken);
        }
        catch
        {
            foreach (var (state, _) in saved)
            {
                lock (state.Lock)
                {
                    state.HasUnsavedChanges = true;
                }
            }

            throw;
        }

        foreach (var (state, completedCount) in saved)
        {
            lock (state.Lock)
            {
                state.CompletedCandles.RemoveRange(0, completedCount);
            }
        }
    }

    private void LogMarketStatus(bool isOpen)
    {
        if (isOpen)
        {
            logger.LogInformation("Market is open, generating ticks");
            return;
        }

        var next = hours.NextSession(clock.GetUtcNow());
        logger.LogInformation("Market is closed; next session opens {Open:O} ({Date:yyyy-MM-dd})", next.Open, next.Date);
    }

    private static CandleData ToData(PriceCandle c) => new(c.InstrumentId, c.Minute, c.Open, c.High, c.Low, c.Close);
}
