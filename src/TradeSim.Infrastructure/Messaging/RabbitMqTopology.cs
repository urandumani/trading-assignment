using RabbitMQ.Client;

namespace TradeSim.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string EventsExchange = "tradesim.events";
    public const string DeadLetterExchange = "tradesim.dlx";

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
    }

    public static async Task DeclareDurableQueueAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        var deadLetterQueue = DeadLetterQueue(queue);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(deadLetterQueue, DeadLetterExchange, routingKey: queue, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queue,
            },
            cancellationToken: cancellationToken);
    }
}
