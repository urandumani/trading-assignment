using System.Text.Json.Serialization;

namespace TradeSim.Api.Contracts;

public sealed record ApiResponse<T>(bool Success, T? Data, IReadOnlyList<ApiError>? Errors);

public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data) => new(true, data, null);

    public static ApiResponse<object> Fail(params ApiError[] errors) => new(false, null, errors);

    public static ApiResponse<object> Fail(string code, string message) => Fail(new ApiError(code, message));
}
