using Shouldly;
using TradeSim.Domain.Orders;

namespace TradeSim.UnitTests.Orders;

public class CommissionTests
{
    [Theory]
    [InlineData(1, 1.00)]
    [InlineData(100, 1.00)]
    [InlineData(200, 1.00)]
    [InlineData(333, 1.67)]
    [InlineData(1000, 5.00)]
    public void Is_half_a_cent_per_share_with_a_one_dollar_minimum(int quantity, decimal expected)
    {
        Commission.For(quantity).ShouldBe(expected);
    }
}
