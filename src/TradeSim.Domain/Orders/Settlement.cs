using TradeSim.Domain.Accounts;
using TradeSim.Domain.Portfolio;

namespace TradeSim.Domain.Orders;

public sealed record FillDetails(
    decimal Price,
    int Quantity,
    decimal Commission,
    decimal CashChange,
    decimal ReleasedCash,
    int ReleasedShares,
    decimal RealizedPnl);

public sealed record Reservation(decimal Cash, int Shares);

public static class Settlement
{
    public static FillDetails Fill(Account account, Position position, Order order, decimal price, DateTimeOffset now)
    {
        if (position.AccountId != account.Id || order.AccountId != account.Id || position.InstrumentId != order.InstrumentId)
        {
            throw new InvalidOperationException("Order, account and position don't belong together.");
        }

        var released = ReleaseReservation(account, position, order);
        var commission = Commission.For(order.Quantity);
        var notional = order.Quantity * price;
        decimal cashChange;
        decimal realizedPnl = 0;

        if (order.Side == OrderSide.Buy)
        {
            cashChange = -(notional + commission);
            position.ApplyBuy(order.Quantity, price);
        }
        else
        {
            cashChange = notional - commission;
            realizedPnl = position.ApplySell(order.Quantity, price);
        }

        account.Credit(cashChange);
        order.Fill(price, commission, now);

        return new FillDetails(price, order.Quantity, commission, cashChange, released.Cash, released.Shares, realizedPnl);
    }

    public static Reservation ReleaseReservation(Account account, Position? position, Order order)
    {
        if (order.Side == OrderSide.Buy)
        {
            if (order.ReservedAmount > 0)
            {
                account.ReleaseCash(order.ReservedAmount);
            }

            return new Reservation(order.ReservedAmount, 0);
        }

        if (order.Type == OrderType.Market)
        {
            return new Reservation(0, 0);
        }

        if (position is null)
        {
            throw new InvalidOperationException("A resting SELL order must have a position holding its shares.");
        }

        position.ReleaseShares(order.Quantity);
        return new Reservation(0, order.Quantity);
    }

    public static Reservation Reserve(Account account, Position? position, Order order)
    {
        if (order.Type == OrderType.Market)
        {
            return new Reservation(0, 0);
        }

        if (order.Side == OrderSide.Buy)
        {
            account.ReserveCash(order.ReservedAmount);
            return new Reservation(order.ReservedAmount, 0);
        }

        if (position is null)
        {
            throw new DomainException(ErrorCodes.InsufficientPosition, "You hold no shares of this instrument.");
        }

        position.ReserveShares(order.Quantity);
        return new Reservation(0, order.Quantity);
    }
}
