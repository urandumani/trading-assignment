namespace TradeSim.Domain.Portfolio;

public sealed class Position
{
    private Position() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public int Quantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    public decimal AvgCost { get; private set; }

    public decimal RealizedPnl { get; private set; }

    public uint Version { get; private set; }

    public int AvailableQuantity => Quantity - ReservedQuantity;

    public static Position Open(Guid accountId, Guid instrumentId) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = accountId,
        InstrumentId = instrumentId,
    };

    public void ApplyBuy(int quantity, decimal price)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        AvgCost = Math.Round((Quantity * AvgCost + quantity * price) / (Quantity + quantity), 4);
        Quantity += quantity;
    }

    public decimal ApplySell(int quantity, decimal price)
    {
        if (quantity <= 0 || quantity > Quantity - ReservedQuantity)
        {
            throw new DomainException(ErrorCodes.InsufficientPosition, $"Cannot sell {quantity} shares; {AvailableQuantity} available.");
        }

        var realized = (price - AvgCost) * quantity;
        RealizedPnl += realized;
        Quantity -= quantity;
        if (Quantity == 0)
        {
            AvgCost = 0;
        }

        return realized;
    }

    public void ReserveShares(int quantity)
    {
        if (quantity <= 0 || quantity > AvailableQuantity)
        {
            throw new DomainException(ErrorCodes.InsufficientPosition, $"Cannot reserve {quantity} shares; {AvailableQuantity} available.");
        }

        ReservedQuantity += quantity;
    }

    public void ReleaseShares(int quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Cannot release more than is reserved.");
        }

        ReservedQuantity -= quantity;
    }
}
