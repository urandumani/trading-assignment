using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradeSim.Api.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
        });
}
