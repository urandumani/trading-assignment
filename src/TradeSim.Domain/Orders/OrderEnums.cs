namespace TradeSim.Domain.Orders;

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    Market,
    Limit,
    StopLoss,
    TakeProfit,
}

public enum TimeInForce
{
    Day,

    Gtc,
}

public enum OrderStatus
{
    Open,
    Filled,
    Cancelled,
    Expired,
    Rejected,
}
