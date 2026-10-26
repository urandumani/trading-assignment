using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using TradeSim.Domain.Market;
using TradeSim.Infrastructure.MarketData;
using TradeSim.Infrastructure.Messaging;
using TradeSim.Infrastructure.Outbox;
using TradeSim.Infrastructure.Persistence;
using TradeSim.Infrastructure.Persistence.Seeding;

namespace TradeSim.IntegrationTests.Api;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string NoRabbitMq = "amqp://guest:guest@localhost:1/";

    public ApiFactory()
        : this(useRabbitMq: true)
    {
    }

    protected ApiFactory(bool useRabbitMq)
    {
        _rabbitMq = useRabbitMq ? new RabbitMqBuilder("rabbitmq:4.1-management-alpine").Build() : null;
    }

    public const string TraderPassword = "Password123!";

    private const int LoadTestUserCount = 80;
    private int _nextUser;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();
    private readonly RabbitMqContainer? _rabbitMq;
    private ConnectionMultiplexer? _redisConnection;

    public RedisPricePublisher Prices { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq?.StartAsync() ?? Task.CompletedTask);

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        Prices = new RedisPricePublisher(_redisConnection);

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await new DatabaseSeeder(db, Options.Create(new SeedOptions
            {
                TraderPassword = TraderPassword,
                LoadTestUsers = true,
                LoadTestUserCount = LoadTestUserCount,
                LoadTestPassword = TraderPassword,
            }),
            TimeProvider.System, NullLogger<DatabaseSeeder>.Instance).SeedAsync();

        var hours = new MarketHours(MarketHours.Cest);
        await new PriceHistorySeeder(db, hours, TimeProvider.System, NullLogger<PriceHistorySeeder>.Instance).SeedAsync(sessionCount: 1);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _redisConnection?.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _rabbitMq?.DisposeAsync().AsTask() ?? Task.CompletedTask);
    }

    public Task RemovePriceAsync(string symbol) =>
        _redisConnection!.GetDatabase().HashDeleteAsync(RedisPricePublisher.PricesKey, symbol);

    public string RabbitMqConnectionString => _rabbitMq?.GetConnectionString() ?? NoRabbitMq;

    public RabbitMqConnection CreateRabbitMqConnection(string? connectionString = null) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = connectionString ?? RabbitMqConnectionString,
            })
            .Build());

    public OutboxRelay CreateOutboxRelay(RabbitMqConnection? rabbitMq = null) =>
        new(Services.GetRequiredService<IServiceScopeFactory>(), rabbitMq ?? CreateRabbitMqConnection(), TimeProvider.System,
            NullLogger<OutboxRelay>.Instance);

    public TradeSimDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TradeSimDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", RabbitMqConnectionString);
        builder.UseSetting("Jwt:Secret", "integration-test-secret-that-is-at-least-32-chars");
        builder.UseSetting("Market:AlwaysOpen", "true");
        builder.UseSetting("RateLimiting:LoginPerMinute", "100000");
        builder.UseSetting("RateLimiting:RequestsPerSecond", "100000");
    }

    public Task<(HttpClient Client, Guid UserId, Guid AccountId)> NewUserAsync() =>
        LoginAsync($"loadtest{Interlocked.Increment(ref _nextUser):D4}");

    public async Task<(HttpClient Client, Guid UserId, Guid AccountId)> LoginAsync(string username)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = TraderPassword });
        response.EnsureSuccessStatusCode();

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
        return (client, data.GetProperty("userId").GetGuid(), data.GetProperty("accountId").GetGuid());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
