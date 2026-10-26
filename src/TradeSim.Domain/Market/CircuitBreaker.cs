namespace TradeSim.Domain.Market;

public static class CircuitBreaker
{
    public const decimal MaxTickMove = 0.10m;

    public static CircuitBreakerResult Apply(decimal previousPrice, decimal proposedPrice)
    {
        var upper = previousPrice * (1 + MaxTickMove);
        var lower = previousPrice * (1 - MaxTickMove);

        if (proposedPrice > upper)
        {
            return new CircuitBreakerResult(upper, Tripped: true);
        }

        if (proposedPrice < lower)
        {
            return new CircuitBreakerResult(lower, Tripped: true);
        }

        return new CircuitBreakerResult(proposedPrice, Tripped: false);
    }
}

public readonly record struct CircuitBreakerResult(decimal Price, bool Tripped);
