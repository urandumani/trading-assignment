using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;
using TradeSim.Domain.Users;
using TradeSim.Infrastructure.Outbox;

namespace TradeSim.Infrastructure.Persistence;

public sealed class TradeSimDbContext(DbContextOptions<TradeSimDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<AccountEvent> AccountEvents => Set<AccountEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PriceCandle> PriceCandles => Set<PriceCandle>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);

        configurationBuilder.Properties<OrderSide>().HaveConversion<string>().HaveMaxLength(10);
        configurationBuilder.Properties<OrderType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TimeInForce>().HaveConversion<string>().HaveMaxLength(10);
        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradeSimDbContext).Assembly);

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            if (!foreignKey.IsOwnership)
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
