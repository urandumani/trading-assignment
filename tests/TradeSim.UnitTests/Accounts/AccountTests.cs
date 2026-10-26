using Shouldly;
using TradeSim.Domain.Accounts;

namespace TradeSim.UnitTests.Accounts;

public class AccountTests
{
    [Fact]
    public void Cash_account_buying_power_is_cash_minus_reserved()
    {
        var account = Account.Open(Guid.NewGuid(), 100_000m, RiskLimits.CashAccount());
        account.ReserveCash(10_000m);

        account.BuyingPower(holdingsValue: 0m).ShouldBe(90_000m);
    }

    [Fact]
    public void Cash_account_buying_power_ignores_the_value_of_holdings()
    {
        var account = Account.Open(Guid.NewGuid(), 100_000m, RiskLimits.CashAccount());
        account.Debit(40_000m);

        account.BuyingPower(holdingsValue: 40_000m).ShouldBe(60_000m);
    }

    [Fact]
    public void Margin_account_has_twice_its_equity_as_buying_power()
    {
        var account = Account.Open(Guid.NewGuid(), 100_000m, RiskLimits.MarginAccount());

        account.BuyingPower(holdingsValue: 0m).ShouldBe(200_000m);
    }

    [Fact]
    public void Margin_account_can_borrow_and_buying_power_shrinks_accordingly()
    {
        var account = Account.Open(Guid.NewGuid(), 100_000m, RiskLimits.MarginAccount());
        account.Debit(150_000m);

        account.Cash.ShouldBe(-50_000m);
        account.Equity(holdingsValue: 150_000m).ShouldBe(100_000m);
        account.BuyingPower(holdingsValue: 150_000m).ShouldBe(50_000m);
    }

    [Fact]
    public void Reserving_and_releasing_cash_round_trips()
    {
        var account = Account.Open(Guid.NewGuid(), 1_000m, RiskLimits.CashAccount());

        account.ReserveCash(300m);
        account.ReleaseCash(300m);

        account.ReservedCash.ShouldBe(0m);
        account.Cash.ShouldBe(1_000m);
    }

    [Fact]
    public void Cannot_release_more_cash_than_is_reserved()
    {
        var account = Account.Open(Guid.NewGuid(), 1_000m, RiskLimits.CashAccount());
        account.ReserveCash(100m);

        Should.Throw<ArgumentOutOfRangeException>(() => account.ReleaseCash(101m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public void Margin_rate_must_be_between_zero_and_one(decimal marginRate)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RiskLimits.Create(marginRate, 1, 1m, 1));
    }
}
