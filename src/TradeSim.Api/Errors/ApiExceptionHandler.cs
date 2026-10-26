using Microsoft.AspNetCore.Diagnostics;
using TradeSim.Api.Contracts;
using TradeSim.Domain;

namespace TradeSim.Api.Errors;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, error) = exception switch
        {
            DomainException domain => (ErrorStatusCodes.For(domain.Code), new ApiError(domain.Code, domain.Message)),
            BadHttpRequestException bad => (bad.StatusCode, new ApiError(ErrorCodes.ValidationFailed, "The request could not be read.")),
            _ => (StatusCodes.Status500InternalServerError, new ApiError(ErrorCodes.InternalError, "An unexpected error occurred.")),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request failed with an unexpected error");
        }
        else
        {
            logger.LogInformation("Request rejected: {Code} {Message}", error.Code, error.Message);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error), cancellationToken);
        return true;
    }
}
