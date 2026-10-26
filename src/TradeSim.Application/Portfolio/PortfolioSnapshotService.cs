using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Application.MarketData;
using TradeSim.Domain.Portfolio;

namespace TradeSim.Application.Portfolio;

public sealed class PortfolioSnapshotService(IAppDbContext db, PriceBook priceBook)
{
    public async Task RecordAsync(Guid eventId, Guid accountId, DateTimeOffset takenAt, CancellationToken cancellationToken = default)
    {
        if (await db.PortfolioSnapshots.AnyAsync(s => s.EventId == eventId, cancellationToken))
        {
            return;
        }

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, cancellationToken);
        var holdings = await db.Positions.AsNoTracking()
            .Where(p => p.AccountId == accountId && p.Quantity > 0)
            .Select(p => new { p.InstrumentId, p.Quantity })
            .ToListAsync(cancellationToken);
        var prices = await priceBook.GetCurrentPricesAsync(cancellationToken);
        var equity = account.Equity(holdings.Sum(h => h.Quantity * prices[h.InstrumentId]));

        db.PortfolioSnapshots.Add(PortfolioSnapshot.Create(eventId, accountId, takenAt, Math.Round(equity, 2)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
