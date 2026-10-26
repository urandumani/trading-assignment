using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace TradeSim.Infrastructure.Messaging;

public sealed class RabbitMqConnection(IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen ?? false;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration.GetRequiredConnectionString("RabbitMq")),
                    ClientProvidedName = Environment.MachineName,
                };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
            }

            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
