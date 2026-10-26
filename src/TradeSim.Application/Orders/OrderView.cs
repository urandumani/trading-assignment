using TradeSim.Domain.Orders;

namespace TradeSim.Application.Orders;

public sealed record OrderView(
    Guid OrderId,
    string InstrumentSymbol,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    int Quantity,
    decimal? LimitPrice,
    decimal? TriggerPrice,
    OrderStatus Status,
    DateTimeOffset? ExecutedAt,
    decimal? ExecutedPrice,
    int ExecutedQuantity,
    decimal? Commission,
    string? RejectReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderView From(Order order, string symbol) => new(
        order.Id,
        symbol,
        order.Side,
        order.Type,
        order.TimeInForce,
        order.Quantity,
        Cents(order.LimitPrice),
        Cents(order.TriggerPrice),
        order.Status,
        order.ExecutedAt,
        Cents(order.ExecutedPrice),
        order.Status == OrderStatus.Filled ? order.Quantity : 0,
        Cents(order.Commission),
        order.RejectReason,
        order.CreatedAt,
        order.UpdatedAt);

    private static decimal? Cents(decimal? amount) => amount is null ? null : Math.Round(amount.Value, 2);
}

public sealed record OrderStatusChanged(
    Guid OrderId,
    Guid AccountId,
    Guid UserId,
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    OrderStatus Status,
    int Quantity,
    decimal? ExecutedPrice,
    decimal? Commission,
    string? Reason,
    DateTimeOffset OccurredAt)
{
    public string RoutingKey => $"order.{Status.ToString().ToLowerInvariant()}";

    public static OrderStatusChanged From(Order order, Guid userId, string symbol) => new(
        order.Id, order.AccountId, userId, symbol, order.Side, order.Type, order.Status, order.Quantity,
        order.ExecutedPrice, order.Commission, order.RejectReason, order.UpdatedAt);
}
