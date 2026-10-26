using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Common;
using TradeSim.Application.Events;
using TradeSim.Application.MarketData;
using TradeSim.Domain;
using TradeSim.Domain.Events;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.Orders;

public sealed class CancelOrderService(IAppDbContext db, IOutbox outbox, InstrumentCatalog catalog, TimeProvider clock)
{
    public Task<OrderView> CancelAsync(Guid userId, Guid accountId, Guid orderId, string? correlationId, CancellationToken cancellationToken = default) =>
        AccountTransaction.ExecuteAsync(db, accountId, () => TryCancelAsync(userId, accountId, orderId, correlationId, cancellationToken), cancellationToken);

    private async Task<OrderView> TryCancelAsync(Guid userId, Guid accountId, Guid orderId, string? correlationId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.AccountId == accountId, cancellationToken)
                    ?? throw new DomainException(ErrorCodes.OrderNotFound, "Order not found.");
        var symbol = (await catalog.GetAllAsync(cancellationToken)).Single(i => i.Id == order.InstrumentId).Symbol;

        if (order.Status == OrderStatus.Cancelled)
        {
            return OrderView.From(order, symbol);
        }

        if (order.Status != OrderStatus.Open)
        {
            throw new DomainException(ErrorCodes.OrderNotOpen,
                $"The order is {order.Status.ToString().ToUpperInvariant()} and can no longer be cancelled.");
        }

        var now = clock.GetUtcNow();
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, cancellationToken);
        var position = await db.Positions.SingleOrDefaultAsync(p => p.AccountId == accountId && p.InstrumentId == order.InstrumentId, cancellationToken);

        var released = Settlement.ReleaseReservation(account, position, order);
        order.Cancel(now);

        await AccountEventLog.AppendAsync(db, accountId, correlationId, now,
            [(AccountEventTypes.OrderCancelled, new OrderClosedPayload(order.Id, symbol, released.Cash, released.Shares, null))],
            cancellationToken);
        var message = OrderStatusChanged.From(order, userId, symbol);
        outbox.Enqueue(message.RoutingKey, message, correlationId);

        await db.SaveChangesAsync(cancellationToken);
        return OrderView.From(order, symbol);
    }
}
