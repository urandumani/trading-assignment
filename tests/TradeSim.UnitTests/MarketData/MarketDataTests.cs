using Shouldly;
using TradeSim.Application.MarketData;
using TradeSim.Domain;

namespace TradeSim.UnitTests.MarketData;

public class OrderBookBuilderTests
{
    [Fact]
    public void Builds_the_requested_depth_with_bids_below_asks()
    {
        var book = OrderBookBuilder.Build(price: 150m, depth: 10, resting: [], seed: 1);

        book.Bids.Count.ShouldBe(10);
        book.Asks.Count.ShouldBe(10);
        book.BestBid.ShouldBeLessThan(150m);
        book.BestAsk.ShouldBeGreaterThan(150m);
        book.Bids.Select(b => b.Price).ShouldBeInOrder(SortDirection.Descending);
        book.Asks.Select(a => a.Price).ShouldBeInOrder(SortDirection.Ascending);
        book.Bids[0].Price.ShouldBe(book.BestBid);
        book.Asks[0].Price.ShouldBe(book.BestAsk);
        book.Bids.Concat(book.Asks).ShouldAllBe(level => level.Quantity > 0 && level.Quantity % 10 == 0);
    }

    [Fact]
    public void Real_orders_add_to_their_price_level()
    {
        var empty = OrderBookBuilder.Build(150m, 10, [], seed: 7);
        var bestBid = empty.Bids[0];

        var book = OrderBookBuilder.Build(150m, 10, [new RestingOrders(IsBuy: true, bestBid.Price, 25)], seed: 7);

        book.Bids[0].ShouldBe(bestBid with { Quantity = bestBid.Quantity + 25 });
    }

    [Fact]
    public void A_real_order_between_simulated_levels_gets_its_own_level()
    {
        var book = OrderBookBuilder.Build(100m, 3, [new RestingOrders(IsBuy: false, 100.015m, 40)], seed: 3);

        book.Asks[0].ShouldBe(new OrderBookLevel(100.015m, 40));
        book.BestAsk.ShouldBe(100.015m);
        book.Asks.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(99.99, 100.01)]
    [InlineData(101.00, 99.00)]
    public void Best_prices_follow_the_final_merged_levels(decimal buyPrice, decimal sellPrice)
    {
        var book = OrderBookBuilder.Build(100m, 3,
            [new RestingOrders(true, buyPrice, 25), new RestingOrders(false, sellPrice, 40)], seed: 1);

        book.BestBid.ShouldBe(buyPrice);
        book.BestAsk.ShouldBe(sellPrice);
        book.Bids[0].Price.ShouldBe(book.BestBid);
        book.Asks[0].Price.ShouldBe(book.BestAsk);
        book.Bids.Count.ShouldBe(3);
        book.Asks.Count.ShouldBe(3);
    }

    [Fact]
    public void The_same_seed_gives_the_same_book()
    {
        OrderBookBuilder.Build(42m, 5, [], seed: 99).Bids
            .ShouldBe(OrderBookBuilder.Build(42m, 5, [], seed: 99).Bids);
    }
}

public class CandleIntervalTests
{
    [Theory]
    [InlineData("1m", 1)]
    [InlineData("5M", 5)]
    [InlineData("15m", 15)]
    [InlineData("1h", 60)]
    [InlineData("1d", 1440)]
    public void Parses_supported_intervals(string interval, int minutes)
    {
        CandleInterval.Parse(interval).ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("2m")]
    [InlineData("")]
    [InlineData("1w")]
    public void Rejects_other_intervals(string interval)
    {
        Should.Throw<DomainException>(() => CandleInterval.Parse(interval)).Code.ShouldBe(ErrorCodes.ValidationFailed);
    }
}

public class VolatilityProfileTests
{
    [Theory]
    [InlineData(0.010, "LOW")]
    [InlineData(0.020, "LOW")]
    [InlineData(0.030, "MEDIUM")]
    [InlineData(0.055, "HIGH")]
    [InlineData(0.090, "VERY_HIGH")]
    [InlineData(0.120, "EXTREME")]
    public void Labels_follow_the_max_move(decimal maxMovePct, string expected)
    {
        new InstrumentDetails(Guid.NewGuid(), "X", "X", maxMovePct, 1, 1m).Volatility.ShouldBe(expected);
    }
}
