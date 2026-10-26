namespace TradeSim.Application.MarketData;

public interface IPriceReader
{
    Task<IReadOnlyDictionary<string, PriceTick>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PriceTick?> GetAsync(string symbol, CancellationToken cancellationToken = default);
}
