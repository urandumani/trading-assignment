using TradeSim.Application.Orders;

namespace TradeSim.Worker.Orders;

public sealed class DayOrderExpiryService(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<DayOrderExpiryService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<RestingOrderService>().ExpireDayOrdersAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {Count} DAY orders", expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Expiring DAY orders failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
