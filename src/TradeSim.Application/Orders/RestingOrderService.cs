using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Common;
using TradeSim.Application.Events;
using TradeSim.Application.MarketData;
using TradeSim.Domain;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;

namespace TradeSim.Application.Orders;

public sealed class RestingOrderService(
    IAppDbContext db,
    IOutbox outbox,
    PriceBook priceBook,
    MarketHours hours,
    TimeProvider clock,
    TradingMetrics metrics,
    ILogger<RestingOrderService> logger)
{
    public async Task<int> FillTriggeredAsync(Guid instrumentId, string symbol, decimal price, CancellationToken cancellationToken = default)
    {
        var candidates = await db.Orders.AsNoTracking()
            .Where(o => o.InstrumentId == instrumentId && o.Status == OrderStatus.Open)
            .Where(o => (o.Type == OrderType.Limit && o.Side == OrderSide.Buy && o.LimitPrice >= price)
                        || (o.Type == OrderType.Limit && o.Side == OrderSide.Sell && o.LimitPrice <= price)
                        || (o.Type == OrderType.StopLoss && o.TriggerPrice >= price)
                        || (o.Type == OrderType.TakeProfit && o.TriggerPrice <= price))
            .OrderBy(o => o.CreatedAt)
            .Select(o => new { o.Id, o.AccountId })
            .ToListAsync(cancellationToken);

        var filled = 0;
        foreach (var candidate in candidates)
        {
            var didFill = await AccountTransaction.ExecuteAsync(db, candidate.AccountId,
                () => FillOneAsync(candidate.Id, symbol, price, cancellationToken), cancellationToken);
            db.ChangeTracker.Clear();
            filled += didFill ? 1 : 0;
        }

        return filled;
    }

    public async Task<int> ExpireDayOrdersAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var open = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Open && o.TimeInForce == TimeInForce.Day)
            .Select(o => new { o.Id, o.AccountId, o.CreatedAt })
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var order in open.Where(o => hours.DayOrderExpiry(o.CreatedAt) <= now))
        {
            var didExpire = await AccountTransaction.ExecuteAsync(db, order.AccountId,
                () => CloseOneAsync(order.Id, AccountEventTypes.OrderExpired, reason: null, cancellationToken), cancellationToken);
            db.ChangeTracker.Clear();
            expired += didExpire ? 1 : 0;
        }

        return expired;
    }

    private async Task<bool> FillOneAsync(Guid orderId, string symbol, decimal price, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        if (order.Status != OrderStatus.Open)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        if (order.TimeInForce == TimeInForce.Day && hours.DayOrderExpiry(order.CreatedAt) <= now)
        {
            await CloseOneAsync(orderId, AccountEventTypes.OrderExpired, reason: null, cancellationToken);
            return false;
        }

        if (!hours.IsOpen(now) || !order.IsTriggeredBy(price))
        {
            return false;
        }

        var account = await db.Accounts.SingleAsync(a => a.Id == order.AccountId, cancellationToken);
        if (order.Side == OrderSide.Buy && !await CanAffordAsync(account, order, price, cancellationToken))
        {
            await CloseOneAsync(orderId, AccountEventTypes.OrderRejected, ErrorCodes.InsufficientBuyingPower, cancellationToken);
            return false;
        }

        var position = await db.Positions.SingleOrDefaultAsync(
            p => p.AccountId == order.AccountId && p.InstrumentId == order.InstrumentId, cancellationToken);
        if (position is null)
        {
            position = Position.Open(order.AccountId, order.InstrumentId);
            db.Positions.Add(position);
        }

        FillDetails fill;
        try
        {
            fill = Settlement.Fill(account, position, order, price, now);
        }
        catch (DomainException ex)
        {
            logger.LogWarning("Rejecting order {OrderId}: {Reason}", orderId, ex.Message);
            db.ChangeTracker.Clear();
            await CloseOneAsync(orderId, AccountEventTypes.OrderRejected, ex.Code, cancellationToken);
            return false;
        }

        await AccountEventLog.AppendAsync(db, account.Id, correlationId: null, now,
            [(AccountEventTypes.OrderFilled, OrderFilledPayload.From(order, symbol, fill, account, position))], cancellationToken);
        var message = OrderStatusChanged.From(order, account.UserId, symbol);
        outbox.Enqueue(message.RoutingKey, message, correlationId: null);

        await db.SaveChangesAsync(cancellationToken);

        metrics.TradeExecuted(symbol, order.Side, order.Quantity, price);
        logger.LogInformation("Filled {Type} {Side} order {OrderId}: {Quantity} {Symbol} at {Price}",
            order.Type, order.Side, order.Id, order.Quantity, symbol, price);
        return true;
    }

    private async Task<bool> CanAffordAsync(Account account, Order order, decimal price, CancellationToken cancellationToken)
    {
        decimal holdingsValue = 0;
        if (account.RiskLimits.MarginRate < 1m)
        {
            var holdings = await db.Positions.AsNoTracking()
                .Where(p => p.AccountId == account.Id && p.Quantity > 0)
                .Select(p => new { p.InstrumentId, p.Quantity })
                .ToListAsync(cancellationToken);
            var currentPrices = await priceBook.GetCurrentPricesAsync(cancellationToken);
            holdingsValue = holdings.Sum(p => p.Quantity * (p.InstrumentId == order.InstrumentId ? price : currentPrices[p.InstrumentId]));
        }

        var available = account.BuyingPower(holdingsValue) + order.ReservedAmount;
        var required = order.Quantity * price + Commission.For(order.Quantity) / account.RiskLimits.MarginRate;
        return required <= available;
    }

    private async Task<bool> CloseOneAsync(Guid orderId, string eventType, string? reason, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        if (order.Status != OrderStatus.Open)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var account = await db.Accounts.SingleAsync(a => a.Id == order.AccountId, cancellationToken);
        var position = await db.Positions.SingleOrDefaultAsync(
            p => p.AccountId == order.AccountId && p.InstrumentId == order.InstrumentId, cancellationToken);
        var symbol = await db.Instruments.Where(i => i.Id == order.InstrumentId).Select(i => i.Symbol).SingleAsync(cancellationToken);

        var released = Settlement.ReleaseReservation(account, position, order);
        if (eventType == AccountEventTypes.OrderExpired)
        {
            order.Expire(now);
        }
        else
        {
            order.Reject(reason ?? "REJECTED", now);
        }

        await AccountEventLog.AppendAsync(db, account.Id, correlationId: null, now,
            [(eventType, new OrderClosedPayload(order.Id, symbol, released.Cash, released.Shares, reason))], cancellationToken);
        var message = OrderStatusChanged.From(order, account.UserId, symbol);
        outbox.Enqueue(message.RoutingKey, message, correlationId: null);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
