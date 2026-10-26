namespace TradeSim.Infrastructure.Persistence.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string TraderPassword { get; set; } = "Password123!";
    public decimal StartingCash { get; set; } = 100_000m;

    public bool LoadTestUsers { get; set; }

    public int LoadTestUserCount { get; set; } = 1000;
    public string LoadTestPassword { get; set; } = "LoadTest123!";
}
