using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TradeSim.Api.Contracts;
using TradeSim.Application.Common;
using TradeSim.Application.Orders;
using TradeSim.Domain;
using TradeSim.Domain.Orders;

namespace TradeSim.Api.Controllers;

[Route("api/v1/orders")]
public sealed class OrdersController(PlaceOrderService placeOrders, CancelOrderService cancelOrders, OrderQueries orders) : ApiControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";

    [HttpPost]
    [ProducesResponseType<ApiResponse<OrderView>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<OrderView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PlaceOrder(
        PlaceOrderRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureCurrentUser(request.UserId!.Value);
        if (idempotencyKey is { Length: 0 or > 100 })
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"{IdempotencyHeader} must be 1 to 100 characters.");
        }

        var result = await placeOrders.PlaceAsync(new PlaceOrderCommand(
            CurrentUserId,
            CurrentAccountId,
            request.InstrumentSymbol,
            request.Side!.Value,
            request.OrderType!.Value,
            request.TimeInForce ?? TimeInForce.Day,
            request.Quantity,
            request.LimitPrice,
            request.TriggerPrice,
            idempotencyKey,
            HttpContext.TraceIdentifier), cancellationToken);

        var body = ApiResponse.Ok(result.Order);
        return result.Replayed
            ? Ok(body)
            : CreatedAtAction(nameof(GetOrder), new { orderId = result.Order.OrderId }, body);
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<ApiResponse<OrderView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken cancellationToken) =>
        OkEnvelope(await orders.GetAsync(CurrentAccountId, orderId, cancellationToken));

    [HttpPut("{orderId:guid}/cancel")]
    [ProducesResponseType<ApiResponse<OrderView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(Guid orderId, CancellationToken cancellationToken) =>
        OkEnvelope(await cancelOrders.CancelAsync(CurrentUserId, CurrentAccountId, orderId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("/api/v1/users/{userId:guid}/orders")]
    [ProducesResponseType<ApiResponse<PagedResult<OrderView>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOrders(
        Guid userId,
        [FromQuery] OrderStatus? status,
        [FromQuery] string? symbol,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await orders.ListAsync(CurrentAccountId, status, symbol, page, pageSize, cancellationToken));
    }
}

public sealed record PlaceOrderRequest(
    [Required] Guid? UserId,
    [Required, StringLength(10)] string InstrumentSymbol,
    [Required] OrderType? OrderType,
    [Required] OrderSide? Side,
    [Range(1, 1_000_000)] int Quantity,
    [Range(0.01, 1_000_000)] decimal? LimitPrice,
    [Range(0.01, 1_000_000)] decimal? TriggerPrice,
    TimeInForce? TimeInForce);
