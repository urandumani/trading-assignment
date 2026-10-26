using TradeSim.Domain;

namespace TradeSim.Api.Errors;

public static class ErrorStatusCodes
{
    public static int For(string code) => code switch
    {
        ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ErrorCodes.InvalidCredentials or ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound or ErrorCodes.InstrumentNotFound or ErrorCodes.OrderNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.OrderNotOpen or ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ErrorCodes.MarketClosed
            or ErrorCodes.InstrumentHalted
            or ErrorCodes.OrderTooSmall
            or ErrorCodes.RiskLimitExceeded
            or ErrorCodes.InsufficientBuyingPower
            or ErrorCodes.InsufficientPosition => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.MarketDataUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
