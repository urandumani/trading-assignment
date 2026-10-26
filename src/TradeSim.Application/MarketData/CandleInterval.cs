using TradeSim.Domain;

namespace TradeSim.Application.MarketData;

public static class CandleInterval
{
    private static readonly Dictionary<string, TimeSpan> Intervals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1m"] = TimeSpan.FromMinutes(1),
        ["5m"] = TimeSpan.FromMinutes(5),
        ["15m"] = TimeSpan.FromMinutes(15),
        ["1h"] = TimeSpan.FromHours(1),
        ["1d"] = TimeSpan.FromDays(1),
    };

    public static TimeSpan Parse(string interval) =>
        Intervals.TryGetValue(interval, out var span)
            ? span
            : throw new DomainException(ErrorCodes.ValidationFailed, $"interval must be one of: {string.Join(", ", Intervals.Keys)}.");
}
