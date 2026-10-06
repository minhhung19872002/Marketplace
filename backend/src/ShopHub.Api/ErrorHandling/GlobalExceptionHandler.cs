using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using ShopHub.Api.Common;
using ShopHub.Application.Common;
using ShopHub.Domain.Common;

namespace ShopHub.Api.ErrorHandling;

/// <summary>Single place that turns exceptions into the API envelope with a Vietnamese message.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, body) = exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, ApiResponse.Fail(
                "Dữ liệu gửi lên chưa hợp lệ.",
                ve.Errors.Select(e => new ApiError(ToCamel(e.PropertyName), e.ErrorMessage)).ToList())),
            NotFoundException nf => (StatusCodes.Status404NotFound, ApiResponse.Fail(nf.Message)),
            ConflictException ce => (StatusCodes.Status409Conflict, ApiResponse.Fail(ce.Message)),
            BusinessRuleException be => (StatusCodes.Status409Conflict, ApiResponse.Fail(be.Message)),
            BadHttpRequestException bhr => (bhr.StatusCode, ApiResponse.Fail(
                bhr.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Dữ liệu gửi lên quá lớn."
                    : "Yêu cầu không hợp lệ.")),
            _ => (StatusCodes.Status500InternalServerError, ApiResponse.Fail(
                $"Đã có lỗi hệ thống. Vui lòng thử lại sau (mã tra cứu: {Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier}).")),
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(body, ct);
        return true;
    }

    private static string ToCamel(string propertyName) =>
        string.IsNullOrEmpty(propertyName) ? string.Empty : JsonNamingPolicy.CamelCase.ConvertName(propertyName);
}

public static class StatusCodeEnvelope
{
    // Bodiless 401/403/404/405… (no route, no permission) also get the JSON envelope in Vietnamese
    public static async Task WriteAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        var message = response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "Bạn cần đăng nhập để tiếp tục.",
            StatusCodes.Status403Forbidden => "Bạn không có quyền thực hiện thao tác này.",
            StatusCodes.Status404NotFound => "Không tìm thấy đường dẫn yêu cầu.",
            StatusCodes.Status405MethodNotAllowed => "Phương thức không được hỗ trợ.",
            StatusCodes.Status415UnsupportedMediaType => "Định dạng dữ liệu không được hỗ trợ.",
            StatusCodes.Status429TooManyRequests => "Bạn thao tác quá nhanh, vui lòng thử lại sau giây lát.",
            _ => "Yêu cầu không thành công.",
        };
        await response.WriteAsJsonAsync(ApiResponse.Fail(message));
    }
}
