using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using TradeSim.Application.MarketData;
using TradeSim.Infrastructure.MarketData;

namespace TradeSim.IntegrationTests.Api;

public static class ApiTestExtensions
{
    public static Task<HttpResponseMessage> PlaceOrderAsync(
        this HttpClient client,
        Guid userId,
        string symbol,
        string side,
        string orderType,
        int quantity,
        decimal? limitPrice = null,
        decimal? triggerPrice = null,
        string timeInForce = "DAY",
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                userId,
                instrumentSymbol = symbol,
                orderType,
                side,
                quantity,
                limitPrice,
                triggerPrice,
                timeInForce,
            }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonElement> DataAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        response.StatusCode.ShouldBe(expected, body.ToString());
        return body.GetProperty("data");
    }

    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        response.StatusCode.ShouldBe(expected, body.ToString());
        return body.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    public static Task SetPriceAsync(this RedisPricePublisher prices, string symbol, decimal price, DateTimeOffset? at = null) =>
        prices.PublishAsync(new PriceTick(symbol, price, price, price, price, false, null, at ?? DateTimeOffset.UtcNow));

    public static decimal Decimal(this JsonElement element, string property) => element.GetProperty(property).GetDecimal();

    public static int Int(this JsonElement element, string property) => element.GetProperty(property).GetInt32();

    public static string Text(this JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
