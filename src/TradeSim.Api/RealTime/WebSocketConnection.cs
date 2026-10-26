using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace TradeSim.Api.RealTime;

public sealed class WebSocketConnection(WebSocket socket, Guid userId)
{
    private const int BufferSize = 256;

    private readonly Channel<string> _outgoing = Channel.CreateBounded<string>(
        new BoundedChannelOptions(BufferSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly Lock _lock = new();
    private readonly HashSet<string> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private bool _allSymbols;

    public Guid Id { get; } = Guid.NewGuid();
    public Guid UserId { get; } = userId;

    public bool IsSubscribedTo(string symbol)
    {
        lock (_lock)
        {
            return _allSymbols || _symbols.Contains(symbol);
        }
    }

    public IReadOnlyList<string> UpdateSubscription(IEnumerable<string> symbols, bool subscribe)
    {
        lock (_lock)
        {
            foreach (var symbol in symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0))
            {
                if (symbol == "*")
                {
                    _allSymbols = subscribe;
                }
                else if (subscribe)
                {
                    _symbols.Add(symbol);
                }
                else
                {
                    _symbols.Remove(symbol);
                }
            }

            return _allSymbols ? ["*"] : _symbols.Order().ToList();
        }
    }

    public void Send(string json) => _outgoing.Writer.TryWrite(json);

    public void Complete() => _outgoing.Writer.TryComplete();

    public async Task RunSendLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var json in _outgoing.Reader.ReadAllAsync(cancellationToken))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
    }
}
