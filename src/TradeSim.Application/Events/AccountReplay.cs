using System.Text.Json;
using TradeSim.Domain.Events;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.Events;

public sealed record ReplayedAccount(
    decimal Cash,
    decimal ReservedCash,
    IReadOnlyDictionary<string, int> Quantities,
    IReadOnlyDictionary<string, int> ReservedQuantities);

public static class AccountReplay
{
    public static ReplayedAccount Rebuild(IEnumerable<AccountEvent> history)
    {
        decimal cash = 0, reservedCash = 0;
        var quantities = new Dictionary<string, int>();
        var reserved = new Dictionary<string, int>();

        foreach (var entry in history.OrderBy(e => e.Version))
        {
            switch (entry.Type)
            {
                case AccountEventTypes.AccountOpened:
                    cash = Read<AccountOpenedPayload>(entry).InitialCash;
                    break;

                case AccountEventTypes.OrderPlaced:
                    var placed = Read<OrderPlacedPayload>(entry);
                    reservedCash += placed.ReservedCash;
                    Add(reserved, placed.Symbol, placed.ReservedShares);
                    break;

                case AccountEventTypes.OrderFilled:
                    var filled = Read<OrderFilledPayload>(entry);
                    cash += filled.CashChange;
                    reservedCash -= filled.ReleasedCash;
                    Add(reserved, filled.Symbol, -filled.ReleasedShares);
                    Add(quantities, filled.Symbol, filled.Side == OrderSide.Buy ? filled.Quantity : -filled.Quantity);
                    break;

                case AccountEventTypes.OrderCancelled or AccountEventTypes.OrderExpired or AccountEventTypes.OrderRejected:
                    var closed = Read<OrderClosedPayload>(entry);
                    reservedCash -= closed.ReleasedCash;
                    Add(reserved, closed.Symbol, -closed.ReleasedShares);
                    break;
            }
        }

        return new ReplayedAccount(cash, reservedCash, quantities, reserved);
    }

    private static T Read<T>(AccountEvent entry) => JsonSerializer.Deserialize<T>(entry.Payload, EventJson.Options)!;

    private static void Add(Dictionary<string, int> totals, string symbol, int delta) =>
        totals[symbol] = totals.GetValueOrDefault(symbol) + delta;
}
