using TradeSim.Domain.Market;

namespace TradeSim.Application.MarketData;

public sealed class PriceBook(InstrumentCatalog catalog, IPriceReader prices)
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCurrentPricesAsync(CancellationToken cancellationToken = default)
    {
        var instruments = await catalog.GetAllAsync(cancellationToken);
        var live = await prices.GetAllAsync(cancellationToken);

        return instruments.ToDictionary(
            i => i.Id,
            i => live.TryGetValue(i.Symbol, out var tick) ? tick.Price : RandomWalk.ToTick(i.LastSavedPrice));
    }
}
