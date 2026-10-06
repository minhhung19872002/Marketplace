using System.Net.Http.Json;
using System.Text.Json;

namespace ShopHub.IntegrationTests.Infrastructure;

public record EnvelopeError(string Field, string Message);

public record Envelope<T>(bool Success, T? Data, string Message, List<EnvelopeError> Errors);

public static class HttpExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<Envelope<T>> ReadEnvelopeAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Envelope<T>>(Json))!;

    public static Task<Envelope<JsonElement>> ReadEnvelopeAsync(this HttpResponseMessage response) =>
        response.ReadEnvelopeAsync<JsonElement>();
}
