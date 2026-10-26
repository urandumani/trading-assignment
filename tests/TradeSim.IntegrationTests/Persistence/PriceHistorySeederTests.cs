using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TradeSim.Application.MarketData;
using TradeSim.Domain.Market;
using TradeSim.Infrastructure.MarketData;
using TradeSim.Infrastructure.Persistence.Seeding;

namespace TradeSim.IntegrationTests.Persistence;

public class PriceHistorySeederTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly MarketHours Hours = new(MarketHours.Cest);

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Generates_five_sessions_of_minute_candles_and_continues_from_the_last_one()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await new DatabaseSeeder(db, Options.Create(new SeedOptions()), Clock, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
            await new PriceHistorySeeder(db, Hours, Clock, NullLogger<PriceHistorySeeder>.Instance).SeedAsync();
        }

        await using var check = newContext();
        (await check.PriceCandles.CountAsync()).ShouldBe(12 * 5 * 390);

        var apxl = await check.Instruments.SingleAsync(i => i.Symbol == "APXL");
        var apxlCandles = await check.PriceCandles.Where(c => c.InstrumentId == apxl.Id).OrderBy(c => c.Minute).ToListAsync();
        apxlCandles[0].Minute.ShouldBe(new DateTimeOffset(2026, 10, 19, 7, 30, 0, TimeSpan.Zero));
        apxlCandles[^1].Minute.ShouldBe(new DateTimeOffset(2026, 10, 23, 13, 59, 0, TimeSpan.Zero));
        apxlCandles.ShouldAllBe(c => c.Low <= c.Open && c.Low <= c.Close && c.High >= c.Open && c.High >= c.Close);

        apxl.LastPrice.ShouldBe(apxlCandles[^1].Close);
    }

    [Fact]
    public async Task Does_nothing_when_history_already_exists()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await new DatabaseSeeder(db, Options.Create(new SeedOptions()), Clock, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
            await new PriceHistorySeeder(db, Hours, Clock, NullLogger<PriceHistorySeeder>.Instance).SeedAsync(sessionCount: 1);
        }

        await using (var db = newContext())
        {
            await new PriceHistorySeeder(db, Hours, Clock, NullLogger<PriceHistorySeeder>.Instance).SeedAsync(sessionCount: 1);
        }

        await using var check = newContext();
        (await check.PriceCandles.CountAsync()).ShouldBe(12 * 390);
    }

    [Fact]
    public async Task Market_data_store_upserts_candles_and_reads_back_the_day_range()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        Guid instrumentId;
        await using (var db = newContext())
        {
            await new DatabaseSeeder(db, Options.Create(new SeedOptions()), Clock, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
            instrumentId = (await db.Instruments.SingleAsync(i => i.Symbol == "NOVA")).Id;
        }

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(newContext().Database.GetConnectionString()!);
        var store = new MarketDataStore(dataSource);
        var minute = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        await store.SaveAsync([new CandleData(instrumentId, minute, 85m, 86m, 84.5m, 85.5m)], [new LastPrice(instrumentId, 85.5m)]);
        await store.SaveAsync([new CandleData(instrumentId, minute, 85m, 87m, 85m, 86.2m)], [new LastPrice(instrumentId, 86.2m)]);
        await store.SaveAsync([new CandleData(instrumentId, minute.AddMinutes(1), 86.2m, 86.4m, 83m, 84m)], []);

        var range = (await store.GetDayRangesAsync(minute))[instrumentId];
        range.ShouldBe(new DayRange(Open: 85m, High: 87m, Low: 83m));
        (await store.GetInstrumentsAsync()).Single(i => i.Id == instrumentId).LastPrice.ShouldBe(86.2m);
    }
}
