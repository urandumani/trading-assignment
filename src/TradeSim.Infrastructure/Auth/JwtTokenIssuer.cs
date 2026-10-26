using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TradeSim.Application.Auth;

namespace TradeSim.Infrastructure.Auth;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    public const string AccountIdClaim = "account_id";

    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(Guid userId, string username, Guid accountId)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(settings.ExpiryMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, username),
                new Claim(AccountIdClaim, accountId.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, lifetime);
    }
}
