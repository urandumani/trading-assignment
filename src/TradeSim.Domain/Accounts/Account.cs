namespace TradeSim.Domain.Accounts;

public sealed class Account
{
    private Account() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public decimal InitialCash { get; private set; }

    public decimal Cash { get; private set; }

    public decimal ReservedCash { get; private set; }

    public RiskLimits RiskLimits { get; private set; } = null!;

    public uint Version { get; private set; }

    public static Account Open(Guid userId, decimal initialCash, RiskLimits riskLimits) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        InitialCash = initialCash,
        Cash = initialCash,
        RiskLimits = riskLimits,
    };

    public decimal Equity(decimal holdingsValue) => Cash + holdingsValue;

    public decimal BuyingPower(decimal holdingsValue) =>
        Equity(holdingsValue) / RiskLimits.MarginRate - holdingsValue - ReservedCash;

    public void ReserveCash(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        ReservedCash += amount;
    }

    public void ReleaseCash(decimal amount)
    {
        if (amount < 0 || amount > ReservedCash)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot release more than is reserved.");
        }

        ReservedCash -= amount;
    }

    public void Debit(decimal amount) => Cash -= amount;

    public void Credit(decimal amount) => Cash += amount;
}
