using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain;
using TradeSim.Domain.Users;

namespace TradeSim.Application.Auth;

public sealed record LoginResult(string AccessToken, string TokenType, int ExpiresIn, Guid UserId, Guid AccountId);

public interface ITokenIssuer
{
    IssuedToken Issue(Guid userId, string username, Guid accountId);
}

public sealed record IssuedToken(string AccessToken, TimeSpan Lifetime);

public sealed class LoginService(IAppDbContext db, IPasswordHasher<User> passwordHasher, ITokenIssuer tokenIssuer)
{
    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            throw new DomainException(ErrorCodes.InvalidCredentials, "Invalid username or password.");
        }

        var accountId = await db.Accounts.Where(a => a.UserId == user.Id).Select(a => a.Id).SingleAsync(cancellationToken);
        var token = tokenIssuer.Issue(user.Id, user.Username, accountId);

        return new LoginResult(token.AccessToken, "Bearer", (int)token.Lifetime.TotalSeconds, user.Id, accountId);
    }
}
