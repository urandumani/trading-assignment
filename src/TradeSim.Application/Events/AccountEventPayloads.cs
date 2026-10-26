using System.Text.Json;
using System.Text.Json.Serialization;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;

namespace TradeSim.Application.Events;

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };
}

public sealed record AccountOpenedPayload(decimal InitialCash);

public sealed record OrderPlacedPayload(
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    TimeInForce TimeInForce,
    int Quantity,
    decimal? LimitPrice,
    decimal? TriggerPrice,
    decimal ReservedCash,
    int ReservedShares);

public sealed record OrderFilledPayload(
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    int Quantity,
    decimal Price,
    decimal Commission,
    decimal CashChange,
    decimal CashAfter,
    decimal ReleasedCash,
    int ReleasedShares,
    decimal RealizedPnl,
    int PositionQuantityAfter,
    decimal AvgCostAfter)
{
    public static OrderFilledPayload From(Order order, string symbol, FillDetails fill, Account account, Position position) => new(
        order.Id, symbol, order.Side, fill.Quantity, fill.Price, fill.Commission, fill.CashChange, account.Cash,
        fill.ReleasedCash, fill.ReleasedShares, fill.RealizedPnl, position.Quantity, position.AvgCost);
}

public sealed record OrderClosedPayload(Guid OrderId, string Symbol, decimal ReleasedCash, int ReleasedShares, string? Reason);
