using Microsoft.AspNetCore.Mvc;
using TradeSim.Api.Contracts;
using TradeSim.Application.MarketData;

namespace TradeSim.Api.Controllers;

[Route("api/v1/market")]
public sealed class MarketController(MarketDataQueries queries) : ApiControllerBase
{
    [HttpGet("snapshot")]
    [ProducesResponseType<ApiResponse<MarketSnapshot>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshot(CancellationToken cancellationToken) =>
        OkEnvelope(await queries.GetSnapshotAsync(cancellationToken));

    [HttpGet("instruments")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<InstrumentSummary>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInstruments(CancellationToken cancellationToken) =>
        OkEnvelope(await queries.GetInstrumentsAsync(cancellationToken));

    [HttpGet("instruments/{symbol}/price-history")]
    [ProducesResponseType<ApiResponse<PriceHistory>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPriceHistory(
        string symbol,
        [FromQuery] string interval = "1m",
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int limit = 390,
        CancellationToken cancellationToken = default) =>
        OkEnvelope(await queries.GetPriceHistoryAsync(symbol, interval, from, to, limit, cancellationToken));

    [HttpGet("instruments/{symbol}/orderbook")]
    [ProducesResponseType<ApiResponse<OrderBookView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderBook(string symbol, [FromQuery] int depth = 10, CancellationToken cancellationToken = default) =>
        OkEnvelope(await queries.GetOrderBookAsync(symbol, depth, cancellationToken));
}
