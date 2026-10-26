using Microsoft.EntityFrameworkCore;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;
using TradeSim.Domain.Users;

namespace TradeSim.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Instrument> Instruments { get; }
    DbSet<Position> Positions { get; }
    DbSet<Order> Orders { get; }
    DbSet<AccountEvent> AccountEvents { get; }
    DbSet<PriceCandle> PriceCandles { get; }
    DbSet<PortfolioSnapshot> PortfolioSnapshots { get; }

    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
