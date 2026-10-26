using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TradeSim.Domain.Events;
using TradeSim.Infrastructure.Persistence;
using TradeSim.Infrastructure.Persistence.Seeding;

namespace TradeSim.IntegrationTests.Persistence;

public class DatabaseTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static DatabaseSeeder Seeder(TradeSimDbContext db, SeedOptions? options = null) =>
        new(db, Options.Create(options ?? new SeedOptions()), TimeProvider.System, NullLogger<DatabaseSeeder>.Instance);

    [Fact]
    public async Task Seeding_creates_instruments_traders_and_their_opening_events()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await Seeder(db).SeedAsync();
        }

        await using var check = newContext();
        (await check.Instruments.CountAsync()).ShouldBe(12);
        (await check.Users.Select(u => u.Username).OrderBy(u => u).ToListAsync())
            .ShouldBe(["trader1", "trader2", "trader3", "trader4", "trader5"]);

        var accounts = await check.Accounts.ToListAsync();
        accounts.Count.ShouldBe(5);
        accounts.ShouldAllBe(a => a.Cash == 100_000m && a.ReservedCash == 0m);
        accounts.Count(a => a.RiskLimits.MarginRate == 0.5m).ShouldBe(2);

        var events = await check.AccountEvents.ToListAsync();
        events.Count.ShouldBe(5);
        events.ShouldAllBe(e => e.Type == AccountEventTypes.AccountOpened && e.Version == 1);
    }

    [Fact]
    public async Task Seeding_again_adds_nothing()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await Seeder(db).SeedAsync();
        }

        await using (var db = newContext())
        {
            await Seeder(db).SeedAsync();
        }

        await using var check = newContext();
        (await check.Instruments.CountAsync()).ShouldBe(12);
        (await check.Users.CountAsync()).ShouldBe(5);
        (await check.AccountEvents.CountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task Load_test_users_are_seeded_only_when_enabled()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await Seeder(db, new SeedOptions { LoadTestUsers = true, LoadTestUserCount = 3 }).SeedAsync();
        }

        await using var check = newContext();
        (await check.Users.Where(u => u.Username.StartsWith("loadtest")).Select(u => u.Username).OrderBy(u => u).ToListAsync())
            .ShouldBe(["loadtest0001", "loadtest0002", "loadtest0003"]);
    }

    [Fact]
    public async Task Concurrent_updates_to_the_same_account_are_detected()
    {
        var newContext = await postgres.CreateMigratedDatabaseAsync();
        await using (var db = newContext())
        {
            await Seeder(db).SeedAsync();
        }

        await using var first = newContext();
        await using var second = newContext();
        var accountA = await first.Accounts.FirstAsync();
        var accountB = await second.Accounts.SingleAsync(a => a.Id == accountA.Id);

        accountA.Debit(60_000m);
        await first.SaveChangesAsync();

        accountB.Debit(60_000m);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
