namespace TradeSim.Application.Abstractions;

public interface IOutbox
{
    void Enqueue(string routingKey, object message, string? correlationId);
}
