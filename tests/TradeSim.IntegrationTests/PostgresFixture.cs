using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradeSim.Infrastructure.Persistence;

namespace TradeSim.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<Func<TradeSimDbContext>> CreateMigratedDatabaseAsync()
    {
        var databaseName = $"test_{Guid.NewGuid():N}";
        await using (var admin = CreateContext(_container.GetConnectionString()))
        {
            var createDatabase = "CREATE DATABASE " + databaseName;
            await admin.Database.ExecuteSqlRawAsync(createDatabase);
        }

        var connectionString = $"{_container.GetConnectionString()};Database={databaseName}";
        await using (var db = CreateContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        return () => CreateContext(connectionString);
    }

    private static TradeSimDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<TradeSimDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
