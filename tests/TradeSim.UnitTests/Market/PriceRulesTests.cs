using Shouldly;
using TradeSim.Domain.Market;

namespace TradeSim.UnitTests.Market;

public class RandomWalkTests
{
    [Fact]
    public void Normal_moves_stay_within_max_move()
    {
        var random = new Random(42);
        const decimal maxMovePct = 0.03m;

        for (var i = 0; i < 10_000; i++)
        {
            var step = RandomWalk.Next(100m, maxMovePct, jumpChance: 0m, random);

            step.IsJump.ShouldBeFalse();
            Math.Abs(step.Price / 100m - 1).ShouldBeLessThanOrEqualTo(maxMovePct / 100m);
        }
    }

    [Fact]
    public void Price_never_goes_negative_over_a_long_walk()
    {
        var random = new Random(7);
        var price = 1m;

        for (var i = 0; i < 100_000; i++)
        {
            price = RandomWalk.Next(price, maxMovePct: 1m, jumpChance: 0.01m, random).Price;
            price.ShouldBeGreaterThan(0m);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_prices()
    {
        static decimal[] Walk(int seed)
        {
            var random = new Random(seed);
            var price = 150m;
            return Enumerable.Range(0, 100).Select(_ => price = RandomWalk.Next(price, 0.03m, 0m, random).Price).ToArray();
        }

        Walk(123).ShouldBe(Walk(123));
        Walk(123).ShouldNotBe(Walk(124));
    }

    [Fact]
    public void Jumps_move_the_price_between_5_and_15_percent()
    {
        var random = new Random(1);

        for (var i = 0; i < 1_000; i++)
        {
            var step = RandomWalk.Next(100m, 0.03m, jumpChance: 1m, random);

            step.IsJump.ShouldBeTrue();
            Math.Abs(step.Price - 100m).ShouldBeInRange(5m, 15m);
        }
    }

    [Theory]
    [InlineData(12.344, 12.34)]
    [InlineData(12.345, 12.35)]
    [InlineData(0.001, 0.01)]
    public void Prices_are_rounded_to_the_cent(decimal price, decimal expected)
    {
        RandomWalk.ToTick(price).ShouldBe(expected);
    }
}

public class CircuitBreakerTests
{
    [Theory]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(90)]
    public void Moves_up_to_ten_percent_pass_through(decimal proposed)
    {
        CircuitBreaker.Apply(100m, proposed).ShouldBe(new CircuitBreakerResult(proposed, Tripped: false));
    }

    [Fact]
    public void A_bigger_rise_is_capped_and_trips_the_breaker()
    {
        CircuitBreaker.Apply(100m, 112m).ShouldBe(new CircuitBreakerResult(110m, Tripped: true));
    }

    [Fact]
    public void A_bigger_fall_is_capped_and_trips_the_breaker()
    {
        CircuitBreaker.Apply(100m, 85m).ShouldBe(new CircuitBreakerResult(90m, Tripped: true));
    }
}

public class PriceCandleTests
{
    [Fact]
    public void Tracks_open_high_low_and_close()
    {
        var candle = PriceCandle.Start(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 100m);

        candle.Add(103m);
        candle.Add(98m);
        candle.Add(101m);

        (candle.Open, candle.High, candle.Low, candle.Close).ShouldBe((100m, 103m, 98m, 101m));
    }
}
