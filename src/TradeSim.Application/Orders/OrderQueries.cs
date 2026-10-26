using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Common;
using TradeSim.Application.MarketData;
using TradeSim.Domain;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.Orders;

public sealed class OrderQueries(IAppDbContext db, InstrumentCatalog catalog)
{
    public async Task<OrderView> GetAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.AccountId == accountId, cancellationToken)
                    ?? throw new DomainException(ErrorCodes.OrderNotFound, "Order not found.");
        return OrderView.From(order, await SymbolAsync(order.InstrumentId, cancellationToken));
    }

    public async Task<PagedResult<OrderView>> ListAsync(
        Guid accountId, OrderStatus? status, string? symbol, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Paging.Validate(page, pageSize);

        var query = db.Orders.AsNoTracking().Where(o => o.AccountId == accountId);
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var instrumentId = (await catalog.GetAsync(symbol, cancellationToken)).Id;
            query = query.Where(o => o.InstrumentId == instrumentId);
        }

        var total = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var symbols = (await catalog.GetAllAsync(cancellationToken)).ToDictionary(i => i.Id, i => i.Symbol);
        return new PagedResult<OrderView>(orders.Select(o => OrderView.From(o, symbols[o.InstrumentId])).ToList(), page, pageSize, total);
    }

    private async Task<string> SymbolAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        (await catalog.GetAllAsync(cancellationToken)).Single(i => i.Id == instrumentId).Symbol;
}
