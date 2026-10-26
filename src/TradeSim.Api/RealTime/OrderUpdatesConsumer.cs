using System.Text;
using System.Text.Json;
using TradeSim.Infrastructure.Messaging;

namespace TradeSim.Api.RealTime;

public sealed class OrderUpdatesConsumer(RabbitMqConnection rabbitMq, WebSocketHub hub, ILogger<OrderUpdatesConsumer> logger)
    : QueueConsumer(rabbitMq, logger)
{
    protected override string? QueueName => null;

    protected override IReadOnlyList<string> RoutingKeys => ["order.*"];

    protected override Task HandleAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(message.Body.Span);
        var userId = JsonDocument.Parse(json).RootElement.GetProperty("userId").GetGuid();
        hub.SendOrderUpdate(userId, json);
        return Task.CompletedTask;
    }
}
