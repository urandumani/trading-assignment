using TradeSim.Domain.Market;

namespace TradeSim.Application.MarketData;

public sealed record OrderBookLevel(decimal Price, int Quantity);

public sealed record OrderBook(decimal Price, decimal BestBid, decimal BestAsk, IReadOnlyList<OrderBookLevel> Bids, IReadOnlyList<OrderBookLevel> Asks);

public sealed record RestingOrders(bool IsBuy, decimal Price, int Quantity);

public static class OrderBookBuilder
{
    public static OrderBook Build(decimal price, int depth, IEnumerable<RestingOrders> resting, int seed)
    {
        var step = Math.Max(0.01m, Math.Round(price * 0.0002m, 2));
        var bestBid = RandomWalk.ToTick(price - step);
        var bestAsk = RandomWalk.ToTick(price + step);
        var random = new Random(seed);

        var bids = new Dictionary<decimal, int>();
        var asks = new Dictionary<decimal, int>();
        for (var level = 0; level < depth; level++)
        {
            bids[RandomWalk.ToTick(bestBid - level * step)] = SimulatedSize(level, random);
            asks[bestAsk + level * step] = SimulatedSize(level, random);
        }

        foreach (var orders in resting)
        {
            var side = orders.IsBuy ? bids : asks;
            side[orders.Price] = side.GetValueOrDefault(orders.Price) + orders.Quantity;
        }

        var bidLevels = bids.OrderByDescending(b => b.Key).Take(depth).Select(b => new OrderBookLevel(b.Key, b.Value)).ToList();
        var askLevels = asks.OrderBy(a => a.Key).Take(depth).Select(a => new OrderBookLevel(a.Key, a.Value)).ToList();
        return new OrderBook(price, bidLevels[0].Price, askLevels[0].Price, bidLevels, askLevels);
    }

    private static int SimulatedSize(int level, Random random) =>
        (int)(random.Next(5, 51) * (1 + level * 0.3)) * 10;
}
