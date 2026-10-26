namespace TradeSim.Worker;

public static class ReadinessFile
{
    public static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tradesim-worker.healthy");

    public static void Register(IHost host)
    {
        File.Delete(Path);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() => File.WriteAllText(Path, DateTimeOffset.UtcNow.ToString("O")));
        lifetime.ApplicationStopping.Register(() => File.Delete(Path));
    }
}
