using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TradeSim.Application.Events;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.IntegrationTests.Api;

public enum RetryFailurePoint { AuditVersionRead, BeforeCommit, AfterCommit }

public sealed class RetryApiFactory : ApiFactory
{
    public RetryApiFactory() : base(useRabbitMq: false) { }

    public RetryFault Fault { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddDbContext<TradeSimDbContext>(options =>
            options.AddInterceptors(new AuditReadFailure(Fault), new CommitFailure(Fault))));
    }

    public sealed class RetryFault
    {
        private RetryFailurePoint? _point;
        public int Failures { get; private set; }

        public void Arm(RetryFailurePoint point) { _point = point; Failures = 0; }

        public void ThrowOnce(RetryFailurePoint point)
        {
            if (_point != point) return;
            _point = null;
            Failures++;
            throw new TimeoutException($"Injected transient failure at {point}.");
        }
    }

    private sealed class AuditReadFailure(RetryFault fault) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("max(", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("account_events"))
                fault.ThrowOnce(RetryFailurePoint.AuditVersionRead);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitFailure(RetryFault fault) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            fault.ThrowOnce(RetryFailurePoint.BeforeCommit);
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            fault.ThrowOnce(RetryFailurePoint.AfterCommit);
            return Task.CompletedTask;
        }
    }
}

public class OrderRetryTests(RetryApiFactory api) : IClassFixture<RetryApiFactory>
{
    [Theory]
    [InlineData(RetryFailurePoint.AuditVersionRead)]
    [InlineData(RetryFailurePoint.BeforeCommit)]
    [InlineData(RetryFailurePoint.AfterCommit)]
    public async Task Retrying_an_unkeyed_order_never_creates_an_extra_trade(RetryFailurePoint point)
    {
        var (client, userId, accountId) = await api.NewUserAsync();
        await api.Prices.SetPriceAsync("APXL", 100m);
        await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 1)).DataAsync(HttpStatusCode.Created);

        api.Fault.Arm(point);
        await (await client.PlaceOrderAsync(userId, "APXL", "BUY", "MARKET", 1)).DataAsync(HttpStatusCode.Created);

        api.Fault.Failures.ShouldBe(1);
        await using var db = api.CreateDbContext();
        (await db.Orders.CountAsync(o => o.AccountId == accountId)).ShouldBe(2);
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        account.Cash.ShouldBe(99_798m);
        account.ReservedCash.ShouldBe(0m);
        (await db.Positions.SingleAsync(p => p.AccountId == accountId)).Quantity.ShouldBe(2);
        var history = await db.AccountEvents.Where(e => e.AccountId == accountId).OrderBy(e => e.Version).ToListAsync();
        history.Count.ShouldBe(5);
        history.Select(e => e.Version).ShouldBe(Enumerable.Range(1, 5).Select(i => (long)i));
        AccountReplay.Rebuild(history).Cash.ShouldBe(account.Cash);
        (await db.OutboxMessages.Select(m => m.Payload).ToListAsync()).Count(p => p.Contains(accountId.ToString())).ShouldBe(2);
    }
}
