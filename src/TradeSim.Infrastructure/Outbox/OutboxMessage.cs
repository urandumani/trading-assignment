namespace TradeSim.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static OutboxMessage Create(string type, string payload, string? correlationId, DateTimeOffset createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Payload = payload,
        CorrelationId = correlationId,
        CreatedAt = createdAt,
    };

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt;
}
