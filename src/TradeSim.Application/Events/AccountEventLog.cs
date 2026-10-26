using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain.Events;

namespace TradeSim.Application.Events;

public static class AccountEventLog
{
    public static async Task AppendAsync(
        IAppDbContext db,
        Guid accountId,
        string? correlationId,
        DateTimeOffset occurredAt,
        IEnumerable<(string Type, object Payload)> events,
        CancellationToken cancellationToken)
    {
        var version = await db.AccountEvents.Where(e => e.AccountId == accountId).MaxAsync(e => (long?)e.Version, cancellationToken) ?? 0;

        foreach (var (type, payload) in events)
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType(), EventJson.Options);
            db.AccountEvents.Add(AccountEvent.Create(accountId, ++version, type, json, correlationId, occurredAt));
        }
    }
}
