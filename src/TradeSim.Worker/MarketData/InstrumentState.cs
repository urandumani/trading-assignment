using TradeSim.Application.MarketData;
using TradeSim.Domain.Market;

namespace TradeSim.Worker.MarketData;

internal sealed class InstrumentState(InstrumentInfo info)
{
    public readonly Lock Lock = new();

    public Guid Id { get; } = info.Id;
    public string Symbol { get; } = info.Symbol;
    public decimal MaxMovePct { get; } = info.MaxMovePct;

    public decimal ExactPrice { get; set; } = info.LastPrice;

    public decimal Price { get; set; } = RandomWalk.ToTick(info.LastPrice);

    public DateOnly? SessionDate { get; set; }
    public decimal DayOpen { get; set; }
    public decimal DayHigh { get; set; }
    public decimal DayLow { get; set; }

    public DateTimeOffset? HaltedUntil { get; set; }

    public PriceCandle? CurrentCandle { get; set; }
    public List<PriceCandle> CompletedCandles { get; } = [];
    public bool HasUnsavedChanges { get; set; }

    public void StartDay(DateOnly date, DayRange? range)
    {
        SessionDate = date;
        DayOpen = RandomWalk.ToTick(range?.Open ?? Price);
        DayHigh = RandomWalk.ToTick(range?.High ?? Price);
        DayLow = RandomWalk.ToTick(range?.Low ?? Price);
    }

    public PriceTick ToTick(DateTimeOffset now) =>
        new(Symbol, Price, DayOpen, DayHigh, DayLow, Halted: HaltedUntil is not null, HaltedUntil, now);
}
