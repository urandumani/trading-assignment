using TradeSim.Domain;

namespace TradeSim.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class Paging
{
    public const int MaxPageSize = 200;

    public static void Validate(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"page must be at least 1 and pageSize between 1 and {MaxPageSize}.");
        }
    }
}
