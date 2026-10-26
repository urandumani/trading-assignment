using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using TradeSim.Infrastructure.Messaging;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.Infrastructure.Outbox;

public sealed class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnection rabbitMq,
    TimeProvider clock,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishPendingAsync(stoppingToken);
                if (failures > 0)
                {
                    logger.LogInformation("RabbitMQ is reachable again; outbox publishing resumed");
                }

                failures = 0;
                if (published < BatchSize)
                {
                    await Task.Delay(PollInterval, clock, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                var delay = TimeSpan.FromMilliseconds(Math.Min(PollInterval.TotalMilliseconds * Math.Pow(2, failures), 30_000));
                logger.LogWarning("Publishing the outbox failed ({Error}); messages stay queued, retrying in {Delay}", ex.Message, delay);
                await CloseChannelAsync();
                await Task.Delay(delay, clock, stoppingToken);
            }
        }
    }

    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradeSimDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var published = 0;
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            foreach (var message in pending)
            {
                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    Type = message.Type,
                    ContentType = "application/json",
                    Persistent = true,
                    Headers = new Dictionary<string, object?> { ["correlation-id"] = message.CorrelationId },
                };

                await channel.BasicPublishAsync(RabbitMqTopology.EventsExchange, message.Type, mandatory: false, properties,
                    Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
                message.MarkPublished(clock.GetUtcNow());
                published++;
            }
        }
        finally
        {
            if (published > 0)
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }

        return published;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var connection = await rabbitMq.GetAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareExchangesAsync(_channel, cancellationToken);
        return _channel;
    }

    private async Task CloseChannelAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch
            {
            }

            _channel = null;
        }
    }
}
