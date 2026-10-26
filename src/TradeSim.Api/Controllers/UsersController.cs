using Microsoft.AspNetCore.Mvc;
using TradeSim.Api.Contracts;
using TradeSim.Application.Users;

namespace TradeSim.Api.Controllers;

[Route("api/v1/users")]
public sealed class UsersController(UserProfileQuery profiles) : ApiControllerBase
{
    [HttpGet("{userId:guid}")]
    [ProducesResponseType<ApiResponse<UserProfile>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProfile(Guid userId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(userId);
        return OkEnvelope(await profiles.GetAsync(userId, cancellationToken));
    }
}
