using ShopHub.Application.Abstractions;

namespace ShopHub.Api.Common;

/// <summary>Captures a provider notification exactly as it arrived (headers, query string, raw body) for signature checks.</summary>
public static class InboundWebhooks
{
    // Notifications are small; anything bigger is not a provider callback
    private const int MaxBodyChars = 64 * 1024;

    public static async Task<InboundWebhook> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        string body;
        if (request.HasFormContentType)
        {
            // A form may already have been parsed by the pipeline: rebuild the urlencoded body from it
            var form = await request.ReadFormAsync(ct);
            body = string.Join('&', form.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value.ToString())}"));
        }
        else
        {
            using var reader = new StreamReader(request.Body);
            var buffer = new char[MaxBodyChars + 1];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            body = new string(buffer, 0, Math.Min(read, MaxBodyChars));
        }
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
        return new InboundWebhook(headers, query, body);
    }
}
