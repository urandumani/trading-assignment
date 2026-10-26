using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace TradeSim.IntegrationTests.Api;

public sealed class RateLimitedApiFactory : ApiFactory
{
    public RateLimitedApiFactory()
        : base(useRabbitMq: false)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:LoginPerMinute", "5");
        builder.UseSetting("RateLimiting:RequestsPerSecond", "50");
    }
}

public class RateLimitTests(RateLimitedApiFactory api) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Bursts_and_repeated_logins_are_throttled_with_429_and_retry_after()
    {
        var (client, _, _) = await api.LoginAsync("trader1");

        var responses = await Task.WhenAll(Enumerable.Range(0, 70).Select(_ => client.GetAsync("/api/v1/market/instruments")));
        var limited = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        limited.Count.ShouldBeGreaterThanOrEqualTo(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(50);
        limited[0].Headers.RetryAfter.ShouldNotBeNull();
        (await limited[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("code").GetString()
            .ShouldBe("RATE_LIMITED");

        var anonymous = api.CreateClient();
        for (var i = 2; i <= 5; i++)
        {
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "trader1", password = ApiFactory.TraderPassword }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var sixth = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "trader1", password = ApiFactory.TraderPassword });
        sixth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        sixth.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeGreaterThan(0);

        (await anonymous.GetAsync("/health")).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }
}

public sealed class RedisDownApiFactory : ApiFactory
{
    public RedisDownApiFactory()
        : base(useRabbitMq: false)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Redis", "localhost:1,abortConnect=false,connectTimeout=200");
    }
}

public class RedisDownTests(RedisDownApiFactory api) : IClassFixture<RedisDownApiFactory>
{
    [Fact]
    public async Task Prices_fall_back_to_the_saved_ones_and_are_marked_stale()
    {
        var (client, _, _) = await api.NewUserAsync();

        var snapshot = await (await client.GetAsync("/api/v1/market/snapshot")).DataAsync(HttpStatusCode.OK);

        var instruments = snapshot.GetProperty("instruments").EnumerateArray().ToList();
        instruments.Count.ShouldBe(12);
        instruments.ShouldAllBe(i => i.GetProperty("stale").GetBoolean() && i.GetProperty("price").GetDecimal() > 0);
        instruments.ShouldAllBe(i => i.GetProperty("updatedAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Market_orders_pause_while_limit_orders_and_portfolios_keep_working()
    {
        var (client, userId, _) = await api.NewUserAsync();

        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 1)).ErrorCodeAsync(HttpStatusCode.ServiceUnavailable))
            .ShouldBe("MARKET_DATA_UNAVAILABLE");
        (await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "LIMIT", 1, limitPrice: 100m)).DataAsync(HttpStatusCode.Created))
            .Text("status").ShouldBe("OPEN");
        (await (await client.GetAsync($"/api/v1/portfolios/{userId}")).DataAsync(HttpStatusCode.OK)).Decimal("reservedCash").ShouldBe(101m);
    }

    [Fact]
    public async Task Health_reports_degraded_but_still_200()
    {
        var response = await api.CreateClient().GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = await response.Content.ReadFromJsonAsync<JsonElement>();
        health.GetProperty("status").GetString().ShouldBe("Degraded");
        health.GetProperty("checks").GetProperty("redis").GetString().ShouldBe("Degraded");
        health.GetProperty("checks").GetProperty("postgres").GetString().ShouldBe("Healthy");
    }
}
