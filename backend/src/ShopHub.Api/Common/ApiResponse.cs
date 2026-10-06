namespace ShopHub.Api.Common;

public record ApiError(string Field, string Message);

/// <summary>Uniform envelope: { success, data, message, errors } — every message in Vietnamese.</summary>
public record ApiResponse<T>(bool Success, T? Data, string Message, IReadOnlyList<ApiError> Errors);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "") => new(true, data, message, []);

    public static ApiResponse<object> Fail(string message, IReadOnlyList<ApiError>? errors = null) =>
        new(false, null, message, errors ?? []);
}
