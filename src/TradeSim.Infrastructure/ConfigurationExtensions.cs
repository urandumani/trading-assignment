using Microsoft.Extensions.Configuration;

namespace TradeSim.Infrastructure;

public static class ConfigurationExtensions
{
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured (ConnectionStrings__{name}).");
}
