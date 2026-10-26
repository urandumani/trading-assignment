using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeSim.Domain.Market;

namespace TradeSim.Infrastructure.Persistence.Seeding;

public sealed class PriceHistorySeeder(
    TradeSimDbContext db,
    MarketHours hours,
    TimeProvider clock,
    ILogger<PriceHistorySeeder> logger)
{
    private const int StepsPerMinute = 180;

    public async Task SeedAsync(int sessionCount = 5, CancellationToken cancellationToken = default)
    {
        if (await db.PriceCandles.AnyAsync(cancellationToken))
        {
            return;
        }

        var sessions = hours.PreviousSessions(clock.GetUtcNow(), sessionCount);
        var instruments = await db.Instruments.OrderBy(i => i.Symbol).ToListAsync(cancellationToken);
        var random = new Random(2026);

        foreach (var instrument in instruments)
        {
            var price = instrument.LastPrice;
            var candles = new List<PriceCandle>();

            foreach (var session in sessions)
            {
                for (var minute = session.Open; minute < session.Close; minute = minute.AddMinutes(1))
                {
                    var candle = PriceCandle.Start(instrument.Id, minute, RandomWalk.ToTick(price));
                    for (var step = 0; step < StepsPerMinute; step++)
                    {
                        price = RandomWalk.Next(price, instrument.MaxMovePct, jumpChance: 0m, random).Price;
                        candle.Add(RandomWalk.ToTick(price));
                    }

                    candles.Add(candle);
                }
            }

            db.PriceCandles.AddRange(candles);
            instrument.UpdateLastPrice(RandomWalk.ToTick(price));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Generated price history: {Sessions} sessions ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}), {Count} candles",
            sessions.Count, sessions[0].Date, sessions[^1].Date, await db.PriceCandles.CountAsync(cancellationToken));
    }
}
