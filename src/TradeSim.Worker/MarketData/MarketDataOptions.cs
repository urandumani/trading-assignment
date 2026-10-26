using TradeSim.Domain.Market;

namespace TradeSim.Worker.MarketData;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public int MinTickMs { get; set; } = 100;
    public int MaxTickMs { get; set; } = 500;

    public decimal JumpChance { get; set; } = RandomWalk.DefaultJumpChance;

    public int HaltSeconds { get; set; } = 30;

    public int PersistIntervalSeconds { get; set; } = 5;
}
