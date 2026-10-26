using System.Net;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TradeSim.Application.Events;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class PortfolioHistoryTests(ApiFactory api)
{
    private async Task<(HttpClient Client, Guid UserId, Guid AccountId)> TradeAFewTimesAsync()
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("SOLR", 18.00m);
        await api.Prices.SetPriceAsync("FINX", 95.00m);

        await (await client.PlaceOrderAsync(userId, "SOLR", "BUY", "MARKET", 500)).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "FINX", "BUY", "MARKET", 20)).DataAsync(HttpStatusCode.Created);
        var limit = await (await client.PlaceOrderAsync(userId, "FINX", "BUY", "LIMIT", 10, limitPrice: 90m)).DataAsync(HttpStatusCode.Created);
        await (await client.PutAsync($"/api/v1/orders/{limit.Text("orderId")}/cancel", null)).DataAsync(HttpStatusCode.OK);
        await (await client.PlaceOrderAsync(userId, "SOLR", "SELL", "STOP_LOSS", 200, triggerPrice: 16m)).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "FINX", "BUY", "LIMIT", 5, limitPrice: 92m, timeInForce: "GTC")).DataAsync(HttpStatusCode.Created);

        await api.Prices.SetPriceAsync("SOLR", 19.00m);
        await (await client.PlaceOrderAsync(userId, "SOLR", "SELL", "MARKET", 100)).DataAsync(HttpStatusCode.Created);

        return (client, userId, accountId);
    }

    [Fact]
    public async Task Replaying_the_event_history_rebuilds_the_stored_balances()
    {
        var (_, _, accountId) = await TradeAFewTimesAsync();

        await using var db = api.CreateDbContext();
        var history = await db.AccountEvents.Where(e => e.AccountId == accountId).ToListAsync();
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        var positions = await db.Positions.Where(p => p.AccountId == accountId)
            .Join(db.Instruments, p => p.InstrumentId, i => i.Id, (p, i) => new { i.Symbol, p.Quantity, p.ReservedQuantity })
            .ToListAsync();

        var replayed = AccountReplay.Rebuild(history);

        history.Select(e => e.Version).ShouldBe(Enumerable.Range(1, history.Count).Select(v => (long)v));
        replayed.Cash.ShouldBe(account.Cash);
        replayed.ReservedCash.ShouldBe(account.ReservedCash);
        foreach (var position in positions)
        {
            replayed.Quantities[position.Symbol].ShouldBe(position.Quantity);
            replayed.ReservedQuantities.GetValueOrDefault(position.Symbol).ShouldBe(position.ReservedQuantity);
        }
    }

    [Fact]
    public async Task Transactions_list_the_audit_trail_newest_first()
    {
        var (client, userId, _) = await TradeAFewTimesAsync();

        var page = await (await client.GetAsync($"/api/v1/portfolios/{userId}/transactions?pageSize=100")).DataAsync(HttpStatusCode.OK);
        var types = page.GetProperty("items").EnumerateArray().Select(e => e.Text("type")).ToList();
        types[^1].ShouldBe("ACCOUNT_OPENED");
        types.ShouldContain("ORDER_PLACED");
        types.ShouldContain("ORDER_FILLED");
        types.ShouldContain("ORDER_CANCELLED");

        var fills = await (await client.GetAsync($"/api/v1/portfolios/{userId}/transactions?type=order_filled")).DataAsync(HttpStatusCode.OK);
        fills.Int("totalCount").ShouldBe(3);
        var latestFill = fills.GetProperty("items")[0];
        latestFill.GetProperty("details").Text("symbol").ShouldBe("SOLR");
        latestFill.GetProperty("details").Text("side").ShouldBe("SELL");
        latestFill.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();

        (await (await client.GetAsync($"/api/v1/portfolios/{userId}/transactions?type=DEPOSIT")).ErrorCodeAsync(HttpStatusCode.BadRequest))
            .ShouldBe("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Performance_adds_up_the_trades()
    {
        var (client, userId, _) = await TradeAFewTimesAsync();

        var performance = await (await client.GetAsync($"/api/v1/portfolios/{userId}/performance")).DataAsync(HttpStatusCode.OK);
        performance.Decimal("initialCash").ShouldBe(100_000m);
        performance.Int("tradeCount").ShouldBe(3);
        performance.Decimal("commissionPaid").ShouldBe(4.50m);
        performance.Decimal("realizedPnl").ShouldBe(100m);
        performance.Decimal("totalReturn").ShouldBe(performance.Decimal("equity") - 100_000m);
        performance.GetProperty("equityCurve").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
    }
}
