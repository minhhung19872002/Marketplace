using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Search;

/// <summary>Minimal typed client over Meilisearch's REST API (v1.x): documents, settings, search, tasks.</summary>
public sealed class MeiliClient
{
    public const string ProductsIndex = "products";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public MeiliClient(HttpClient http, ShopHubSettings settings)
    {
        _http = http;
        _http.BaseAddress = new Uri(settings.MeiliUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(5);
        if (!string.IsNullOrEmpty(settings.MeiliMasterKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.MeiliMasterKey);
    }

    public async Task EnsureIndexAsync(string uid, CancellationToken ct)
    {
        var res = await _http.GetAsync($"indexes/{uid}", ct);
        if (res.IsSuccessStatusCode) return;
        var create = await _http.PostAsJsonAsync("indexes", new { uid, primaryKey = "id" }, Json, ct);
        await WaitAsync(await TaskUidAsync(create, ct), ct);
    }

    public async Task UpdateSettingsAsync(string uid, object settings, CancellationToken ct)
    {
        var res = await _http.PatchAsJsonAsync($"indexes/{uid}/settings", settings, Json, ct);
        await WaitAsync(await TaskUidAsync(res, ct), ct);
    }

    public async Task AddOrReplaceAsync<T>(string uid, IReadOnlyCollection<T> documents, CancellationToken ct)
    {
        if (documents.Count == 0) return;
        var res = await _http.PostAsJsonAsync($"indexes/{uid}/documents", documents, Json, ct);
        await WaitAsync(await TaskUidAsync(res, ct), ct);
    }

    public async Task DeleteAsync(string uid, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var res = await _http.PostAsJsonAsync($"indexes/{uid}/documents/delete-batch", ids, Json, ct);
        await WaitAsync(await TaskUidAsync(res, ct), ct);
    }

    public async Task DeleteAllAsync(string uid, CancellationToken ct)
    {
        var res = await _http.DeleteAsync($"indexes/{uid}/documents", ct);
        await WaitAsync(await TaskUidAsync(res, ct), ct);
    }

    public async Task<long> CountAsync(string uid, CancellationToken ct)
    {
        var res = await _http.GetAsync($"indexes/{uid}/stats", ct);
        if (!res.IsSuccessStatusCode) return -1;
        var node = await res.Content.ReadFromJsonAsync<JsonObject>(Json, ct);
        return node?["numberOfDocuments"]?.GetValue<long>() ?? -1;
    }

    public async Task<JsonObject> SearchAsync(string uid, object body, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync($"indexes/{uid}/search", body, Json, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Meilisearch search failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
        return await res.Content.ReadFromJsonAsync<JsonObject>(Json, ct) ?? throw new HttpRequestException("Empty Meilisearch response");
    }

    private static async Task<long> TaskUidAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Meilisearch request failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
        var node = await res.Content.ReadFromJsonAsync<JsonObject>(Json, ct);
        return node?["taskUid"]?.GetValue<long>() ?? throw new HttpRequestException("Meilisearch did not return a task id");
    }

    // Writes are asynchronous in Meilisearch: wait so that "saved" really means "searchable"
    private async Task WaitAsync(long taskUid, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var node = await _http.GetFromJsonAsync<JsonObject>($"tasks/{taskUid}", Json, ct);
            var status = node?["status"]?.GetValue<string>();
            if (status == "succeeded") return;
            if (status is "failed" or "canceled")
                throw new HttpRequestException($"Meilisearch task {taskUid} {status}: {node?["error"]?.ToJsonString()}");
            await Task.Delay(50, ct);
        }
        throw new TimeoutException($"Meilisearch task {taskUid} did not finish in time");
    }
}
