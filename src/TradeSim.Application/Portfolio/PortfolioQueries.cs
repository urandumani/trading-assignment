using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Common;
using TradeSim.Application.MarketData;
using TradeSim.Domain;
using TradeSim.Domain.Events;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.Portfolio;

public sealed record PortfolioSummary(
    Guid UserId,
    Guid AccountId,
    string AccountType,
    decimal MarginRate,
    decimal Cash,
    decimal ReservedCash,
    decimal HoldingsValue,
    decimal Equity,
    decimal BuyingPower,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    DateTimeOffset AsOf);

public sealed record PositionView(
    string Symbol,
    string Name,
    int Quantity,
    int ReservedQuantity,
    int AvailableQuantity,
    decimal AvgCost,
    decimal Price,
    decimal MarketValue,
    decimal UnrealizedPnl,
    decimal UnrealizedPnlPercent,
    decimal RealizedPnl);

public sealed record TransactionView(Guid Id, long Version, string Type, DateTimeOffset OccurredAt, string? CorrelationId, JsonElement Details);

public sealed record PerformanceView(
    decimal InitialCash,
    decimal Equity,
    decimal TotalReturn,
    decimal TotalReturnPercent,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal CommissionPaid,
    int TradeCount,
    IReadOnlyList<EquityPoint> EquityCurve);

public sealed record EquityPoint(DateTimeOffset Time, decimal Equity);

public sealed class PortfolioQueries(IAppDbContext db, InstrumentCatalog catalog, PriceBook priceBook, TimeProvider clock)
{
    public async Task<PortfolioSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken)
                      ?? throw new DomainException(ErrorCodes.NotFound, "Account not found.");
        var positions = await GetPositionsAsync(userId, cancellationToken);

        var holdingsValue = positions.Sum(p => p.MarketValue);
        var realized = await db.Positions.Where(p => p.AccountId == account.Id).SumAsync(p => p.RealizedPnl, cancellationToken);

        return new PortfolioSummary(
            userId,
            account.Id,
            account.RiskLimits.MarginRate < 1m ? "MARGIN" : "CASH",
            Math.Round(account.RiskLimits.MarginRate, 2),
            Cents(account.Cash),
            Cents(account.ReservedCash),
            Cents(holdingsValue),
            Cents(account.Equity(holdingsValue)),
            Cents(Math.Max(0m, account.BuyingPower(holdingsValue))),
            Cents(realized),
            Cents(positions.Sum(p => p.UnrealizedPnl)),
            clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<PositionView>> GetPositionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var accountId = await AccountIdAsync(userId, cancellationToken);
        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.AccountId == accountId && p.Quantity > 0)
            .ToListAsync(cancellationToken);

        var instruments = (await catalog.GetAllAsync(cancellationToken)).ToDictionary(i => i.Id);
        var prices = await priceBook.GetCurrentPricesAsync(cancellationToken);

        return positions
            .Select(p =>
            {
                var price = prices[p.InstrumentId];
                var marketValue = p.Quantity * price;
                var cost = p.Quantity * p.AvgCost;
                var unrealized = marketValue - cost;
                return new PositionView(
                    instruments[p.InstrumentId].Symbol,
                    instruments[p.InstrumentId].Name,
                    p.Quantity,
                    p.ReservedQuantity,
                    p.AvailableQuantity,
                    Math.Round(p.AvgCost, 4) / 1.0000m,
                    price,
                    Cents(marketValue),
                    Cents(unrealized),
                    cost == 0 ? 0 : Math.Round(unrealized / cost * 100, 2),
                    Cents(p.RealizedPnl));
            })
            .OrderBy(p => p.Symbol)
            .ToList();
    }

    public async Task<PagedResult<TransactionView>> GetTransactionsAsync(
        Guid userId, string? type, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Paging.Validate(page, pageSize);
        var accountId = await AccountIdAsync(userId, cancellationToken);

        var query = db.AccountEvents.AsNoTracking().Where(e => e.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            var storedType = KnownTypes.FirstOrDefault(t => string.Equals(ToApiType(t), type, StringComparison.OrdinalIgnoreCase))
                             ?? throw new DomainException(ErrorCodes.ValidationFailed,
                                 $"type must be one of: {string.Join(", ", KnownTypes.Select(ToApiType))}.");
            query = query.Where(e => e.Type == storedType);
        }

        var total = await query.CountAsync(cancellationToken);
        var events = await query
            .OrderByDescending(e => e.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = events
            .Select(e => new TransactionView(e.Id, e.Version, ToApiType(e.Type), e.OccurredAt, e.CorrelationId,
                JsonDocument.Parse(e.Payload).RootElement.Clone()))
            .ToList();
        return new PagedResult<TransactionView>(items, page, pageSize, total);
    }

    public async Task<PerformanceView> GetPerformanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var summary = await GetSummaryAsync(userId, cancellationToken);
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == summary.AccountId, cancellationToken);

        var filled = db.Orders.Where(o => o.AccountId == account.Id && o.Status == OrderStatus.Filled);
        var commissionPaid = await filled.SumAsync(o => o.Commission ?? 0m, cancellationToken);
        var tradeCount = await filled.CountAsync(cancellationToken);

        var openedAt = await db.AccountEvents
            .Where(e => e.AccountId == account.Id && e.Type == AccountEventTypes.AccountOpened)
            .Select(e => e.OccurredAt)
            .FirstAsync(cancellationToken);
        var snapshots = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => s.AccountId == account.Id)
            .OrderBy(s => s.TakenAt)
            .Select(s => new EquityPoint(s.TakenAt, s.Equity))
            .ToListAsync(cancellationToken);
        var curve = new List<EquityPoint> { new(openedAt, Cents(account.InitialCash)) };
        curve.AddRange(snapshots);
        curve.Add(new EquityPoint(summary.AsOf, summary.Equity));

        var totalReturn = summary.Equity - account.InitialCash;
        return new PerformanceView(
            Cents(account.InitialCash),
            summary.Equity,
            Cents(totalReturn),
            account.InitialCash == 0 ? 0 : Math.Round(totalReturn / account.InitialCash * 100, 2),
            summary.RealizedPnl,
            summary.UnrealizedPnl,
            Cents(commissionPaid),
            tradeCount,
            curve);
    }

    private static readonly string[] KnownTypes =
    [
        AccountEventTypes.AccountOpened, AccountEventTypes.OrderPlaced, AccountEventTypes.OrderFilled,
        AccountEventTypes.OrderCancelled, AccountEventTypes.OrderExpired, AccountEventTypes.OrderRejected,
    ];

    private static decimal Cents(decimal amount) => Math.Round(amount, 2);

    private static string ToApiType(string storedType) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(storedType);

    private async Task<Guid> AccountIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NotFound, "Account not found.");
}
