using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TradeSim.Infrastructure.Messaging;

public sealed record ReceivedMessage(string? MessageId, string RoutingKey, string? CorrelationId, ReadOnlyMemory<byte> Body);

public abstract class QueueConsumer(RabbitMqConnection rabbitMq, ILogger logger) : BackgroundService
{
    private IChannel? _channel;

    protected abstract string? QueueName { get; }

    protected abstract IReadOnlyList<string> RoutingKeys { get; }

    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    protected abstract Task HandleAsync(ReceivedMessage message, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (_channel is null)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Can't consume from RabbitMQ yet ({Error}); retrying in {Delay}", ex.Message, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }
    }

    private async Task StartConsumingAsync(CancellationToken cancellationToken)
    {
        var connection = await rabbitMq.GetAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareExchangesAsync(channel, cancellationToken);

        string queue;
        if (QueueName is { } name)
        {
            await RabbitMqTopology.DeclareDurableQueueAsync(channel, name, cancellationToken);
            queue = name;
        }
        else
        {
            queue = (await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true, cancellationToken: cancellationToken)).QueueName;
        }

        foreach (var routingKey in RoutingKeys)
        {
            await channel.QueueBindAsync(queue, RabbitMqTopology.EventsExchange, routingKey, cancellationToken: cancellationToken);
        }

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, cancellationToken);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken);

        _channel = channel;
        logger.LogInformation("Consuming {RoutingKeys} from queue {Queue}", string.Join(", ", RoutingKeys), queue);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var correlationId = delivery.BasicProperties.Headers is { } headers
                            && headers.TryGetValue("correlation-id", out var value) && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;
        var message = new ReceivedMessage(delivery.BasicProperties.MessageId, delivery.RoutingKey, correlationId, delivery.Body.ToArray());

        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId, ["MessageId"] = message.MessageId });

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await HandleAsync(message, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Malformed {RoutingKey} message; moving it to the dead-letter queue", delivery.RoutingKey);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Count && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Handling {RoutingKey} failed (attempt {Attempt}): {Error}; retrying in {Delay}",
                    delivery.RoutingKey, attempt + 1, ex.Message, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handling {RoutingKey} failed {Attempts} times; moving it to the dead-letter queue",
                    delivery.RoutingKey, attempt + 1);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                return;
            }
        }
    }
}
