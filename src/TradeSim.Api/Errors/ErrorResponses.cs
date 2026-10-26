using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TradeSim.Api.Contracts;
using TradeSim.Domain;

namespace TradeSim.Api.Errors;

public static class ErrorResponses
{
    public static IActionResult FromModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError(
                ErrorCodes.ValidationFailed,
                string.IsNullOrEmpty(error.ErrorMessage) ? "The value is invalid." : error.ErrorMessage,
                ToFieldName(entry.Key))))
            .ToArray();

        return new BadRequestObjectResult(ApiResponse.Fail(errors));
    }

    public static Task WriteStatusCodeAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        var (code, message) = response.StatusCode switch
        {
            StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "No resource exists at this URL."),
            StatusCodes.Status405MethodNotAllowed => ("METHOD_NOT_ALLOWED", "This HTTP method is not supported here."),
            StatusCodes.Status415UnsupportedMediaType => (ErrorCodes.ValidationFailed, "Send the request body as application/json."),
            var other => ($"HTTP_{other}", "The request failed."),
        };

        return response.WriteAsJsonAsync(ApiResponse.Fail(code, message));
    }

    private static string? ToFieldName(string key)
    {
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return name.Length == 0 || name == "$" ? null : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
