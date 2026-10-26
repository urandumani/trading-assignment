using Shouldly;
using TradeSim.Domain;
using TradeSim.Domain.Orders;

namespace TradeSim.UnitTests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(
        OrderType type,
        OrderSide side = OrderSide.Buy,
        int quantity = 10,
        decimal? limitPrice = null,
        decimal? triggerPrice = null,
        TimeInForce timeInForce = TimeInForce.Day) =>
        Order.Create(Guid.NewGuid(), Guid.NewGuid(), side, type, timeInForce, quantity, limitPrice, triggerPrice, null, Now);

    [Fact]
    public void A_new_order_is_open()
    {
        var order = NewOrder(OrderType.Market);

        order.Status.ShouldBe(OrderStatus.Open);
        order.CreatedAt.ShouldBe(Now);
    }

    public static TheoryData<OrderType, OrderSide, int, decimal?, decimal?> InvalidOrders => new()
    {
        { OrderType.Market, OrderSide.Buy, 0, null, null },
        { OrderType.Market, OrderSide.Buy, -5, null, null },
        { OrderType.Market, OrderSide.Buy, 10, 100m, null },
        { OrderType.Limit, OrderSide.Buy, 10, null, null },
        { OrderType.Limit, OrderSide.Buy, 10, 0m, null },
        { OrderType.Limit, OrderSide.Buy, 10, 100m, 90m },
        { OrderType.StopLoss, OrderSide.Sell, 10, null, null },
        { OrderType.StopLoss, OrderSide.Buy, 10, null, 90m },
        { OrderType.TakeProfit, OrderSide.Buy, 10, null, 110m },
        { OrderType.TakeProfit, OrderSide.Sell, 10, 100m, 110m },
        { OrderType.Limit, OrderSide.Buy, 10, 100.005m, null },
        { OrderType.StopLoss, OrderSide.Sell, 10, null, 89.999m },
    };

    [Theory]
    [MemberData(nameof(InvalidOrders))]
    public void Rejects_fields_that_dont_fit_the_order_type(OrderType type, OrderSide side, int quantity, decimal? limitPrice, decimal? triggerPrice)
    {
        Should.Throw<DomainException>(() => NewOrder(type, side, quantity, limitPrice, triggerPrice))
            .Code.ShouldBe("VALIDATION_FAILED");
    }

    [Fact]
    public void A_limit_buy_reserves_its_full_cost_plus_commission()
    {
        var order = NewOrder(OrderType.Limit, OrderSide.Buy, quantity: 1000, limitPrice: 25m);

        order.ReservedAmount.ShouldBe(25_005m);
    }

    [Theory]
    [InlineData((OrderType)99, OrderSide.Buy, TimeInForce.Day)]
    [InlineData(OrderType.Market, (OrderSide)99, TimeInForce.Day)]
    [InlineData(OrderType.Market, OrderSide.Buy, (TimeInForce)99)]
    public void Undefined_enums_are_rejected_at_the_domain_boundary(OrderType type, OrderSide side, TimeInForce timeInForce)
    {
        Should.Throw<DomainException>(() => NewOrder(type, side, timeInForce: timeInForce))
            .Code.ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void A_limit_sell_reserves_no_cash()
    {
        NewOrder(OrderType.Limit, OrderSide.Sell, limitPrice: 25m).ReservedAmount.ShouldBe(0m);
    }

    [Theory]
    [InlineData(OrderType.Limit, OrderSide.Buy, 100.00, 99.99, true)]
    [InlineData(OrderType.Limit, OrderSide.Buy, 100.00, 100.00, true)]
    [InlineData(OrderType.Limit, OrderSide.Buy, 100.00, 100.01, false)]
    [InlineData(OrderType.Limit, OrderSide.Sell, 100.00, 100.01, true)]
    [InlineData(OrderType.Limit, OrderSide.Sell, 100.00, 99.99, false)]
    [InlineData(OrderType.StopLoss, OrderSide.Sell, 90.00, 89.99, true)]
    [InlineData(OrderType.StopLoss, OrderSide.Sell, 90.00, 90.01, false)]
    [InlineData(OrderType.TakeProfit, OrderSide.Sell, 110.00, 110.00, true)]
    [InlineData(OrderType.TakeProfit, OrderSide.Sell, 110.00, 109.99, false)]
    public void Triggers_when_the_price_crosses_its_level(OrderType type, OrderSide side, decimal level, decimal price, bool expected)
    {
        var order = type == OrderType.Limit
            ? NewOrder(type, side, limitPrice: level)
            : NewOrder(type, side, triggerPrice: level);

        order.IsTriggeredBy(price).ShouldBe(expected);
    }

    [Fact]
    public void A_market_order_triggers_at_any_price()
    {
        NewOrder(OrderType.Market).IsTriggeredBy(123.45m).ShouldBeTrue();
    }

    [Fact]
    public void Filling_records_the_execution()
    {
        var order = NewOrder(OrderType.Market, quantity: 100);
        var filledAt = Now.AddSeconds(1);

        order.Fill(150.25m, 1.00m, filledAt);

        order.Status.ShouldBe(OrderStatus.Filled);
        order.ExecutedPrice.ShouldBe(150.25m);
        order.Commission.ShouldBe(1.00m);
        order.ExecutedAt.ShouldBe(filledAt);
        order.UpdatedAt.ShouldBe(filledAt);
    }

    [Fact]
    public void Only_open_orders_can_change_status()
    {
        var order = NewOrder(OrderType.Limit, limitPrice: 10m);
        order.Cancel(Now);

        Should.Throw<DomainException>(() => order.Fill(10m, 1m, Now)).Code.ShouldBe("ORDER_NOT_OPEN");
        Should.Throw<DomainException>(() => order.Cancel(Now)).Code.ShouldBe("ORDER_NOT_OPEN");
        Should.Throw<DomainException>(() => order.Reject("x", Now)).Code.ShouldBe("ORDER_NOT_OPEN");
    }

    [Fact]
    public void Day_orders_expire()
    {
        var order = NewOrder(OrderType.Limit, limitPrice: 10m, timeInForce: TimeInForce.Day);

        order.Expire(Now);

        order.Status.ShouldBe(OrderStatus.Expired);
    }

    [Fact]
    public void Gtc_orders_do_not_expire()
    {
        var order = NewOrder(OrderType.Limit, limitPrice: 10m, timeInForce: TimeInForce.Gtc);

        Should.Throw<InvalidOperationException>(() => order.Expire(Now));
    }

    [Fact]
    public void Rejecting_keeps_the_reason()
    {
        var order = NewOrder(OrderType.Limit, limitPrice: 10m);

        order.Reject("INSUFFICIENT_BUYING_POWER", Now);

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectReason.ShouldBe("INSUFFICIENT_BUYING_POWER");
    }
}
