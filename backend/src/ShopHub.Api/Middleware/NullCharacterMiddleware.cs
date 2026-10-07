using System.Text;
using ShopHub.Api.Common;

namespace ShopHub.Api.Middleware;

/// <summary>
/// Rejects U+0000 at the door (query string, JSON body, form / multipart fields and file names — L080). PostgreSQL text columns cannot store it and it is a
/// classic smuggling vector, so the request is refused with a clear 400 instead of failing deep inside.
/// </summary>
public sealed class NullCharacterMiddleware(RequestDelegate next)
{
    private const int MaxInspectedBodyBytes = 10 * 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (ContainsNull(Uri.UnescapeDataString(context.Request.QueryString.Value ?? string.Empty)) ||
            ContainsNull(Uri.UnescapeDataString(context.Request.Path.Value ?? string.Empty)))
        {
            await RejectAsync(context);
            return;
        }

        if (context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true &&
            context.Request.ContentLength is null or <= MaxInspectedBodyBytes)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 8192, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;

            // Raw NUL byte or its JSON escape (\u0000, any case)
            if (ContainsNull(body) || body.Contains("\\u0000", StringComparison.OrdinalIgnoreCase))
            {
                await RejectAsync(context);
                return;
            }
        }

        if (context.Request.HasFormContentType && context.Request.ContentLength is null or <= MaxInspectedBodyBytes * 6)
        {
            // Buffered by ASP.NET: model binding reads the same form afterwards. File contents are binary and not inspected.
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form.Any(f => f.Value.Any(v => v is not null && ContainsNull(v))) || form.Files.Any(f => ContainsNull(f.FileName) || ContainsNull(f.Name)))
            {
                await RejectAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool ContainsNull(string value) => value.Contains('\0');

    private static Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(ApiResponse.Fail("Dữ liệu chứa ký tự không hợp lệ (U+0000)."));
    }
}
