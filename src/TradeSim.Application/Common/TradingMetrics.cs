using System.Diagnostics;
using System.Diagnostics.Metrics;
using TradeSim.Domain.Orders;

namespace TradeSim.Application.Common;

public sealed class TradingMetrics
{
    public const string MeterName = "TradeSim";

    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersRejected;
    private readonly Counter<long> _tradesExecuted;
    private readonly Counter<long> _tradeVolume;
    private readonly Counter<double> _tradeValue;

    public TradingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _ordersPlaced = meter.CreateCounter<long>("tradesim.orders.placed", "{order}", "Orders accepted");
        _ordersRejected = meter.CreateCounter<long>("tradesim.orders.rejected", "{order}", "Orders rejected, by reason");
        _tradesExecuted = meter.CreateCounter<long>("tradesim.trades.executed", "{trade}", "Filled orders");
        _tradeVolume = meter.CreateCounter<long>("tradesim.trade.volume", "{share}", "Shares traded");
        _tradeValue = meter.CreateCounter<double>("tradesim.trade.value", "USD", "Value traded");
    }

    public void OrderPlaced(OrderType type) =>
        _ordersPlaced.Add(1, new KeyValuePair<string, object?>("type", type.ToString()));

    public void OrderRejected(string reason) =>
        _ordersRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void TradeExecuted(string symbol, OrderSide side, int quantity, decimal price)
    {
        var tags = new TagList { { "symbol", symbol }, { "side", side.ToString() } };
        _tradesExecuted.Add(1, tags);
        _tradeVolume.Add(quantity, tags);
        _tradeValue.Add((double)(quantity * price), tags);
    }
}
