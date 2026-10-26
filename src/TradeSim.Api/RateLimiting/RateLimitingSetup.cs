using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using TradeSim.Api.Contracts;
using TradeSim.Domain;

namespace TradeSim.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int LoginPerMinute { get; set; } = 5;

    public int RequestsPerSecond { get; set; } = 50;
}

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(ErrorCodes.RateLimited, "Too many requests. Wait for the time in the Retry-After header and try again."),
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var caller = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                             ?? context.Connection.RemoteIpAddress?.ToString()
                             ?? "unknown";
                return RateLimitPartition.GetTokenBucketLimiter(caller, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.RequestsPerSecond,
                    TokensPerPeriod = limits.RequestsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                });
            });

            options.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.LoginPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }
}
