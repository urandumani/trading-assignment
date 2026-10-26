using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeSim.Application.Events;
using TradeSim.Application.Orders;
using TradeSim.Application.Portfolio;

namespace TradeSim.Infrastructure.Messaging;

public sealed class PortfolioSnapshotConsumer(RabbitMqConnection rabbitMq, IServiceScopeFactory scopeFactory, ILogger<PortfolioSnapshotConsumer> logger)
    : QueueConsumer(rabbitMq, logger)
{
    public const string Queue = "portfolio-snapshots";

    protected override string QueueName => Queue;

    protected override IReadOnlyList<string> RoutingKeys => ["order.filled"];

    protected override async Task HandleAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        var fill = JsonSerializer.Deserialize<OrderStatusChanged>(message.Body.Span, EventJson.Options)
                   ?? throw new JsonException("The message is empty.");
        if (!Guid.TryParse(message.MessageId, out var eventId))
        {
            throw new JsonException("The message has no id.");
        }

        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PortfolioSnapshotService>()
            .RecordAsync(eventId, fill.AccountId, fill.OccurredAt, cancellationToken);
    }
}
