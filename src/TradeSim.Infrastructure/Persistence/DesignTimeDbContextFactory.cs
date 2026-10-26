using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradeSim.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TradeSimDbContext>
{
    public TradeSimDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TradeSimDbContext>()
            .UseNpgsql("Host=localhost;Database=tradesim")
            .UseSnakeCaseNamingConvention()
            .Options);
}
