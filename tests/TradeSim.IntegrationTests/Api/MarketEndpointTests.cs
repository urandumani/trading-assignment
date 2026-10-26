using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TradeSim.Application.MarketData;
using TradeSim.Domain.Orders;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class MarketEndpointTests(ApiFactory api)
{
    private async Task<HttpClient> ClientAsync() => (await api.LoginAsync("trader3")).Client;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    [Fact]
    public async Task Snapshot_shows_live_prices_from_redis_and_falls_back_to_the_saved_price()
    {
        var now = DateTimeOffset.UtcNow;
        await api.Prices.PublishAsync(new PriceTick("NOVA", 90.00m, DayOpen: 80.00m, DayHigh: 91.00m, DayLow: 79.50m, false, null, now));
        await api.Prices.PublishAsync(new PriceTick("HELX", 40.00m, 42.00m, 42.50m, 39.90m, Halted: true, HaltedUntil: now.AddSeconds(30), now));
        await api.RemovePriceAsync("LUXE");

        var data = await DataAsync(await (await ClientAsync()).GetAsync("/api/v1/market/snapshot"));

        data.GetProperty("marketOpen").ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
        var instruments = data.GetProperty("instruments").EnumerateArray().ToDictionary(i => i.GetProperty("symbol").GetString()!);
        instruments.Count.ShouldBe(12);

        var nova = instruments["NOVA"];
        nova.GetProperty("price").GetDecimal().ShouldBe(90.00m);
        nova.GetProperty("change").GetDecimal().ShouldBe(10.00m);
        nova.GetProperty("changePercent").GetDecimal().ShouldBe(12.50m);
        nova.GetProperty("dayHigh").GetDecimal().ShouldBe(91.00m);
        nova.GetProperty("halted").GetBoolean().ShouldBeFalse();
        nova.GetProperty("stale").GetBoolean().ShouldBeFalse();

        instruments["HELX"].GetProperty("halted").GetBoolean().ShouldBeTrue();

        var luxe = instruments["LUXE"];
        luxe.GetProperty("price").GetDecimal().ShouldBeGreaterThan(0m);
        luxe.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        luxe.GetProperty("stale").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Instruments_list_volatility_and_minimum_size()
    {
        var data = await DataAsync(await (await ClientAsync()).GetAsync("/api/v1/market/instruments"));

        var instruments = data.EnumerateArray().ToDictionary(i => i.GetProperty("symbol").GetString()!);
        instruments.Count.ShouldBe(12);
        instruments["GRDX"].GetProperty("volatility").GetString().ShouldBe("LOW");
        instruments["APXL"].GetProperty("volatility").GetString().ShouldBe("MEDIUM");
        instruments["CRYP"].GetProperty("volatility").GetString().ShouldBe("EXTREME");
        instruments["CRYP"].GetProperty("minQuantity").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task Price_history_defaults_to_one_session_of_minute_candles()
    {
        var data = await DataAsync(await (await ClientAsync()).GetAsync("/api/v1/market/instruments/apxl/price-history"));

        data.GetProperty("symbol").GetString().ShouldBe("APXL");
        data.GetProperty("interval").GetString().ShouldBe("1m");
        var times = data.GetProperty("candles").EnumerateArray().Select(c => c.GetProperty("time").GetDateTimeOffset()).ToList();
        times.Count.ShouldBe(390);
        times.ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public async Task Hourly_candles_combine_the_minute_candles_inside_each_hour()
    {
        var client = await ClientAsync();

        var hourly = (await DataAsync(await client.GetAsync("/api/v1/market/instruments/APXL/price-history?interval=1h")))
            .GetProperty("candles").EnumerateArray().ToList();
        var minutes = (await DataAsync(await client.GetAsync("/api/v1/market/instruments/APXL/price-history?interval=1m")))
            .GetProperty("candles").EnumerateArray().ToList();

        hourly.Count.ShouldBe(7);

        var secondHour = hourly[1];
        var hourStart = secondHour.GetProperty("time").GetDateTimeOffset();
        var inside = minutes.Where(m => m.GetProperty("time").GetDateTimeOffset() >= hourStart
                                        && m.GetProperty("time").GetDateTimeOffset() < hourStart.AddHours(1)).ToList();
        inside.Count.ShouldBe(60);
        secondHour.GetProperty("open").GetDecimal().ShouldBe(inside[0].GetProperty("open").GetDecimal());
        secondHour.GetProperty("close").GetDecimal().ShouldBe(inside[^1].GetProperty("close").GetDecimal());
        secondHour.GetProperty("high").GetDecimal().ShouldBe(inside.Max(m => m.GetProperty("high").GetDecimal()));
        secondHour.GetProperty("low").GetDecimal().ShouldBe(inside.Min(m => m.GetProperty("low").GetDecimal()));

        var daily = (await DataAsync(await client.GetAsync("/api/v1/market/instruments/APXL/price-history?interval=1d")))
            .GetProperty("candles").EnumerateArray().ToList();
        daily.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("/api/v1/market/instruments/APXL/price-history?interval=2m", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("/api/v1/market/instruments/APXL/price-history?limit=0", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("/api/v1/market/instruments/APXL/price-history?from=2026-10-20T00:00:00Z&to=2026-10-19T00:00:00Z", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("/api/v1/market/instruments/AAPL/price-history", HttpStatusCode.NotFound, "INSTRUMENT_NOT_FOUND")]
    [InlineData("/api/v1/market/instruments/AAPL/orderbook", HttpStatusCode.NotFound, "INSTRUMENT_NOT_FOUND")]
    [InlineData("/api/v1/market/instruments/APXL/orderbook?depth=51", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    public async Task Invalid_market_requests_are_rejected(string url, HttpStatusCode status, string code)
    {
        var response = await (await ClientAsync()).GetAsync(url);

        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Order_book_includes_users_open_limit_orders()
    {
        await api.Prices.PublishAsync(new PriceTick("FINX", 95.00m, 95m, 95m, 95m, false, null, DateTimeOffset.UtcNow));
        await using (var db = api.CreateDbContext())
        {
            var finx = await db.Instruments.SingleAsync(i => i.Symbol == "FINX");
            var account = await db.Accounts.FirstAsync();
            db.Orders.Add(Order.Create(account.Id, finx.Id, OrderSide.Buy, OrderType.Limit, TimeInForce.Gtc, 777, 94.97m, null, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var data = await DataAsync(await (await ClientAsync()).GetAsync("/api/v1/market/instruments/FINX/orderbook?depth=10"));

        data.GetProperty("price").GetDecimal().ShouldBe(95.00m);
        data.GetProperty("bestBid").GetDecimal().ShouldBe(94.98m);
        data.GetProperty("bestAsk").GetDecimal().ShouldBe(95.02m);
        data.GetProperty("spread").GetDecimal().ShouldBe(0.04m);

        var bids = data.GetProperty("bids").EnumerateArray()
            .Select(b => (Price: b.GetProperty("price").GetDecimal(), Quantity: b.GetProperty("quantity").GetInt32())).ToList();
        var asks = data.GetProperty("asks").EnumerateArray().ToList();
        bids.Count.ShouldBe(10);
        asks.Count.ShouldBe(10);
        bids.Select(b => b.Price).ShouldBeInOrder(SortDirection.Descending);

        bids[1].ShouldBe((94.97m, 777));
    }

    [Fact]
    public async Task Order_book_headlines_and_spread_follow_real_orders_inside_the_simulated_levels()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("HELX", 100m);
        await (await client.PlaceOrderAsync(userId, "HELX", "BUY", "MARKET", 1)).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "HELX", "BUY", "LIMIT", 1, limitPrice: 101m, timeInForce: "GTC")).DataAsync(HttpStatusCode.Created);
        await (await client.PlaceOrderAsync(userId, "HELX", "SELL", "LIMIT", 1, limitPrice: 99m, timeInForce: "GTC")).DataAsync(HttpStatusCode.Created);

        var data = await DataAsync(await client.GetAsync("/api/v1/market/instruments/HELX/orderbook?depth=3"));

        data.GetProperty("bestBid").GetDecimal().ShouldBe(101m);
        data.GetProperty("bestAsk").GetDecimal().ShouldBe(99m);
        data.GetProperty("bids")[0].GetProperty("price").GetDecimal().ShouldBe(data.GetProperty("bestBid").GetDecimal());
        data.GetProperty("asks")[0].GetProperty("price").GetDecimal().ShouldBe(data.GetProperty("bestAsk").GetDecimal());
        data.GetProperty("spread").GetDecimal().ShouldBe(-2m);
    }
}
