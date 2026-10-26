namespace TradeSim.Domain.Portfolio;

public sealed class PortfolioSnapshot
{
    private PortfolioSnapshot() { }

    public Guid EventId { get; private set; }
    public Guid AccountId { get; private set; }
    public DateTimeOffset TakenAt { get; private set; }
    public decimal Equity { get; private set; }

    public static PortfolioSnapshot Create(Guid eventId, Guid accountId, DateTimeOffset takenAt, decimal equity) => new()
    {
        EventId = eventId,
        AccountId = accountId,
        TakenAt = takenAt,
        Equity = equity,
    };
}
