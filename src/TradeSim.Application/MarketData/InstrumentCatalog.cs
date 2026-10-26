using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TradeSim.Application.Abstractions;
using TradeSim.Domain;

namespace TradeSim.Application.MarketData;

public sealed record InstrumentDetails(Guid Id, string Symbol, string Name, decimal MaxMovePct, int MinQuantity, decimal LastSavedPrice)
{
    public string Volatility => MaxMovePct switch
    {
        <= 0.020m => "LOW",
        <= 0.035m => "MEDIUM",
        <= 0.060m => "HIGH",
        <= 0.090m => "VERY_HIGH",
        _ => "EXTREME",
    };
}

public sealed class InstrumentCatalog(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private const string CacheKey = "instruments";

    public async Task<IReadOnlyList<InstrumentDetails>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await cache.GetOrCreateAsync(CacheKey, _ => LoadAsync(cancellationToken)))!;

    public async Task<InstrumentDetails> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var instruments = await GetAllAsync(cancellationToken);
        return instruments.FirstOrDefault(i => string.Equals(i.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
               ?? throw new DomainException(ErrorCodes.InstrumentNotFound, $"Unknown instrument '{symbol}'.");
    }

    private async Task<IReadOnlyList<InstrumentDetails>> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Instruments
            .AsNoTracking()
            .OrderBy(i => i.Symbol)
            .Select(i => new InstrumentDetails(i.Id, i.Symbol, i.Name, i.MaxMovePct, i.MinQuantity, i.LastPrice))
            .ToListAsync(cancellationToken);
    }
}
