using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Shouldly;
using TradeSim.Infrastructure.Messaging;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class MessagingTests(ApiFactory api)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private async Task<WebSocket> ConnectAsync(HttpClient client)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter;
        var socket = await api.Server.CreateWebSocketClient().ConnectAsync(new Uri($"ws://localhost/ws?access_token={token}"), CancellationToken.None);
        (await ReceiveAsync(socket)).GetProperty("type").GetString().ShouldBe("welcome");
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
    }

    private static async Task<JsonElement> ReceiveUntilAsync(WebSocket socket, Func<JsonElement, bool> match, Func<CancellationToken, Task> keepTrying)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var background = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested)
            {
                await keepTrying(timeout.Token);
                await Task.Delay(200, timeout.Token);
            }
        }, timeout.Token);

        try
        {
            while (true)
            {
                var message = await ReceiveAsync(socket, timeout.Token);
                if (match(message))
                {
                    return message;
                }
            }
        }
        finally
        {
            await timeout.CancelAsync();
            await background.ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private static async Task<uint> MessageCountAsync(IChannel channel, string queue) =>
        (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;

    private async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!await condition())
        {
            await Task.Delay(100, timeout.Token);
        }
    }

    [Fact]
    public async Task A_websocket_needs_a_valid_token()
    {
        var client = api.Server.CreateWebSocketClient();

        await Should.ThrowAsync<InvalidOperationException>(() => client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => client.ConnectAsync(new Uri("ws://localhost/ws?access_token=not-a-jwt"), CancellationToken.None));
    }

    [Fact]
    public async Task A_client_receives_ticks_only_for_the_symbols_it_subscribed_to()
    {
        var (client, _, _) = await api.NewUserAsync();
        using var socket = await ConnectAsync(client);

        await SendAsync(socket, new { action = "subscribe", symbols = new[] { "SOLR" } });
        var subscribed = await ReceiveAsync(socket);
        subscribed.GetProperty("type").GetString().ShouldBe("subscribed");
        subscribed.GetProperty("data").GetProperty("symbols")[0].GetString().ShouldBe("SOLR");

        var tick = await ReceiveUntilAsync(socket,
            message => message.GetProperty("type").GetString() == "tick",
            async _ =>
            {
                await api.Prices.SetPriceAsync("NOVA", 99m);
                await api.Prices.SetPriceAsync("SOLR", 18.50m);
            });

        tick.GetProperty("data").GetProperty("symbol").GetString().ShouldBe("SOLR");
        tick.GetProperty("data").GetProperty("price").GetDecimal().ShouldBe(18.50m);
    }

    [Fact]
    public async Task Unknown_client_messages_get_an_error_reply()
    {
        var (client, _, _) = await api.NewUserAsync();
        using var socket = await ConnectAsync(client);

        await SendAsync(socket, new { action = "dance" });
        (await ReceiveAsync(socket)).GetProperty("type").GetString().ShouldBe("error");

        await SendAsync(socket, new { action = "ping" });
        (await ReceiveAsync(socket)).GetProperty("type").GetString().ShouldBe("pong");
    }

    [Fact]
    public async Task A_users_order_updates_arrive_over_the_websocket_via_the_outbox_and_rabbitmq()
    {
        var (client, userId, _) = await api.NewUserAsync();
        using var socket = await ConnectAsync(client);
        await api.Prices.SetPriceAsync("GRDX", 60m);
        var orderId = (await (await client.PlaceOrderAsync(userId, "GRDX", "BUY", "MARKET", 5)).DataAsync(HttpStatusCode.Created)).Text("orderId");

        var relay = api.CreateOutboxRelay();
        var update = await ReceiveUntilAsync(socket,
            message => message.GetProperty("type").GetString() == "order_update",
            async cancellationToken => await relay.PublishPendingAsync(cancellationToken));

        var data = update.GetProperty("data");
        data.GetProperty("orderId").GetString().ShouldBe(orderId);
        data.GetProperty("status").GetString().ShouldBe("FILLED");
        data.GetProperty("executedPrice").GetDecimal().ShouldBe(60m);

        await using var db = api.CreateDbContext();
        (await db.OutboxMessages.ToListAsync()).Single(m => m.Payload.Contains(orderId)).PublishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task While_rabbitmq_is_unreachable_messages_wait_in_the_outbox()
    {
        var (client, userId, _) = await api.NewUserAsync();
        var orderId = (await (await client.PlaceOrderAsync(userId, "BRKX", "BUY", "LIMIT", 1, limitPrice: 1m)).DataAsync(HttpStatusCode.Created))
            .Text("orderId");

        var brokenRelay = api.CreateOutboxRelay(api.CreateRabbitMqConnection("amqp://guest:guest@localhost:1/"));
        await Should.ThrowAsync<Exception>(() => brokenRelay.PublishPendingAsync());

        await using (var db = api.CreateDbContext())
        {
            (await db.OutboxMessages.ToListAsync()).Single(m => m.Payload.Contains(orderId)).PublishedAt.ShouldBeNull();
        }

        var relay = api.CreateOutboxRelay();
        while (await relay.PublishPendingAsync() > 0)
        {
        }

        await using var check = api.CreateDbContext();
        (await check.OutboxMessages.ToListAsync()).Single(m => m.Payload.Contains(orderId)).PublishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Fills_become_equity_snapshots_and_bad_messages_end_up_in_the_dead_letter_queue()
    {
        var consumer = new PortfolioSnapshotConsumer(api.CreateRabbitMqConnection(), api.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PortfolioSnapshotConsumer>.Instance)
        {
            RetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40)],
        };
        await using var connection = await new ConnectionFactory { Uri = new Uri(api.RabbitMqConnectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareExchangesAsync(channel, CancellationToken.None);
        await RabbitMqTopology.DeclareDurableQueueAsync(channel, PortfolioSnapshotConsumer.Queue, CancellationToken.None);

        await consumer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await channel.QueueDeclarePassiveAsync(PortfolioSnapshotConsumer.Queue)).ConsumerCount > 0);

        try
        {
            var (client, userId, accountId) = await api.NewUserAsync();
            await api.Prices.SetPriceAsync("FINX", 95m);
            await (await client.PlaceOrderAsync(userId, "FINX", "BUY", "MARKET", 10)).DataAsync(HttpStatusCode.Created);
            var relay = api.CreateOutboxRelay();
            await WaitUntilAsync(async () =>
            {
                await relay.PublishPendingAsync();
                await using var db = api.CreateDbContext();
                return await db.PortfolioSnapshots.AnyAsync(s => s.AccountId == accountId);
            });

            var performance = await (await client.GetAsync($"/api/v1/portfolios/{userId}/performance")).DataAsync(HttpStatusCode.OK);
            performance.GetProperty("equityCurve").GetArrayLength().ShouldBe(3);

            var deadLetterQueue = RabbitMqTopology.DeadLetterQueue(PortfolioSnapshotConsumer.Queue);
            var before = await MessageCountAsync(channel, deadLetterQueue);

            await channel.BasicPublishAsync(RabbitMqTopology.EventsExchange, "order.filled", mandatory: false,
                new BasicProperties { MessageId = Guid.NewGuid().ToString() }, Encoding.UTF8.GetBytes("this is not json"));
            var unknownAccount = JsonSerializer.Serialize(new
            {
                orderId = Guid.NewGuid(), accountId = Guid.NewGuid(), userId = Guid.NewGuid(), symbol = "FINX", side = "BUY",
                orderType = "MARKET", status = "FILLED", quantity = 1, occurredAt = DateTimeOffset.UtcNow,
            });
            await channel.BasicPublishAsync(RabbitMqTopology.EventsExchange, "order.filled", mandatory: false,
                new BasicProperties { MessageId = Guid.NewGuid().ToString() }, Encoding.UTF8.GetBytes(unknownAccount));

            await WaitUntilAsync(async () => await MessageCountAsync(channel, deadLetterQueue) == before + 2);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
