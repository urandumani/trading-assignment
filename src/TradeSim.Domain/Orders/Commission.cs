namespace TradeSim.Domain.Orders;

public static class Commission
{
    public const decimal PerShare = 0.005m;
    public const decimal Minimum = 1.00m;

    public static decimal For(int quantity) =>
        Math.Max(Minimum, Math.Round(quantity * PerShare, 2, MidpointRounding.AwayFromZero));
}
