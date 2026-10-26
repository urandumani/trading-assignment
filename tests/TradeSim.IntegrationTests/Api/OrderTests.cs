using System.Net;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class OrderTests(ApiFactory api)
{
    [Fact]
    public async Task A_market_buy_fills_at_the_current_price_and_shows_in_the_portfolio()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 150.00m);

        var response = await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 100);

        var order = await response.DataAsync(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/orders/{order.Text("orderId")}");
        order.Text("status").ShouldBe("FILLED");
        order.Decimal("executedPrice").ShouldBe(150.00m);
        order.Int("executedQuantity").ShouldBe(100);
        order.Decimal("commission").ShouldBe(1.00m);
        order.GetProperty("executedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));

        await api.Prices.SetPriceAsync("APXL", 155.00m);
        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("cash").ShouldBe(84_999.00m);
        summary.Decimal("holdingsValue").ShouldBe(15_500.00m);
        summary.Decimal("equity").ShouldBe(100_499.00m);
        summary.Decimal("unrealizedPnl").ShouldBe(500.00m);

        var position = (await (await client.GetAsync($"/api/v1/portfolios/{userId}/positions")).DataAsync(HttpStatusCode.OK))[0];
        position.Text("symbol").ShouldBe("APXL");
        position.Int("quantity").ShouldBe(100);
        position.Decimal("avgCost").ShouldBe(150m);
        position.Decimal("unrealizedPnlPercent").ShouldBe(3.33m);
    }

    [Fact]
    public async Task A_market_sell_receives_the_proceeds_and_realizes_profit()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("NOVA", 100.00m);
        await (await client.PlaceOrderAsync(userId, "NOVA", "BUY", "MARKET", 100)).DataAsync(HttpStatusCode.Created);

        await api.Prices.SetPriceAsync("NOVA", 110.00m);
        await (await client.PlaceOrderAsync(userId, "NOVA", "SELL", "MARKET", 40)).DataAsync(HttpStatusCode.Created);

        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("cash").ShouldBe(100_000m - 10_001m + 4_399m);
        summary.Decimal("realizedPnl").ShouldBe(400m);
        var position = (await (await client.GetAsync($"/api/v1/portfolios/{userId}/positions")).DataAsync(HttpStatusCode.OK))[0];
        position.Int("quantity").ShouldBe(60);
    }

    [Fact]
    public async Task A_limit_buy_holds_cash_until_cancelled()
    {
        var (client, userId, _) = await api.NewUserAsync();

        var order = await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 10, limitPrice: 140.00m, timeInForce: "GTC"))
            .DataAsync(HttpStatusCode.Created);
        order.Text("status").ShouldBe("OPEN");
        order.Text("timeInForce").ShouldBe("GTC");
        var orderId = order.Text("orderId");

        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Decimal("reservedCash").ShouldBe(1_401.00m);
        summary.Decimal("buyingPower").ShouldBe(98_599.00m);

        var cancelled = await (await client.PutAsync($"/api/v1/orders/{orderId}/cancel", null)).DataAsync(HttpStatusCode.OK);
        cancelled.Text("status").ShouldBe("CANCELLED");
        (await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK)).Decimal("reservedCash").ShouldBe(0m);

        (await (await client.PutAsync($"/api/v1/orders/{orderId}/cancel", null)).DataAsync(HttpStatusCode.OK)).Text("status").ShouldBe("CANCELLED");
        (await (await client.GetAsync($"/api/v1/orders/{orderId}")).DataAsync(HttpStatusCode.OK)).Text("status").ShouldBe("CANCELLED");
    }

    [Fact]
    public async Task Stop_loss_and_take_profit_hold_shares_so_they_cant_be_sold_twice()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("QNTM", 100.00m);
        await (await client.PlaceOrderAsync(userId, "QNTM", "BUY", "MARKET", 50)).DataAsync(HttpStatusCode.Created);

        (await (await client.PlaceOrderAsync(userId, "QNTM", "SELL", "STOP_LOSS", 30, triggerPrice: 90m)).DataAsync(HttpStatusCode.Created))
            .Text("orderType").ShouldBe("STOP_LOSS");
        (await (await client.PlaceOrderAsync(userId, "QNTM", "SELL", "TAKE_PROFIT", 20, triggerPrice: 120m)).DataAsync(HttpStatusCode.Created))
            .Text("status").ShouldBe("OPEN");

        var position = (await (await client.GetAsync($"/api/v1/portfolios/{userId}/positions")).DataAsync(HttpStatusCode.OK))[0];
        position.Int("reservedQuantity").ShouldBe(50);
        position.Int("availableQuantity").ShouldBe(0);

        (await (await client.PlaceOrderAsync(userId, "QNTM", "SELL", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("INSUFFICIENT_POSITION");
    }

    [Fact]
    public async Task Resending_an_idempotency_key_returns_the_original_order()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("GRDX", 60.00m);
        var key = Guid.NewGuid().ToString();

        var first = await (await client.PlaceOrderAsync(userId, "GRDX", "BUY", "MARKET", 10, idempotencyKey: key)).DataAsync(HttpStatusCode.Created);
        var second = await (await client.PlaceOrderAsync(userId, "GRDX", "BUY", "MARKET", 10, idempotencyKey: key)).DataAsync(HttpStatusCode.OK);

        second.Text("orderId").ShouldBe(first.Text("orderId"));
        await using var db = api.CreateDbContext();
        (await db.Orders.CountAsync(o => o.AccountId == accountId)).ShouldBe(1);
        (await db.Accounts.SingleAsync(a => a.Id == accountId)).Cash.ShouldBe(100_000m - 601m);
    }

    [Fact]
    public async Task A_margin_account_can_buy_more_than_its_cash()
    {
        var (client, userId, _) = await api.LoginAsync("trader5");
        await api.Prices.SetPriceAsync("LUXE", 150.00m);

        await (await client.PlaceOrderAsync(userId, "LUXE", "BUY", "MARKET", 1000)).DataAsync(HttpStatusCode.Created);

        var summary = await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK);
        summary.Text("accountType").ShouldBe("MARGIN");
        summary.Decimal("cash").ShouldBe(-50_005.00m);
        summary.Decimal("equity").ShouldBe(99_995.00m);
    }

    [Fact]
    public async Task Orders_can_be_listed_and_filtered_by_status()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("BRKX", 35.00m);
        await (await client.PlaceOrderAsync(userId, "BRKX", "BUY", "MARKET", 5)).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "BRKX", "BUY", "LIMIT", 5, limitPrice: 30m)).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "BRKX", "BUY", "LIMIT", 5, limitPrice: 31m)).DataAsync(HttpStatusCode.Created);

        var all = await (await client.GetAsync($"/api/v1/users/{userId}/orders")).DataAsync(HttpStatusCode.OK);
        all.Int("totalCount").ShouldBe(3);
        all.GetProperty("items")[0].Decimal("limitPrice").ShouldBe(31m);

        var open = await (await client.GetAsync($"/api/v1/users/{userId}/orders?status=OPEN&pageSize=1")).DataAsync(HttpStatusCode.OK);
        open.Int("totalCount").ShouldBe(2);
        open.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Fifty_simultaneous_orders_never_spend_more_than_the_account_has()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("MEDI", 100.00m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => client.PlaceOrderAsync(userId, "MEDI", "BUY", "MARKET", 100)));

        var unexpected = responses.FirstOrDefault(r => r.StatusCode is not
            (HttpStatusCode.Created or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict));
        unexpected.ShouldBeNull(unexpected is null ? null : await unexpected.Content.ReadAsStringAsync());
        var filled = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        filled.ShouldBe(9);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(41);

        await using var db = api.CreateDbContext();
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        account.Cash.ShouldBe(100_000m - filled * 10_001m);
        account.Cash.ShouldBeGreaterThanOrEqualTo(0m);
        (await db.Positions.SingleAsync(p => p.AccountId == accountId)).Quantity.ShouldBe(filled * 100);
    }
}
