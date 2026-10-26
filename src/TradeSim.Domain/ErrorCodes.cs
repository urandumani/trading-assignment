namespace TradeSim.Domain;

public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";

    public const string InstrumentNotFound = "INSTRUMENT_NOT_FOUND";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string MarketClosed = "MARKET_CLOSED";
    public const string InstrumentHalted = "INSTRUMENT_HALTED";
    public const string OrderTooSmall = "ORDER_TOO_SMALL";
    public const string RiskLimitExceeded = "RISK_LIMIT_EXCEEDED";
    public const string InsufficientBuyingPower = "INSUFFICIENT_BUYING_POWER";
    public const string InsufficientPosition = "INSUFFICIENT_POSITION";
    public const string OrderNotOpen = "ORDER_NOT_OPEN";

    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string MarketDataUnavailable = "MARKET_DATA_UNAVAILABLE";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}
