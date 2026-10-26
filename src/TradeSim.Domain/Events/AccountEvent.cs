namespace TradeSim.Domain.Events;

public sealed class AccountEvent
{
    private AccountEvent() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public long Version { get; private set; }
    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static AccountEvent Create(Guid accountId, long version, string type, string payload, string? correlationId, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = accountId,
        Version = version,
        Type = type,
        Payload = payload,
        CorrelationId = correlationId,
        OccurredAt = occurredAt,
    };
}

public static class AccountEventTypes
{
    public const string AccountOpened = nameof(AccountOpened);
    public const string OrderPlaced = nameof(OrderPlaced);
    public const string OrderFilled = nameof(OrderFilled);
    public const string OrderCancelled = nameof(OrderCancelled);
    public const string OrderExpired = nameof(OrderExpired);
    public const string OrderRejected = nameof(OrderRejected);
}
