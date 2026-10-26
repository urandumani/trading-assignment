using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TradeSim.Application.Abstractions;
using TradeSim.Application.Common;
using TradeSim.Application.Events;
using TradeSim.Application.MarketData;
using TradeSim.Application.Orders;
using TradeSim.Domain.Market;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class RestingOrderTests(ApiFactory api)
{
    private async Task<int> TickAsync(string symbol, decimal price)
    {
        await api.Prices.SetPriceAsync(symbol, price);
        Guid instrumentId;
        await using (var db = api.CreateDbContext())
        {
            instrumentId = await db.Instruments.Where(i => i.Symbol == symbol).Select(i => i.Id).SingleAsync();
        }

        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RestingOrderService>().FillTriggeredAsync(instrumentId, symbol, price);
    }

    private static async Task<string> StatusAsync(HttpClient client, string orderId) =>
        (await (await client.GetAsync($"/api/v1/orders/{orderId}")).DataAsync(HttpStatusCode.OK)).Text("status");

    [Fact]
    public async Task A_limit_buy_fills_once_the_price_drops_to_its_limit()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("NOVA", 100m);
        var orderId = (await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "LIMIT", 10, limitPrice: 95m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");

        await TickAsync("NOVA", 95.01m);
        (await StatusAsync(client, orderId)).ShouldBe("OPEN");

        await TickAsync("NOVA", 94.80m);

        var order = await (await client.GetAsync($"/api/v1/orders/{orderId}")).DataAsync(HttpStatusCode.OK);
        order.Text("status").ShouldBe("FILLED");
        order.Decimal("executedPrice").ShouldBe(94.80m);
        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("reservedCash").ShouldBe(0m);
        summary.Decimal("cash").ShouldBe(100_000m - 948m - 1m);
    }

    [Fact]
    public async Task A_limit_sell_fills_once_the_price_rises_to_its_limit()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("LUXE", 300m);
        await (await client.PlaceOrderAsync(userId, "LUXE", "BUY", "MARKET", 10)).DataAsync(HttpStatusCode.Created);
        var orderId = (await (await client.PlaceOrderAsync(userId, "LUXE", "SELL", "LIMIT", 10, limitPrice: 310m))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");

        await TickAsync("LUXE", 309.99m);
        (await StatusAsync(client, orderId)).ShouldBe("OPEN");

        await TickAsync("LUXE", 311.50m);

        (await StatusAsync(client, orderId)).ShouldBe("FILLED");
        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("realizedPnl").ShouldBe(115m);
    }

    [Fact]
    public async Task A_stop_loss_sells_when_the_price_falls_through_its_trigger()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("MEDI", 100m);
        await (await client.PlaceOrderAsync(userId, "MEDI", "BUY", "MARKET", 50)).DataAsync(HttpStatusCode.Created);
        var orderId = (await (await client.PlaceOrderAsync(userId, "MEDI", "SELL", "STOP_LOSS", 50, triggerPrice: 90m))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");

        await TickAsync("MEDI", 90.01m);
        (await StatusAsync(client, orderId)).ShouldBe("OPEN");

        await TickAsync("MEDI", 89.50m);

        (await StatusAsync(client, orderId)).ShouldBe("FILLED");
        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("realizedPnl").ShouldBe(-525m);
        (await (await client.GetAsync($"/api/v1/portfolios/{userId}/positions")).DataAsync(HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_take_profit_sells_when_the_price_reaches_its_trigger()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("GRDX", 60m);
        await (await client.PlaceOrderAsync(userId, "GRDX", "BUY", "MARKET", 20)).DataAsync(HttpStatusCode.Created);
        var orderId = (await (await client.PlaceOrderAsync(userId, "GRDX", "SELL", "TAKE_PROFIT", 20, triggerPrice: 63m))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");

        await TickAsync("GRDX", 62.99m);
        (await StatusAsync(client, orderId)).ShouldBe("OPEN");

        await TickAsync("GRDX", 63.10m);

        (await StatusAsync(client, orderId)).ShouldBe("FILLED");
        (await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK)).Decimal("realizedPnl").ShouldBe(62m);
    }

    [Fact]
    public async Task A_cancelled_order_is_never_filled()
    {
        var (client, userId, _) = await api.NewUserAsync();
        var orderId = (await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "LIMIT", 10, limitPrice: 50m))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");
        await (await client.PutAsync($"/api/v1/orders/{orderId}/cancel", null)).DataAsync(HttpStatusCode.OK);

        await TickAsync("NOVA", 40m);

        (await StatusAsync(client, orderId)).ShouldBe("CANCELLED");
        (await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK)).Decimal("cash").ShouldBe(100_000m);
    }

    [Fact]
    public async Task Fills_are_announced_through_the_outbox_with_the_users_id()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("BRKX", 35m);
        await (await client.PlaceOrderAsync(userId, "BRKX", "BUY", "LIMIT", 3, limitPrice: 34m)).DataAsync(HttpStatusCode.Created);

        await TickAsync("BRKX", 33.90m);

        await using var db = api.CreateDbContext();
        var messages = await db.OutboxMessages.Where(m => m.Type == "order.filled").Select(m => m.Payload).ToListAsync();
        messages.ShouldContain(m => m.Contains(userId.ToString()) && m.Contains(accountId.ToString()));
    }

    [Fact]
    public async Task Day_orders_expire_after_their_session_but_gtc_orders_stay()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        var dayOrder = (await (await client.PlaceOrderAsync(userId, "AERO", "BUY", "LIMIT", 5, limitPrice: 20m, timeInForce: "DAY"))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");
        var gtcOrder = (await (await client.PlaceOrderAsync(userId, "AERO", "BUY", "LIMIT", 5, limitPrice: 21m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created)).Text("orderId");

        using (var scope = api.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var later = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(2));
            var expiry = new RestingOrderService(services.GetRequiredService<IAppDbContext>(), services.GetRequiredService<IOutbox>(),
                services.GetRequiredService<PriceBook>(),
                services.GetRequiredService<MarketHours>(), later, services.GetRequiredService<TradingMetrics>(),
                NullLogger<RestingOrderService>.Instance);
            (await expiry.ExpireDayOrdersAsync()).ShouldBeGreaterThanOrEqualTo(1);
        }

        (await StatusAsync(client, dayOrder)).ShouldBe("EXPIRED");
        (await StatusAsync(client, gtcOrder)).ShouldBe("OPEN");
        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("reservedCash").ShouldBe(106m);

        var expired = await (await client.GetAsync($"/api/v1/portfolios/{userId}/transactions?type=ORDER_EXPIRED")).DataAsync(HttpStatusCode.OK);
        expired.Int("totalCount").ShouldBe(1);

        await using var db = api.CreateDbContext();
        var replayed = AccountReplay.Rebuild(await db.AccountEvents.Where(e => e.AccountId == accountId).ToListAsync());
        replayed.ReservedCash.ShouldBe(106m);
    }
}
