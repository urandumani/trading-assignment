using Shouldly;
using TradeSim.Domain;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;

namespace TradeSim.UnitTests.Orders;

public class SettlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    private static (Account Account, Position Position) NewAccount(decimal cash = 100_000m, RiskLimits? limits = null)
    {
        var account = Account.Open(Guid.NewGuid(), cash, limits ?? RiskLimits.CashAccount());
        return (account, Position.Open(account.Id, InstrumentId));
    }

    private static Order NewOrder(Account account, OrderSide side, OrderType type, int quantity, decimal? limit = null, decimal? trigger = null) =>
        Order.Create(account.Id, InstrumentId, side, type, TimeInForce.Day, quantity, limit, trigger, null, Now);

    [Fact]
    public void A_market_buy_pays_price_times_quantity_plus_commission()
    {
        var (account, position) = NewAccount();
        var order = NewOrder(account, OrderSide.Buy, OrderType.Market, 100);

        var fill = Settlement.Fill(account, position, order, 150.25m, Now);

        fill.CashChange.ShouldBe(-15_026.00m);
        account.Cash.ShouldBe(84_974.00m);
        position.Quantity.ShouldBe(100);
        position.AvgCost.ShouldBe(150.25m);
        order.Status.ShouldBe(OrderStatus.Filled);
        order.ExecutedPrice.ShouldBe(150.25m);
        order.Commission.ShouldBe(1.00m);
    }

    [Fact]
    public void A_market_sell_receives_proceeds_minus_commission_and_realizes_pnl()
    {
        var (account, position) = NewAccount();
        Settlement.Fill(account, position, NewOrder(account, OrderSide.Buy, OrderType.Market, 100), 100m, Now);

        var fill = Settlement.Fill(account, position, NewOrder(account, OrderSide.Sell, OrderType.Market, 40), 110m, Now);

        fill.CashChange.ShouldBe(4_399.00m);
        fill.RealizedPnl.ShouldBe(400m);
        position.Quantity.ShouldBe(60);
        account.Cash.ShouldBe(100_000m - 10_001m + 4_399m);
    }

    [Fact]
    public void A_limit_buy_reserves_cash_and_the_fill_releases_it()
    {
        var (account, position) = NewAccount();
        var order = NewOrder(account, OrderSide.Buy, OrderType.Limit, 1000, limit: 25m);

        Settlement.Reserve(account, position, order).Cash.ShouldBe(25_005m);
        account.ReservedCash.ShouldBe(25_005m);

        var fill = Settlement.Fill(account, position, order, 24.50m, Now);

        fill.ReleasedCash.ShouldBe(25_005m);
        account.ReservedCash.ShouldBe(0m);
        account.Cash.ShouldBe(100_000m - 24_505m);
    }

    [Fact]
    public void A_resting_sell_reserves_shares_and_the_fill_releases_them()
    {
        var (account, position) = NewAccount();
        Settlement.Fill(account, position, NewOrder(account, OrderSide.Buy, OrderType.Market, 50), 10m, Now);
        var stopLoss = NewOrder(account, OrderSide.Sell, OrderType.StopLoss, 50, trigger: 9m);

        Settlement.Reserve(account, position, stopLoss).Shares.ShouldBe(50);
        position.AvailableQuantity.ShouldBe(0);

        var fill = Settlement.Fill(account, position, stopLoss, 8.95m, Now);

        fill.ReleasedShares.ShouldBe(50);
        position.Quantity.ShouldBe(0);
        position.ReservedQuantity.ShouldBe(0);
    }

    [Fact]
    public void Cancelling_releases_what_was_reserved()
    {
        var (account, position) = NewAccount();
        var order = NewOrder(account, OrderSide.Buy, OrderType.Limit, 10, limit: 100m);
        Settlement.Reserve(account, position, order);

        Settlement.ReleaseReservation(account, position, order).Cash.ShouldBe(1_001m);

        account.ReservedCash.ShouldBe(0m);
    }

    [Fact]
    public void A_resting_sell_needs_shares_to_reserve()
    {
        var (account, position) = NewAccount();

        Should.Throw<DomainException>(() => Settlement.Reserve(account, position, NewOrder(account, OrderSide.Sell, OrderType.Limit, 1, limit: 5m)))
            .Code.ShouldBe(ErrorCodes.InsufficientPosition);
    }

    [Fact]
    public void A_margin_account_can_spend_more_than_its_cash()
    {
        var (account, position) = NewAccount(limits: RiskLimits.MarginAccount());

        Settlement.Fill(account, position, NewOrder(account, OrderSide.Buy, OrderType.Market, 1000), 150m, Now);

        account.Cash.ShouldBe(-50_005m);
        account.Equity(holdingsValue: 150_000m).ShouldBe(99_995m);
    }
}
