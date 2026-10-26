using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class OrderValidationTests(ApiFactory api)
{
    [Fact]
    public async Task Missing_fields_are_reported_per_field()
    {
        var (client, userId, _) = await api.NewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/orders", new { userId, instrumentSymbol = "APXL", orderType = "MARKET", quantity = 0 });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fields = body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        fields.ShouldContain("side");
        fields.ShouldContain("quantity");
    }

    [Fact]
    public async Task An_unknown_enum_value_is_a_validation_error()
    {
        var (client, userId, _) = await api.NewUserAsync();

        var code = await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "ICEBERG", 1)).ErrorCodeAsync(HttpStatusCode.BadRequest);

        code.ShouldBe("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Fields_that_dont_fit_the_order_type_are_rejected()
    {
        var (client, userId, _) = await api.NewUserAsync();

        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "STOP_LOSS", 1, triggerPrice: 100m)).ErrorCodeAsync(HttpStatusCode.BadRequest))
            .ShouldBe("VALIDATION_FAILED");
        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 1)).ErrorCodeAsync(HttpStatusCode.BadRequest))
            .ShouldBe("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("orderType", 99)]
    [InlineData("side", 99)]
    [InlineData("timeInForce", 99)]
    [InlineData("orderType", 0)]
    [InlineData("side", 0)]
    [InlineData("timeInForce", 0)]
    public async Task Numeric_enums_are_rejected_without_creating_an_order(string field, int value)
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 100m);
        var body = new Dictionary<string, object?>
        {
            ["userId"] = userId, ["instrumentSymbol"] = "APXL", ["orderType"] = "MARKET",
            ["side"] = "BUY", ["quantity"] = 1, ["timeInForce"] = "DAY",
        };
        body[field] = value;

        (await (await client.PostAsJsonAsync("/api/v1/orders", body)).ErrorCodeAsync(HttpStatusCode.BadRequest))
            .ShouldBe("VALIDATION_FAILED");

        await using var db = api.CreateDbContext();
        (await db.Orders.CountAsync(o => o.AccountId == accountId)).ShouldBe(0);
    }

    [Fact]
    public async Task Ordering_for_another_user_is_forbidden()
    {
        var (client, _, _) = await api.NewUserAsync();
        var (_, otherUserId, _) = await api.NewUserAsync();

        (await (await client.PlaceOrderAsync(otherUserId, "APXL", "BUY", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.Forbidden))
            .ShouldBe("FORBIDDEN");
    }

    [Fact]
    public async Task Unknown_instruments_are_not_found()
    {
        var (client, userId, _) = await api.NewUserAsync();

        (await (await client.PlaceOrderAsync(userId, "AAPL", "BUY", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.NotFound))
            .ShouldBe("INSTRUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Halted_instruments_cant_be_traded()
    {
        var (client, userId, _) = await api.NewUserAsync();
        var now = DateTimeOffset.UtcNow;
        await api.Prices.PublishAsync(new(Symbol: "HELX", 40m, 40m, 40m, 40m, Halted: true, HaltedUntil: now.AddSeconds(30), now));

        (await (await client.PlaceOrderAsync(userId, "HELX", "BUY", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("INSTRUMENT_HALTED");

        await api.Prices.SetPriceAsync("HELX", 40m);
    }

    [Fact]
    public async Task Orders_below_the_minimum_size_are_rejected()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("CRYP", 12m);

        (await (await client.PlaceOrderAsync(userId, "CRYP", "BUY", "MARKET", 5)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("ORDER_TOO_SMALL");
    }

    [Fact]
    public async Task Risk_limits_cap_order_value_and_position_size()
    {
        var (client, userId, _) = await api.NewUserAsync();

        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 2_000, limitPrice: 150m)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("RISK_LIMIT_EXCEEDED");
        (await (await client.PlaceOrderAsync(userId, "SOLR", "BUY", "LIMIT", 10_001, limitPrice: 1m)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("RISK_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Buying_needs_enough_buying_power()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 150m);

        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 700)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("INSUFFICIENT_BUYING_POWER");
    }

    [Fact]
    public async Task Selling_needs_shares()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("FINX", 95m);

        (await (await client.PlaceOrderAsync(userId, "FINX", "SELL", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.UnprocessableEntity))
            .ShouldBe("INSUFFICIENT_POSITION");
    }

    [Fact]
    public async Task Without_a_current_price_market_orders_pause_but_limit_orders_are_accepted()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("AERO", 28m, at: DateTimeOffset.UtcNow.AddMinutes(-1));

        (await (await client.PlaceOrderAsync(userId, "AERO", "BUY", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.ServiceUnavailable))
            .ShouldBe("MARKET_DATA_UNAVAILABLE");
        (await (await client.PlaceOrderAsync(userId, "AERO", "BUY", "LIMIT", 1, limitPrice: 27m)).DataAsync(HttpStatusCode.Created))
            .Text("status").ShouldBe("OPEN");
    }

    [Fact]
    public async Task Other_users_orders_and_unknown_ids_are_not_found()
    {
        var (owner, ownerId, _) = await api.NewUserAsync();
        var (stranger, _, _) = await api.NewUserAsync();
        var orderId = (await (await owner.PlaceOrderAsync(ownerId, "APXL", "BUY", "LIMIT", 1, limitPrice: 1m)).DataAsync(HttpStatusCode.Created))
            .Text("orderId");

        (await (await stranger.GetAsync($"/api/v1/orders/{orderId}")).ErrorCodeAsync(HttpStatusCode.NotFound)).ShouldBe("ORDER_NOT_FOUND");
        (await (await stranger.PutAsync($"/api/v1/orders/{orderId}/cancel", null)).ErrorCodeAsync(HttpStatusCode.NotFound)).ShouldBe("ORDER_NOT_FOUND");
        (await (await owner.GetAsync($"/api/v1/orders/{Guid.NewGuid()}")).ErrorCodeAsync(HttpStatusCode.NotFound)).ShouldBe("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task A_filled_order_cant_be_cancelled()
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("GRDX", 60m);
        var orderId = (await (await client.PlaceOrderAsync(userId, "GRDX", "BUY", "MARKET", 1)).DataAsync(HttpStatusCode.Created)).Text("orderId");

        (await (await client.PutAsync($"/api/v1/orders/{orderId}/cancel", null)).ErrorCodeAsync(HttpStatusCode.Conflict))
            .ShouldBe("ORDER_NOT_OPEN");
    }

    [Fact]
    public async Task Another_users_portfolio_is_forbidden()
    {
        var (client, _, _) = await api.NewUserAsync();
        var (_, otherUserId, _) = await api.NewUserAsync();

        foreach (var path in new[] { "", "/positions", "/transactions", "/performance" })
        {
            (await (await client.GetAsync($"/api/v1/portfolios/{otherUserId}{path}")).ErrorCodeAsync(HttpStatusCode.Forbidden))
                .ShouldBe("FORBIDDEN");
        }

        (await (await client.GetAsync($"/api/v1/users/{otherUserId}/orders")).ErrorCodeAsync(HttpStatusCode.Forbidden)).ShouldBe("FORBIDDEN");
    }
}
