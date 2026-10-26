namespace TradeSim.Domain.Orders;

public sealed class Order
{
    private Order() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid InstrumentId { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public OrderSide Side { get; private set; }
    public OrderType Type { get; private set; }
    public TimeInForce TimeInForce { get; private set; }
    public int Quantity { get; private set; }
    public decimal? LimitPrice { get; private set; }

    public decimal? TriggerPrice { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal ReservedAmount { get; private set; }

    public decimal? ExecutedPrice { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public decimal? Commission { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Order Create(
        Guid accountId,
        Guid instrumentId,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        int quantity,
        decimal? limitPrice,
        decimal? triggerPrice,
        string? idempotencyKey,
        DateTimeOffset now,
        Guid? orderId = null)
    {
        if (!Enum.IsDefined(side) || !Enum.IsDefined(type) || !Enum.IsDefined(timeInForce))
        {
            throw Invalid("Order type, side and time in force must be supported values.");
        }

        if (quantity <= 0)
        {
            throw Invalid("Quantity must be greater than zero.");
        }

        switch (type)
        {
            case OrderType.Market when limitPrice is not null || triggerPrice is not null:
                throw Invalid("A MARKET order takes no limitPrice or triggerPrice.");
            case OrderType.Limit when limitPrice is not > 0 || triggerPrice is not null:
                throw Invalid("A LIMIT order needs a positive limitPrice and no triggerPrice.");
            case OrderType.StopLoss or OrderType.TakeProfit when triggerPrice is not > 0 || limitPrice is not null:
                throw Invalid("STOP_LOSS and TAKE_PROFIT orders need a positive triggerPrice and no limitPrice.");
            case OrderType.StopLoss or OrderType.TakeProfit when side != OrderSide.Sell:
                throw Invalid("STOP_LOSS and TAKE_PROFIT orders must be SELL orders.");
        }

        if (limitPrice is { } limit && limit % 0.01m != 0 || triggerPrice is { } trigger && trigger % 0.01m != 0)
        {
            throw Invalid("Prices must be in whole cents (at most 2 decimals).");
        }

        var order = new Order
        {
            Id = orderId ?? Guid.CreateVersion7(),
            AccountId = accountId,
            InstrumentId = instrumentId,
            IdempotencyKey = idempotencyKey,
            Side = side,
            Type = type,
            TimeInForce = timeInForce,
            Quantity = quantity,
            LimitPrice = limitPrice,
            TriggerPrice = triggerPrice,
            Status = OrderStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (type == OrderType.Limit && side == OrderSide.Buy)
        {
            order.ReservedAmount = quantity * limitPrice!.Value + Orders.Commission.For(quantity);
        }

        return order;
    }

    public bool IsTriggeredBy(decimal price) => (Type, Side) switch
    {
        (OrderType.Market, _) => true,
        (OrderType.Limit, OrderSide.Buy) => price <= LimitPrice,
        (OrderType.Limit, OrderSide.Sell) => price >= LimitPrice,
        (OrderType.StopLoss, _) => price <= TriggerPrice,
        (OrderType.TakeProfit, _) => price >= TriggerPrice,
        _ => false,
    };

    public void Fill(decimal price, decimal commission, DateTimeOffset now)
    {
        EnsureOpen();
        Status = OrderStatus.Filled;
        ExecutedPrice = price;
        Commission = commission;
        ExecutedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureOpen();
        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureOpen();
        if (TimeInForce != TimeInForce.Day)
        {
            throw new InvalidOperationException("Only DAY orders expire.");
        }

        Status = OrderStatus.Expired;
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        EnsureOpen();
        Status = OrderStatus.Rejected;
        RejectReason = reason;
        UpdatedAt = now;
    }

    private void EnsureOpen()
    {
        if (Status != OrderStatus.Open)
        {
            throw new DomainException(ErrorCodes.OrderNotOpen, $"Order is {Status.ToString().ToUpperInvariant()}, not OPEN.");
        }
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationFailed, message);
}
