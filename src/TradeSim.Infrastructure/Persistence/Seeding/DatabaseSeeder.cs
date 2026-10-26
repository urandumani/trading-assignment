using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Users;

namespace TradeSim.Infrastructure.Persistence.Seeding;

public sealed class DatabaseSeeder(
    TradeSimDbContext db,
    IOptions<SeedOptions> options,
    TimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    private static readonly (string Symbol, string Name, decimal MaxMovePct, int MinQuantity, decimal Price)[] InstrumentData =
    [
        ("APXL", "Apex Labs",                0.030m,  1, 150.00m),
        ("NOVA", "Nova Dynamics",            0.035m,  1,  85.00m),
        ("QNTM", "Quantum Ridge Computing",  0.060m,  1, 220.00m),
        ("HELX", "Helix Biotherapeutics",    0.090m,  1,  42.00m),
        ("GRDX", "GridEx Utilities",         0.010m,  1,  60.00m),
        ("BRKX", "Bricks & Mortar REIT",     0.015m,  1,  35.00m),
        ("AERO", "Aerolux Airlines",         0.050m,  1,  28.00m),
        ("FINX", "Finexa Bank",              0.025m,  1,  95.00m),
        ("SOLR", "Solaris Renewables",       0.055m,  1,  18.00m),
        ("MEDI", "Medicore Health",          0.020m,  1, 120.00m),
        ("LUXE", "Luxe Maison Group",        0.030m,  1, 310.00m),
        ("CRYP", "Cryptonic Mining",         0.120m, 10,  12.00m),
    ];

    private static readonly (string Username, bool Margin)[] Traders =
    [
        ("trader1", false),
        ("trader2", false),
        ("trader3", false),
        ("trader4", true),
        ("trader5", true),
    ];

    private readonly SeedOptions _options = options.Value;
    private readonly PasswordHasher<User> _hasher = new();

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedInstrumentsAsync(cancellationToken);

        var traders = Traders.Select(t => (t.Username, t.Margin));
        await SeedUsersAsync(traders, _options.TraderPassword, cancellationToken);

        if (_options.LoadTestUsers)
        {
            var loadTestUsers = Enumerable.Range(1, _options.LoadTestUserCount).Select(i => ($"loadtest{i:D4}", false));
            await SeedUsersAsync(loadTestUsers, _options.LoadTestPassword, cancellationToken);
        }
    }

    private async Task SeedInstrumentsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Instruments.Select(i => i.Symbol).ToHashSetAsync(cancellationToken);
        var missing = InstrumentData.Where(i => !existing.Contains(i.Symbol)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.Instruments.AddRange(missing.Select(i => Instrument.Create(i.Symbol, i.Name, i.MaxMovePct, i.MinQuantity, i.Price)));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} instruments", missing.Count);
    }

    private async Task SeedUsersAsync(IEnumerable<(string Username, bool Margin)> users, string password, CancellationToken cancellationToken)
    {
        var existing = await db.Users.Select(u => u.Username).ToHashSetAsync(cancellationToken);
        var missing = users.Where(u => !existing.Contains(u.Username)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var passwordHash = _hasher.HashPassword(null!, password);
        var now = clock.GetUtcNow();
        var openedPayload = JsonSerializer.Serialize(new { initialCash = _options.StartingCash });

        foreach (var (username, margin) in missing)
        {
            var user = User.Create(username, passwordHash);
            var account = Account.Open(user.Id, _options.StartingCash, margin ? RiskLimits.MarginAccount() : RiskLimits.CashAccount());

            db.Users.Add(user);
            db.Accounts.Add(account);
            db.AccountEvents.Add(AccountEvent.Create(account.Id, 1, AccountEventTypes.AccountOpened, openedPayload, null, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} users with accounts", missing.Count);
    }
}
