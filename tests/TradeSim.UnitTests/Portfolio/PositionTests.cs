using Shouldly;
using TradeSim.Domain;
using TradeSim.Domain.Portfolio;

namespace TradeSim.UnitTests.Portfolio;

public class PositionTests
{
    private static Position NewPosition() => Position.Open(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Buying_twice_gives_a_weighted_average_cost()
    {
        var position = NewPosition();

        position.ApplyBuy(100, 10m);
        position.ApplyBuy(300, 14m);

        position.Quantity.ShouldBe(400);
        position.AvgCost.ShouldBe(13m);
    }

    [Fact]
    public void Selling_realizes_profit_against_average_cost()
    {
        var position = NewPosition();
        position.ApplyBuy(100, 10m);

        var realized = position.ApplySell(40, 12.5m);

        realized.ShouldBe(100m);
        position.RealizedPnl.ShouldBe(100m);
        position.Quantity.ShouldBe(60);
        position.AvgCost.ShouldBe(10m);
    }

    [Fact]
    public void Selling_at_a_loss_realizes_a_negative_amount()
    {
        var position = NewPosition();
        position.ApplyBuy(10, 50m);

        position.ApplySell(10, 45m).ShouldBe(-50m);
    }

    [Fact]
    public void Selling_everything_resets_average_cost()
    {
        var position = NewPosition();
        position.ApplyBuy(10, 50m);

        position.ApplySell(10, 55m);

        position.Quantity.ShouldBe(0);
        position.AvgCost.ShouldBe(0m);
    }

    [Fact]
    public void Cannot_sell_more_than_is_held()
    {
        var position = NewPosition();
        position.ApplyBuy(10, 50m);

        Should.Throw<DomainException>(() => position.ApplySell(11, 50m)).Code.ShouldBe("INSUFFICIENT_POSITION");
    }

    [Fact]
    public void Reserved_shares_cannot_be_sold_or_reserved_again()
    {
        var position = NewPosition();
        position.ApplyBuy(10, 50m);
        position.ReserveShares(8);

        position.AvailableQuantity.ShouldBe(2);
        Should.Throw<DomainException>(() => position.ApplySell(3, 50m)).Code.ShouldBe("INSUFFICIENT_POSITION");
        Should.Throw<DomainException>(() => position.ReserveShares(3)).Code.ShouldBe("INSUFFICIENT_POSITION");
    }

    [Fact]
    public void Released_shares_become_available_again()
    {
        var position = NewPosition();
        position.ApplyBuy(10, 50m);
        position.ReserveShares(10);

        position.ReleaseShares(10);

        position.AvailableQuantity.ShouldBe(10);
    }
}
