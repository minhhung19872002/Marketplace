using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using ShopHub.Api.Common;

namespace ShopHub.Api.ErrorHandling;

/// <summary>Model-binding failures (bad JSON, wrong type) as a 400 envelope — never the framework's English text.</summary>
public static class ModelStateResponse
{
    private const string Fallback = "Giá trị không hợp lệ.";

    public static IActionResult Create(ActionContext context)
    {
        var errors = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .SelectMany(kv => kv.Value!.Errors.Select(e => new ApiError(NormaliseField(kv.Key), Translate(e.ErrorMessage))))
            .ToList();

        return new BadRequestObjectResult(ApiResponse.Fail("Dữ liệu gửi lên chưa hợp lệ.", errors));
    }

    // Our own messages are Vietnamese; anything coming from the framework (ASCII English) is replaced
    private static string Translate(string message) =>
        string.IsNullOrWhiteSpace(message) || message.All(c => c < 128) ? Fallback : message;

    private static string NormaliseField(string key)
    {
        var trimmed = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key.TrimStart('$');
        return trimmed.Length == 0 ? string.Empty : JsonNamingPolicy.CamelCase.ConvertName(trimmed);
    }

    public static void UseVietnameseMessages(DefaultModelBindingMessageProvider p)
    {
        p.SetAttemptedValueIsInvalidAccessor((value, field) => $"Giá trị '{value}' không hợp lệ cho {field}.");
        p.SetMissingBindRequiredValueAccessor(field => $"Thiếu giá trị cho {field}.");
        p.SetMissingKeyOrValueAccessor(() => "Thiếu giá trị.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "Thiếu nội dung yêu cầu.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"Giá trị '{value}' không hợp lệ.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => Fallback);
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Giá trị phải là số.");
        p.SetUnknownValueIsInvalidAccessor(field => $"Giá trị của {field} không hợp lệ.");
        p.SetValueIsInvalidAccessor(value => $"Giá trị '{value}' không hợp lệ.");
        p.SetValueMustBeANumberAccessor(field => $"{field} phải là số.");
        p.SetValueMustNotBeNullAccessor(field => $"{field} không được để trống.");
    }
}
