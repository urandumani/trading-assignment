using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TradeSim.Api.Auth;
using TradeSim.Api.Contracts;
using TradeSim.Api.Errors;
using TradeSim.Api.Health;
using TradeSim.Api.Middleware;
using TradeSim.Api.OpenApi;
using TradeSim.Api.RateLimiting;
using TradeSim.Api.RealTime;
using TradeSim.Application;
using TradeSim.Domain;
using TradeSim.Infrastructure;
using TradeSim.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddAspNetCoreInstrumentation(options =>
    options.Filter = context => !context.Request.Path.StartsWithSegments("/health")));
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddInfrastructureChecks();

static void ConfigureJson(JsonSerializerOptions json) =>
    json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
builder.Services.AddControllers().AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = ErrorResponses.FromModelState);

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());

builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddHostedService<TickBroadcaster>();
builder.Services.AddHostedService<OrderUpdatesConsumer>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(ErrorResponses.WriteStatusCodeAsync);

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "TradeSim API v1");
    options.DocumentTitle = "TradeSim API";
});

app.MapControllers();

app.Map("/ws", async (HttpContext context, WebSocketHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(ErrorCodes.ValidationFailed, "Connect with a WebSocket client."));
        return;
    }

    var userId = Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.RunAsync(socket, userId, context.RequestAborted);
}).RequireAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });

app.Run();

public partial class Program;
