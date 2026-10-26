namespace TradeSim.Infrastructure.MarketData;

public sealed class MarketOptions
{
    public const string SectionName = "Market";

    public string? TimeZone { get; set; }

    public bool AlwaysOpen { get; set; }
}
