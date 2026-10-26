namespace TradeSim.Domain.Market;

public sealed class Instrument
{
    private Instrument() { }

    public Guid Id { get; private set; }
    public string Symbol { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    public decimal MaxMovePct { get; private set; }

    public int MinQuantity { get; private set; }

    public decimal LastPrice { get; private set; }

    public static Instrument Create(string symbol, string name, decimal maxMovePct, int minQuantity, decimal initialPrice) => new()
    {
        Id = Guid.CreateVersion7(),
        Symbol = symbol,
        Name = name,
        MaxMovePct = maxMovePct,
        MinQuantity = minQuantity,
        LastPrice = initialPrice,
    };

    public void UpdateLastPrice(decimal price) => LastPrice = price;
}
