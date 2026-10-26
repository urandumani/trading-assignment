namespace TradeSim.Domain.Market;

public sealed class PriceCandle
{
    private PriceCandle() { }

    public Guid InstrumentId { get; private set; }

    public DateTimeOffset Minute { get; private set; }

    public decimal Open { get; private set; }
    public decimal High { get; private set; }
    public decimal Low { get; private set; }
    public decimal Close { get; private set; }

    public static PriceCandle Start(Guid instrumentId, DateTimeOffset minute, decimal price) => new()
    {
        InstrumentId = instrumentId,
        Minute = minute,
        Open = price,
        High = price,
        Low = price,
        Close = price,
    };

    public void Add(decimal price)
    {
        High = Math.Max(High, price);
        Low = Math.Min(Low, price);
        Close = price;
    }
}
