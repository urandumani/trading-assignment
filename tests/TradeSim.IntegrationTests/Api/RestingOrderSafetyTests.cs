using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TradeSim.Application.Events;
using TradeSim.Application.Orders;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class RestingOrderSafetyTests(ApiFactory api)
{
    [Theory]
    [InlineData(10, 100, OrderStatus.Rejected)]
    [InlineData(100, 100, OrderStatus.Filled)]
    [InlineData(10, 10, OrderStatus.Filled)]
    public async Task A_margin_buy_is_checked_again_using_current_collateral_and_its_own_reservation(
        decimal collateralPrice, decimal fillPrice, OrderStatus expected)
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await using (var db = api.CreateDbContext())
        {
            var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
            db.Entry(account.RiskLimits).Property(l => l.MarginRate).CurrentValue = 0.5m;
            await db.SaveChangesAsync();
        }
        await api.Prices.SetPriceAsync("APXL", 100m);
        await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 900)).DataAsync(HttpStatusCode.Created);
        await api.Prices.SetPriceAsync("NOVA", 150m);
        var placed = await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "LIMIT", 1000, limitPrice: 100m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created);
        var orderId = placed.GetProperty("orderId").GetGuid();
        await api.Prices.SetPriceAsync("APXL", collateralPrice);
        await api.Prices.SetPriceAsync("NOVA", fillPrice);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradeSimDbContext>();
            var nova = await db.Instruments.SingleAsync(i => i.Symbol == "NOVA");
            (await scope.ServiceProvider.GetRequiredService<RestingOrderService>().FillTriggeredAsync(nova.Id, "NOVA", fillPrice))
                .ShouldBe(expected == OrderStatus.Filled ? 1 : 0);
        }

        await using var check = api.CreateDbContext();
        var saved = await check.Orders.SingleAsync(o => o.Id == orderId);
        saved.Status.ShouldBe(expected);
        var current = await check.Accounts.SingleAsync(a => a.Id == accountId);
        current.ReservedCash.ShouldBe(0m);
        if (expected == OrderStatus.Rejected)
        {
            saved.RejectReason.ShouldBe("INSUFFICIENT_BUYING_POWER");
            current.Cash.ShouldBe(9_995.50m);
            (await check.Positions.CountAsync(p => p.AccountId == accountId)).ShouldBe(1);
            (await check.AccountEvents.CountAsync(e => e.AccountId == accountId && e.Type == AccountEventTypes.OrderRejected)).ShouldBe(1);
            (await check.OutboxMessages.Where(m => m.Type == "order.rejected").Select(m => m.Payload).ToListAsync())
                .Count(p => p.Contains(orderId.ToString())).ShouldBe(1);
        }
        else
        {
            current.Cash.ShouldBe(9_995.50m - 1000 * fillPrice - 5m);
            current.BuyingPower(900 * collateralPrice + 1000 * fillPrice).ShouldBeGreaterThanOrEqualTo(0m);
        }
        await AssertReplayAsync(accountId);
    }

    [Fact]
    public async Task A_cash_buy_can_use_its_reservation_without_spending_another_orders_reservation()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 10, limitPrice: 9m, timeInForce: "GTC")).DataAsync(HttpStatusCode.Created);
        var nova = await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "LIMIT", 999, limitPrice: 100m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created);
        await using var scope = api.Services.CreateAsyncScope();
        await using var lookup = api.CreateDbContext();
        var instrument = await lookup.Instruments.SingleAsync(i => i.Symbol == "NOVA");
        (await scope.ServiceProvider.GetRequiredService<RestingOrderService>().FillTriggeredAsync(instrument.Id, "NOVA", 99m)).ShouldBe(1);
        await using var db = api.CreateDbContext();
        (await db.Orders.SingleAsync(o => o.Id == nova.GetProperty("orderId").GetGuid())).Status.ShouldBe(OrderStatus.Filled);
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        account.Cash.ShouldBe(1_094m);
        account.ReservedCash.ShouldBe(91m);
        await AssertReplayAsync(accountId);
    }

    [Fact]
    public async Task A_margin_fill_also_accounts_for_the_commissions_effect_on_equity()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await using (var db = api.CreateDbContext())
        {
            var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
            db.Entry(account.RiskLimits).Property(l => l.MarginRate).CurrentValue = 0.5m;
            await db.SaveChangesAsync();
        }
        await api.Prices.SetPriceAsync("NOVA", 200m);
        var placed = await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "LIMIT", 1007, limitPrice: 198.60m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created);
        await api.Prices.SetPriceAsync("NOVA", 198.60m);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradeSimDbContext>();
            var instrumentId = await db.Instruments.Where(i => i.Symbol == "NOVA").Select(i => i.Id).SingleAsync();
            (await scope.ServiceProvider.GetRequiredService<RestingOrderService>().FillTriggeredAsync(instrumentId, "NOVA", 198.60m)).ShouldBe(0);
        }
        await using var check = api.CreateDbContext();
        (await check.Orders.SingleAsync(o => o.Id == placed.GetProperty("orderId").GetGuid())).Status.ShouldBe(OrderStatus.Rejected);
        var saved = await check.Accounts.SingleAsync(a => a.Id == accountId);
        saved.Cash.ShouldBe(100_000m);
        saved.ReservedCash.ShouldBe(0m);
        await AssertReplayAsync(accountId);
    }

    [Theory]
    [InlineData(OrderSide.Buy, "2026-10-23T14:00:00Z")]
    [InlineData(OrderSide.Sell, "2026-10-23T14:00:00Z")]
    [InlineData(OrderSide.Buy, "2026-10-26T09:00:00Z")]
    [InlineData(OrderSide.Sell, "2026-10-26T09:00:00Z")]
    public async Task A_day_order_expires_inside_the_fill_transaction_before_any_sweep(OrderSide side, string fillAt)
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 100m);
        if (side == OrderSide.Sell)
            await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 1)).DataAsync(HttpStatusCode.Created);
        var friday = DateTimeOffset.Parse("2026-10-23T10:00:00Z");
        Guid orderId, instrumentId;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var placer = ActivatorUtilities.CreateInstance<PlaceOrderService>(scope.ServiceProvider, Hours(), new FakeTimeProvider(friday));
            var result = await placer.PlaceAsync(new PlaceOrderCommand(userId, accountId, "APXL", side, OrderType.Limit,
                TimeInForce.Day, 1, 100m, null, null, null));
            orderId = result.Order.OrderId;
            var db = scope.ServiceProvider.GetRequiredService<TradeSimDbContext>();
            instrumentId = await db.Instruments.Where(i => i.Symbol == "APXL").Select(i => i.Id).SingleAsync();
        }
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var filler = ActivatorUtilities.CreateInstance<RestingOrderService>(scope.ServiceProvider, Hours(), new FakeTimeProvider(DateTimeOffset.Parse(fillAt)));
            (await filler.FillTriggeredAsync(instrumentId, "APXL", 100m)).ShouldBe(0);
        }
        await using var check = api.CreateDbContext();
        (await check.Orders.SingleAsync(o => o.Id == orderId)).Status.ShouldBe(OrderStatus.Expired);
        var account = await check.Accounts.SingleAsync(a => a.Id == accountId);
        account.ReservedCash.ShouldBe(0m);
        account.Cash.ShouldBe(side == OrderSide.Buy ? 100_000m : 99_899m);
        (await check.Positions.Where(p => p.AccountId == accountId).SumAsync(p => p.ReservedQuantity)).ShouldBe(0);
        (await check.AccountEvents.CountAsync(e => e.AccountId == accountId && e.Type == AccountEventTypes.OrderExpired)).ShouldBe(1);
        (await check.OutboxMessages.Where(m => m.Type == "order.expired").Select(m => m.Payload).ToListAsync())
            .Count(p => p.Contains(orderId.ToString())).ShouldBe(1);
        await AssertReplayAsync(accountId);
    }

    [Fact]
    public async Task A_gtc_order_waits_while_closed_and_can_fill_in_the_next_session()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        var order = await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 1, limitPrice: 100m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created);
        var id = order.GetProperty("orderId").GetGuid();
        Guid instrumentId;
        await using (var db = api.CreateDbContext())
            instrumentId = await db.Instruments.Where(i => i.Symbol == "APXL").Select(i => i.Id).SingleAsync();
        foreach (var (at, fills) in new[] { ("2026-10-23T14:00:00Z", 0), ("2026-10-26T09:00:00Z", 1) })
        {
            await using var scope = api.Services.CreateAsyncScope();
            var service = ActivatorUtilities.CreateInstance<RestingOrderService>(scope.ServiceProvider, Hours(), new FakeTimeProvider(DateTimeOffset.Parse(at)));
            (await service.FillTriggeredAsync(instrumentId, "APXL", 99m)).ShouldBe(fills);
        }
        await using var check = api.CreateDbContext();
        (await check.Orders.SingleAsync(o => o.Id == id)).Status.ShouldBe(OrderStatus.Filled);
        await AssertReplayAsync(accountId);
    }

    private static MarketHours Hours() => new(MarketHours.Cest);

    private async Task AssertReplayAsync(Guid accountId)
    {
        await using var db = api.CreateDbContext();
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        var replay = AccountReplay.Rebuild(await db.AccountEvents.Where(e => e.AccountId == accountId).ToListAsync());
        replay.Cash.ShouldBe(account.Cash);
        replay.ReservedCash.ShouldBe(account.ReservedCash);
        var positions = await db.Positions.Where(p => p.AccountId == accountId)
            .Join(db.Instruments, p => p.InstrumentId, i => i.Id, (p, i) => new { i.Symbol, p.Quantity, p.ReservedQuantity }).ToListAsync();
        foreach (var position in positions)
        {
            replay.Quantities.GetValueOrDefault(position.Symbol).ShouldBe(position.Quantity);
            replay.ReservedQuantities.GetValueOrDefault(position.Symbol).ShouldBe(position.ReservedQuantity);
        }
    }
}
