using Microsoft.EntityFrameworkCore;
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

public sealed record PlaceOrderCommand(
    Guid UserId,
    Guid AccountId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    TimeInForce TimeInForce,
    int Quantity,
    decimal? LimitPrice,
    decimal? TriggerPrice,
    string? IdempotencyKey,
    string? CorrelationId);

public sealed record PlaceOrderResult(OrderView Order, bool Replayed);

public sealed class PlaceOrderService(
    IAppDbContext db,
    IOutbox outbox,
    InstrumentCatalog catalog,
    IPriceReader prices,
    PriceBook priceBook,
    MarketHours hours,
    TimeProvider clock,
    TradingMetrics metrics)
{
    public async Task<PlaceOrderResult> PlaceAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        var orderId = Guid.CreateVersion7();
        try
        {
            return await AccountTransaction.ExecuteAsync(db, command.AccountId, () => TryPlaceAsync(command, orderId, cancellationToken), cancellationToken);
        }
        catch (DomainException ex)
        {
            metrics.OrderRejected(ex.Code);
            throw;
        }
    }

    private async Task<PlaceOrderResult> TryPlaceAsync(PlaceOrderCommand command, Guid orderId, CancellationToken cancellationToken)
    {
        var existing = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.AccountId == command.AccountId
                && (o.Id == orderId || command.IdempotencyKey != null && o.IdempotencyKey == command.IdempotencyKey), cancellationToken);
        if (existing is not null)
        {
            var symbol = (await catalog.GetAllAsync(cancellationToken)).Single(i => i.Id == existing.InstrumentId).Symbol;
            return new PlaceOrderResult(OrderView.From(existing, symbol), Replayed: existing.Id != orderId);
        }

        var instrument = await catalog.GetAsync(command.Symbol, cancellationToken);
        var now = clock.GetUtcNow();

        var order = Order.Create(command.AccountId, instrument.Id, command.Side, command.Type, command.TimeInForce,
            command.Quantity, command.LimitPrice, command.TriggerPrice, command.IdempotencyKey, now, orderId);

        var marketPrice = await CheckMarketAsync(instrument, order, now, cancellationToken);

        var account = await db.Accounts.SingleAsync(a => a.Id == command.AccountId, cancellationToken);
        var position = await db.Positions.SingleOrDefaultAsync(
            p => p.AccountId == account.Id && p.InstrumentId == instrument.Id, cancellationToken);

        await CheckRiskLimitsAsync(account, position, order, marketPrice, cancellationToken);
        await CheckFundsAsync(account, position, order, marketPrice, cancellationToken);

        db.Orders.Add(order);
        var reserved = Settlement.Reserve(account, position, order);
        var events = new List<(string, object)>
        {
            (AccountEventTypes.OrderPlaced, new OrderPlacedPayload(order.Id, instrument.Symbol, order.Side, order.Type,
                order.TimeInForce, order.Quantity, order.LimitPrice, order.TriggerPrice, reserved.Cash, reserved.Shares)),
        };

        if (order.Type == OrderType.Market)
        {
            if (position is null)
            {
                position = Position.Open(account.Id, instrument.Id);
                db.Positions.Add(position);
            }

            var fill = Settlement.Fill(account, position, order, marketPrice!.Value, now);
            events.Add((AccountEventTypes.OrderFilled, OrderFilledPayload.From(order, instrument.Symbol, fill, account, position)));
        }

        await AccountEventLog.AppendAsync(db, account.Id, command.CorrelationId, now, events, cancellationToken);
        var message = OrderStatusChanged.From(order, command.UserId, instrument.Symbol);
        outbox.Enqueue(order.Type == OrderType.Market ? message.RoutingKey : "order.placed", message, command.CorrelationId);

        await db.SaveChangesAsync(cancellationToken);

        metrics.OrderPlaced(order.Type);
        if (order.Status == OrderStatus.Filled)
        {
            metrics.TradeExecuted(instrument.Symbol, order.Side, order.Quantity, order.ExecutedPrice!.Value);
        }

        return new PlaceOrderResult(OrderView.From(order, instrument.Symbol), Replayed: false);
    }

    private async Task<decimal?> CheckMarketAsync(InstrumentDetails instrument, Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!hours.IsOpen(now))
        {
            var next = hours.NextSession(now);
            throw new DomainException(ErrorCodes.MarketClosed,
                $"The market is closed. It opens at 09:30 CEST ({next.Open:yyyy-MM-ddTHH:mm}Z).");
        }

        var tick = await prices.GetAsync(instrument.Symbol, cancellationToken);
        if (tick is { Halted: true, FromFallback: false } && tick.HaltedUntil > now)
        {
            throw new DomainException(ErrorCodes.InstrumentHalted,
                $"{instrument.Symbol} is halted by the circuit breaker until {tick.HaltedUntil:HH:mm:ss}Z.");
        }

        if (order.Quantity < instrument.MinQuantity)
        {
            throw new DomainException(ErrorCodes.OrderTooSmall,
                $"The minimum order for {instrument.Symbol} is {instrument.MinQuantity} shares.");
        }

        var freshPrice = tick is not null && tick.IsLive(now) ? tick.Price : (decimal?)null;
        if (order.Type == OrderType.Market && freshPrice is null)
        {
            throw new DomainException(ErrorCodes.MarketDataUnavailable,
                $"No current price for {instrument.Symbol}; MARKET orders are paused. LIMIT orders are still accepted.");
        }

        return freshPrice;
    }

    private async Task CheckRiskLimitsAsync(Account account, Position? position, Order order, decimal? marketPrice, CancellationToken cancellationToken)
    {
        var limits = account.RiskLimits;

        var referencePrice = order.Type switch
        {
            OrderType.Market => marketPrice!.Value,
            OrderType.Limit => order.LimitPrice!.Value,
            _ => order.TriggerPrice!.Value,
        };
        var orderValue = order.Quantity * referencePrice;
        if (orderValue > limits.MaxOrderValue)
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded,
                $"Order value {orderValue:0.00} exceeds the limit of {limits.MaxOrderValue:0.00} per order.");
        }

        if (order.Side == OrderSide.Buy)
        {
            var openBuyQuantity = await db.Orders
                .Where(o => o.AccountId == account.Id && o.InstrumentId == order.InstrumentId
                            && o.Status == OrderStatus.Open && o.Side == OrderSide.Buy)
                .SumAsync(o => o.Quantity, cancellationToken);
            var resultingQuantity = (position?.Quantity ?? 0) + openBuyQuantity + order.Quantity;
            if (resultingQuantity > limits.MaxPositionQuantity)
            {
                throw new DomainException(ErrorCodes.RiskLimitExceeded,
                    $"Position limit: holding plus open buy orders would reach {resultingQuantity} shares (max {limits.MaxPositionQuantity}).");
            }
        }

        if (order.Type != OrderType.Market)
        {
            var openOrders = await db.Orders.CountAsync(o => o.AccountId == account.Id && o.Status == OrderStatus.Open, cancellationToken);
            if (openOrders >= limits.MaxOpenOrders)
            {
                throw new DomainException(ErrorCodes.RiskLimitExceeded, $"You already have {openOrders} open orders (max {limits.MaxOpenOrders}).");
            }
        }
    }

    private async Task CheckFundsAsync(Account account, Position? position, Order order, decimal? marketPrice, CancellationToken cancellationToken)
    {
        if (order.Side == OrderSide.Sell)
        {
            var available = position?.AvailableQuantity ?? 0;
            if (order.Quantity > available)
            {
                throw new DomainException(ErrorCodes.InsufficientPosition,
                    $"You can sell at most {available} shares (shares held by other open orders don't count).");
            }

            return;
        }

        var required = order.Type == OrderType.Market
            ? order.Quantity * marketPrice!.Value + Commission.For(order.Quantity)
            : order.ReservedAmount;
        var buyingPower = account.BuyingPower(await HoldingsValueAsync(account.Id, cancellationToken));
        if (required > buyingPower)
        {
            throw new DomainException(ErrorCodes.InsufficientBuyingPower,
                $"This order needs {required:0.00} but buying power is {buyingPower:0.00}.");
        }
    }

    private async Task<decimal> HoldingsValueAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var holdings = await db.Positions.AsNoTracking()
            .Where(p => p.AccountId == accountId && p.Quantity > 0)
            .Select(p => new { p.InstrumentId, p.Quantity })
            .ToListAsync(cancellationToken);
        if (holdings.Count == 0)
        {
            return 0m;
        }

        var currentPrices = await priceBook.GetCurrentPricesAsync(cancellationToken);
        return holdings.Sum(h => h.Quantity * currentPrices[h.InstrumentId]);
    }
}
