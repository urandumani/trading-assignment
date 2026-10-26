using System.Net;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace TradeSim.IntegrationTests.Api;

public sealed class ClosedMarketApiFactory : ApiFactory
{
    public ClosedMarketApiFactory()
        : base(useRabbitMq: false)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Market:AlwaysOpen", "false");
        builder.UseSetting("Market:TimeZone", ZoneWhereItIsNight());
    }

    private static string ZoneWhereItIsNight()
    {
        var offset = ((4 - DateTime.UtcNow.Hour) % 24 + 24) % 24;
        if (offset > 12)
        {
            offset -= 24;
        }

        return offset == 0 ? "Etc/UTC" : $"Etc/GMT{(offset > 0 ? "-" : "+")}{Math.Abs(offset)}";
    }
}

public class ClosedMarketTests(ClosedMarketApiFactory api) : IClassFixture<ClosedMarketApiFactory>
{
    [Theory]
    [InlineData("MARKET", null)]
    [InlineData("LIMIT", 100.0)]
    public async Task No_orders_are_accepted_while_the_market_is_closed(string orderType, double? limitPrice)
    {
        var (client, userId, _) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 150m);

        var code = await (await client.PlaceOrderAsync(userId, "APXL", "BUY", orderType, 1, (decimal?)limitPrice))
            .ErrorCodeAsync(HttpStatusCode.UnprocessableEntity);

        code.ShouldBe("MARKET_CLOSED");
    }
}
