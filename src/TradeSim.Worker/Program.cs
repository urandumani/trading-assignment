using Microsoft.EntityFrameworkCore;
using TradeSim.Application;
using TradeSim.Infrastructure;
using TradeSim.Infrastructure.Messaging;
using TradeSim.Infrastructure.Observability;
using TradeSim.Infrastructure.Outbox;
using TradeSim.Infrastructure.Persistence;
using TradeSim.Infrastructure.Persistence.Seeding;
using TradeSim.Worker;
using TradeSim.Worker.MarketData;
using TradeSim.Worker.Orders;

var builder = Host.CreateApplicationBuilder(args);

builder.AddTelemetry();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<PriceHistorySeeder>();

builder.Services.Configure<MarketDataOptions>(builder.Configuration.GetSection(MarketDataOptions.SectionName));
builder.Services.AddHostedService<MarketDataEngine>();
builder.Services.AddHostedService<DayOrderExpiryService>();
builder.Services.AddHostedService<OutboxRelay>();
builder.Services.AddHostedService<PortfolioSnapshotConsumer>();

var host = builder.Build();

ReadinessFile.Register(host);

await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<TradeSimDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<PriceHistorySeeder>().SeedAsync();
}

await host.RunAsync();
