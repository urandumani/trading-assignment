using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using TradeSim.Api.Contracts;
using TradeSim.Domain;
using TradeSim.Infrastructure.Auth;

namespace TradeSim.Api.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    protected Guid CurrentAccountId => Guid.Parse(User.FindFirst(JwtTokenIssuer.AccountIdClaim)!.Value);

    protected void EnsureCurrentUser(Guid userId)
    {
        if (userId != CurrentUserId)
        {
            throw new DomainException(ErrorCodes.Forbidden, "You can only access your own data.");
        }
    }

    protected OkObjectResult OkEnvelope<T>(T data) => Ok(ApiResponse.Ok(data));
}
