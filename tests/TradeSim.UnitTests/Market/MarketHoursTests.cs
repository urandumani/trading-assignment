using Shouldly;
using TradeSim.Domain.Market;

namespace TradeSim.UnitTests.Market;

public class MarketHoursTests
{
    private static readonly MarketHours Hours = new(MarketHours.Cest);

    private static DateTimeOffset Utc(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7, 29, false)]
    [InlineData(7, 30, true)]
    [InlineData(13, 59, true)]
    [InlineData(14, 0, false)]
    public void Friday_before_the_european_switch_to_winter_time(int hour, int minute, bool open)
    {
        Hours.IsOpen(Utc(10, 23, hour, minute)).ShouldBe(open);
    }

    [Theory]
    [InlineData(7, 29, false)]
    [InlineData(7, 30, true)]
    [InlineData(13, 59, true)]
    [InlineData(14, 0, false)]
    public void Monday_after_the_european_switch_keeps_the_cest_session(int hour, int minute, bool open)
    {
        Hours.IsOpen(Utc(10, 26, hour, minute)).ShouldBe(open);
    }

    [Fact]
    public void Midwinter_session_is_also_0730_to_1400_utc()
    {
        Hours.SessionOn(new DateOnly(2027, 1, 15)).ShouldBe(new MarketSession(
            new DateOnly(2027, 1, 15),
            new DateTimeOffset(2027, 1, 15, 7, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 15, 14, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Weekends_are_closed()
    {
        Hours.IsOpen(Utc(10, 24, 10, 0)).ShouldBeFalse();
        Hours.IsOpen(Utc(10, 25, 10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Always_open_mode_ignores_the_schedule()
    {
        new MarketHours(TimeZoneInfo.Utc, alwaysOpen: true).IsOpen(Utc(10, 25, 3, 0)).ShouldBeTrue();
    }

    [Fact]
    public void Next_session_from_a_weekend_is_monday_morning()
    {
        var next = Hours.NextSession(Utc(10, 25, 12, 0));

        next.Date.ShouldBe(new DateOnly(2026, 10, 26));
        next.Open.ShouldBe(Utc(10, 26, 7, 30));
        next.Close.ShouldBe(Utc(10, 26, 14, 0));
    }

    [Fact]
    public void Next_session_during_trading_is_the_following_trading_day()
    {
        Hours.NextSession(Utc(10, 23, 10, 0)).Date.ShouldBe(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void Previous_sessions_are_the_last_completed_weekdays_oldest_first()
    {
        var sessions = Hours.PreviousSessions(Utc(10, 25, 12, 0), 5);

        sessions.Select(s => s.Date.Day).ShouldBe([19, 20, 21, 22, 23]);
    }

    [Fact]
    public void A_session_still_in_progress_is_not_a_previous_session()
    {
        var sessions = Hours.PreviousSessions(Utc(10, 23, 10, 0), 1);

        sessions.Single().Date.ShouldBe(new DateOnly(2026, 10, 22));
    }

    [Fact]
    public void A_day_order_expires_at_the_close_of_its_session()
    {
        Hours.DayOrderExpiry(Utc(10, 23, 9, 15)).ShouldBe(Utc(10, 23, 14, 0));
        Hours.DayOrderExpiry(Utc(10, 26, 13, 59)).ShouldBe(Utc(10, 26, 14, 0));
    }

    [Fact]
    public void Outside_a_session_a_day_order_lasts_until_local_midnight()
    {
        Hours.DayOrderExpiry(Utc(10, 25, 12, 0)).ShouldBe(Utc(10, 25, 22, 0));
    }

    [Fact]
    public void Local_date_follows_cest_midnight_not_utc_midnight()
    {
        Hours.LocalDate(Utc(10, 23, 22, 30)).ShouldBe(new DateOnly(2026, 10, 24));
    }
}
