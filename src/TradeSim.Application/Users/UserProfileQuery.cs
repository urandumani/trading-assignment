using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain;

namespace TradeSim.Application.Users;

public sealed record UserProfile(Guid UserId, string Username, Guid AccountId, string AccountType, decimal MarginRate);

public sealed class UserProfileQuery(IAppDbContext db)
{
    public async Task<UserProfile> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await (
                from user in db.Users
                join account in db.Accounts on user.Id equals account.UserId
                where user.Id == userId
                select new { user.Id, user.Username, AccountId = account.Id, account.RiskLimits.MarginRate })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "User not found.");

        var accountType = profile.MarginRate < 1m ? "MARGIN" : "CASH";
        return new UserProfile(profile.Id, profile.Username, profile.AccountId, accountType, profile.MarginRate);
    }
}
