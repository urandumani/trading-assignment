namespace TradeSim.Domain.Accounts;

public sealed class RiskLimits
{
    private RiskLimits() { }

    public decimal MarginRate { get; private set; }

    public int MaxPositionQuantity { get; private set; }

    public decimal MaxOrderValue { get; private set; }

    public int MaxOpenOrders { get; private set; }

    public static RiskLimits Create(decimal marginRate, int maxPositionQuantity, decimal maxOrderValue, int maxOpenOrders)
    {
        if (marginRate is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(marginRate), "Margin rate must be in (0, 1].");
        }

        return new RiskLimits
        {
            MarginRate = marginRate,
            MaxPositionQuantity = maxPositionQuantity,
            MaxOrderValue = maxOrderValue,
            MaxOpenOrders = maxOpenOrders,
        };
    }

    public static RiskLimits CashAccount() => Create(1.0m, 10_000, 250_000m, 100);

    public static RiskLimits MarginAccount() => Create(0.5m, 10_000, 250_000m, 100);
}
