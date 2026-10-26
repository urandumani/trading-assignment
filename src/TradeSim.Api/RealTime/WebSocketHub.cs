using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace TradeSim.Api.RealTime;

public sealed class WebSocketHub(ILogger<WebSocketHub> logger)
{
    private const int MaxMessageBytes = 16 * 1024;

    private readonly ConcurrentDictionary<Guid, WebSocketConnection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public async Task RunAsync(WebSocket socket, Guid userId, CancellationToken cancellationToken)
    {
        var connection = new WebSocketConnection(socket, userId);
        _connections[connection.Id] = connection;
        logger.LogInformation("WebSocket connected for user {UserId} ({Count} on this instance)", userId, _connections.Count);

        connection.Send(Message("welcome", new
        {
            userId,
            help = """Send {"action":"subscribe","symbols":["APXL"]} (or ["*"] for all) to receive ticks; your own order updates arrive automatically.""",
        }));

        var sending = connection.RunSendLoopAsync(cancellationToken);
        try
        {
            await ReceiveLoopAsync(socket, connection, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
        }
        finally
        {
            connection.Complete();
            _connections.TryRemove(connection.Id, out _);
            logger.LogInformation("WebSocket closed for user {UserId}", userId);
        }

        try
        {
            await sending;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
        }
    }

    public void BroadcastTick(string tickJson)
    {
        var symbol = JsonDocument.Parse(tickJson).RootElement.GetProperty("symbol").GetString()!;
        var message = $$"""{"type":"tick","data":{{tickJson}}}""";
        foreach (var connection in _connections.Values)
        {
            if (connection.IsSubscribedTo(symbol))
            {
                connection.Send(message);
            }
        }
    }

    public void SendOrderUpdate(Guid userId, string orderJson)
    {
        var message = $$"""{"type":"order_update","data":{{orderJson}}}""";
        foreach (var connection in _connections.Values.Where(c => c.UserId == userId))
        {
            connection.Send(message);
        }
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, WebSocketConnection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", cancellationToken);
                return;
            }

            if (result.EndOfMessage)
            {
                connection.Send(HandleClientMessage(connection, message.ToArray()));
                message.SetLength(0);
            }
        }
    }

    private static string HandleClientMessage(WebSocketConnection connection, byte[] json)
    {
        try
        {
            var request = JsonSerializer.Deserialize<ClientMessage>(json, JsonSerializerOptions.Web);
            return request?.Action?.ToLowerInvariant() switch
            {
                "subscribe" => Message("subscribed", new { symbols = connection.UpdateSubscription(request.Symbols ?? [], subscribe: true) }),
                "unsubscribe" => Message("subscribed", new { symbols = connection.UpdateSubscription(request.Symbols ?? [], subscribe: false) }),
                "ping" => Message("pong", new { }),
                _ => Message("error", new { message = "Unknown action. Use subscribe, unsubscribe or ping." }),
            };
        }
        catch (JsonException)
        {
            return Message("error", new { message = """Messages must be JSON, e.g. {"action":"subscribe","symbols":["APXL"]}.""" });
        }
    }

    private static string Message(string type, object data) =>
        JsonSerializer.Serialize(new { type, data }, JsonSerializerOptions.Web);

    private sealed record ClientMessage(string? Action, string[]? Symbols);
}
