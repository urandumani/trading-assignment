using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TradeSim.Api.Contracts;
using TradeSim.Api.RateLimiting;
using TradeSim.Application.Auth;

namespace TradeSim.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController(LoginService loginService) : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<LoginResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        OkEnvelope(await loginService.LoginAsync(request.Username, request.Password, cancellationToken));
}

public sealed record LoginRequest(string Username, string Password);
