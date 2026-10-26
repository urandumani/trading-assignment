namespace TradeSim.Domain.Market;

public sealed class MarketHours(TimeZoneInfo timeZone, bool alwaysOpen = false)
{
    public static readonly TimeOnly OpenTime = new(9, 30);
    public static readonly TimeOnly CloseTime = new(16, 0);

    public static readonly TimeZoneInfo Cest = TimeZoneInfo.CreateCustomTimeZone(
        "CEST", TimeSpan.FromHours(2), "Central European Summer Time (UTC+02:00)", "CEST");

    public bool AlwaysOpen => alwaysOpen;

    public bool IsOpen(DateTimeOffset now) => alwaysOpen || SessionContaining(now) is not null;

    public MarketSession? SessionContaining(DateTimeOffset now)
    {
        var date = LocalDate(now);
        if (!IsTradingDay(date))
        {
            return null;
        }

        var session = SessionOn(date);
        return now >= session.Open && now < session.Close ? session : null;
    }

    public MarketSession NextSession(DateTimeOffset now)
    {
        var date = LocalDate(now);
        while (true)
        {
            if (IsTradingDay(date) && SessionOn(date).Open > now)
            {
                return SessionOn(date);
            }

            date = date.AddDays(1);
        }
    }

    public IReadOnlyList<MarketSession> PreviousSessions(DateTimeOffset now, int count)
    {
        var sessions = new List<MarketSession>(count);
        var date = LocalDate(now);
        while (sessions.Count < count)
        {
            if (IsTradingDay(date) && SessionOn(date).Close <= now)
            {
                sessions.Add(SessionOn(date));
            }

            date = date.AddDays(-1);
        }

        sessions.Reverse();
        return sessions;
    }

    public DateOnly LocalDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

    public MarketSession SessionOn(DateOnly date) => new(date, ToInstant(date, OpenTime), ToInstant(date, CloseTime));

    public DateTimeOffset DayOrderExpiry(DateTimeOffset placedAt) =>
        SessionContaining(placedAt)?.Close ?? StartOfDay(LocalDate(placedAt).AddDays(1));

    public DateTimeOffset StartOfDay(DateOnly date) => ToInstant(date, TimeOnly.MinValue);

    private static bool IsTradingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}

public sealed record MarketSession(DateOnly Date, DateTimeOffset Open, DateTimeOffset Close);
