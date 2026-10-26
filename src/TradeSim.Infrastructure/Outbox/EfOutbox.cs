using System.Text.Json;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Events;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.Infrastructure.Outbox;

public sealed class EfOutbox(TradeSimDbContext db, TimeProvider clock) : IOutbox
{
    public void Enqueue(string routingKey, object message, string? correlationId) =>
        db.OutboxMessages.Add(OutboxMessage.Create(
            routingKey,
            JsonSerializer.Serialize(message, message.GetType(), EventJson.Options),
            correlationId,
            clock.GetUtcNow()));
}
