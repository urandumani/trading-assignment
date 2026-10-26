namespace TradeSim.Domain.Market;

public static class RandomWalk
{
    public const decimal DefaultJumpChance = 1m / 20_000m;
    public const decimal MinJump = 0.05m;
    public const decimal MaxJump = 0.15m;

    public static PriceStep Next(decimal price, decimal maxMovePct, decimal jumpChance, Random random)
    {
        if ((decimal)random.NextDouble() < jumpChance)
        {
            var size = MinJump + (decimal)random.NextDouble() * (MaxJump - MinJump);
            var direction = random.Next(2) == 0 ? -1 : 1;
            return new PriceStep(price * (1 + direction * size), IsJump: true);
        }

        var change = ((decimal)random.NextDouble() * 2 - 1) * maxMovePct / 100m;
        return new PriceStep(price * (1 + change), IsJump: false);
    }

    public static decimal ToTick(decimal price) => Math.Max(0.01m, Math.Round(price, 2, MidpointRounding.AwayFromZero));
}

public readonly record struct PriceStep(decimal Price, bool IsJump);
