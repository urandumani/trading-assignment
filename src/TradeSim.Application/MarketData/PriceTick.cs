namespace TradeSim.Application.MarketData;

public sealed record PriceTick(
    string Symbol,
    decimal Price,
    decimal DayOpen,
    decimal DayHigh,
    decimal DayLow,
    bool Halted,
    DateTimeOffset? HaltedUntil,
    DateTimeOffset Timestamp,
    bool FromFallback = false)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);

    public bool IsLive(DateTimeOffset now) => !FromFallback && now - Timestamp <= MaxAge;
}

public interface IPricePublisher
{
    Task PublishAsync(PriceTick tick, CancellationToken cancellationToken = default);
}
