using Microsoft.AspNetCore.Mvc;
using TradeSim.Api.Contracts;
using TradeSim.Application.Common;
using TradeSim.Application.Portfolio;

namespace TradeSim.Api.Controllers;

[Route("api/v1/portfolios/{userId:guid}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
public sealed class PortfoliosController(PortfolioQueries portfolios) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PortfolioSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(Guid userId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await portfolios.GetSummaryAsync(userId, cancellationToken));
    }

    [HttpGet("positions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PositionView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPositions(Guid userId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await portfolios.GetPositionsAsync(userId, cancellationToken));
    }

    [HttpGet("transactions")]
    [ProducesResponseType<ApiResponse<PagedResult<TransactionView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(
        Guid userId,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await portfolios.GetTransactionsAsync(userId, type, page, pageSize, cancellationToken));
    }

    [HttpGet("performance")]
    [ProducesResponseType<ApiResponse<PerformanceView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerformance(Guid userId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await portfolios.GetPerformanceAsync(userId, cancellationToken));
    }
}
