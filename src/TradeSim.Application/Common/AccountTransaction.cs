using Microsoft.EntityFrameworkCore;
using TradeSim.Application.Abstractions;
using TradeSim.Domain;

namespace TradeSim.Application.Common;

public static class AccountTransaction
{
    public const int MaxAttempts = 5;

    public static async Task<T> ExecuteAsync<T>(IAppDbContext db, Guid accountId, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await db.Database.ExecuteSqlAsync($"SELECT 1 FROM accounts WHERE id = {accountId} FOR UPDATE", cancellationToken);

                    var result = await work();

                    await transaction.CommitAsync(cancellationToken);
                    return result;
                });
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(10 * Math.Pow(2, attempt) + Random.Shared.Next(0, 20)), cancellationToken);
            }
            catch (DbUpdateException)
            {
                throw new DomainException(ErrorCodes.ConcurrencyConflict,
                    "The account was changed by another request at the same time. Please retry.");
            }
        }
    }
}
