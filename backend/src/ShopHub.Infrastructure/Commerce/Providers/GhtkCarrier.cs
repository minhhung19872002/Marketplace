using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Logistics;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Commerce.Providers;

/// <summary>
/// Giao Hàng Tiết Kiệm (services-staging.ghtklab.com by default). GHTK takes Vietnamese place names, so ShopHub's
/// division names go as they are. Fee, order 1.5, cancel, A6 label (PDF) and status; the status callback is
/// authenticated by the secret token in the URL registered at GHTK (SH_GHTK_WEBHOOK_TOKEN).
/// </summary>
public sealed class GhtkCarrier(
    IHttpClientFactory http,
    GhtkOptions options,
    DivisionNameResolver divisions,
    ILogger<GhtkCarrier> logger) : ICarrier
{
    public const string ProviderName = "GHTK";
    public const string HttpClientName = "ghtk";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Provider => ProviderName;

    private sealed record FeeInfo(long Fee, bool Delivery);

    private sealed record FeeReply(bool Success, string? Message, FeeInfo? Fee);

    public async Task<long?> QuoteFeeAsync(Carrier carrier, CarrierQuote quote, CancellationToken ct)
    {
        var from = await divisions.NamesAsync(quote.From, ct);
        var to = await divisions.NamesAsync(quote.To, ct);
        if (from is null || to is null) return null;
        var url = QueryHelpers.AddQueryString("/services/shipment/fee", new Dictionary<string, string?>
        {
            ["pick_province"] = from.Province, ["pick_district"] = from.District, ["pick_ward"] = from.Ward,
            ["province"] = to.Province, ["district"] = to.District, ["ward"] = to.Ward,
            ["weight"] = quote.ChargeableWeightG.ToString(CultureInfo.InvariantCulture),
            ["value"] = quote.ParcelValue.ToString(CultureInfo.InvariantCulture),
            ["deliver_option"] = "none", ["transport"] = "road",
        });
        var reply = await SendAsync<FeeReply>(HttpMethod.Get, url, null, ct);
        return reply is { Success: true, Fee: { Delivery: true } fee } ? fee.Fee : null;
    }

    private sealed record OrderInfo(string Label);

    private sealed record OrderReply(bool Success, string? Message, OrderInfo? Order);

    public async Task<string> CreateShipmentAsync(Carrier carrier, CarrierParcel parcel, CancellationToken ct)
    {
        if (parcel.Sender is null || parcel.Receiver is null) throw new CarrierUnavailableException("Thiếu địa chỉ người gửi / người nhận.");
        var from = await divisions.NamesAsync(parcel.Sender.Point, ct) ?? throw new CarrierUnavailableException("Không xác định được địa chỉ lấy hàng.");
        var to = await divisions.NamesAsync(parcel.Receiver.Point, ct) ?? throw new CarrierUnavailableException("Không xác định được địa chỉ nhận hàng.");
        var items = parcel.Items is { Count: > 0 } list ? list : [new CarrierItem($"Đơn {parcel.OrderCode}", 1, parcel.WeightG)];
        var reply = await SendAsync<OrderReply>(HttpMethod.Post, "/services/shipment/order/?ver=1.5", new
        {
            products = items.Select(i => new { name = i.Name, quantity = i.Quantity, weight = Math.Round(i.WeightG / 1000.0, 3) }).ToList(),
            order = new
            {
                id = parcel.OrderCode,
                pick_name = parcel.Sender.Name, pick_tel = parcel.Sender.Phone, pick_address = parcel.Sender.Street,
                pick_province = from.Province, pick_district = from.District, pick_ward = from.Ward,
                name = parcel.Receiver.Name, tel = parcel.Receiver.Phone, address = parcel.Receiver.Street,
                province = to.Province, district = to.District, ward = to.Ward, hamlet = "Khác",
                // the fee was collected at checkout: GHTK collects only the COD amount from the receiver
                is_freeship = "1",
                pick_money = parcel.CodAmount,
                value = parcel.ParcelValue,
                note = parcel.Note,
                transport = "road",
                pick_option = parcel.PickupMethod == PickupMethod.DropOff ? "post" : "cod",
            },
        }, ct);
        if (reply is not { Success: true, Order.Label.Length: > 0 })
            throw new CarrierUnavailableException($"GHTK không nhận đơn: {reply?.Message ?? "không kết nối được"}.");
        return reply.Order.Label;
    }

    private sealed record BasicReply(bool Success, string? Message);

    public async Task CancelShipmentAsync(Carrier carrier, string trackingNo, CancellationToken ct)
    {
        var reply = await SendAsync<BasicReply>(HttpMethod.Post, $"/services/shipment/cancel/{Uri.EscapeDataString(trackingNo)}", null, ct);
        if (reply is not { Success: true }) throw new CarrierUnavailableException($"GHTK không huỷ được vận đơn {trackingNo}: {reply?.Message ?? "không kết nối được"}.");
    }

    public async Task<byte[]?> GetLabelAsync(Carrier carrier, string trackingNo, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"/services/label/{Uri.EscapeDataString(trackingNo)}?original=portrait&paper_size=A6", null);
        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/pdf") return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GHTK label {Label} failed", trackingNo);
            throw new CarrierUnavailableException("Không tải được phiếu giao hàng của GHTK, vui lòng thử lại.");
        }
    }

    /// <summary>GHTK status_id → ShopHub shipment status (statuses with no ShopHub meaning stay at Created and are ignored).</summary>
    public static ShipmentStatus Map(int statusId) => statusId switch
    {
        -1 => ShipmentStatus.Cancelled,
        3 or 123 => ShipmentStatus.Picked,
        10 => ShipmentStatus.InTransit,
        4 => ShipmentStatus.OutForDelivery,
        5 or 6 or 45 => ShipmentStatus.Delivered,
        9 or 49 or 410 => ShipmentStatus.Failed,
        20 => ShipmentStatus.Returning,
        11 or 21 => ShipmentStatus.Returned,
        _ => ShipmentStatus.Created,
    };

    private static string Describe(int statusId, string? reason)
    {
        var text = statusId switch
        {
            -1 => "Vận đơn đã huỷ",
            3 or 123 => "GHTK đã lấy hàng",
            10 => "Đang trung chuyển (giao chậm)",
            4 => "Đang giao hàng",
            5 or 6 or 45 => "Giao hàng thành công",
            9 or 49 or 410 => "Giao hàng không thành công",
            20 => "Đang hoàn hàng",
            11 or 21 => "Đã hoàn hàng về người gửi",
            _ => $"GHTK: trạng thái {statusId}",
        };
        return string.IsNullOrWhiteSpace(reason) ? text : $"{text} — {reason}";
    }

    private sealed record Callback(
        [property: JsonPropertyName("label_id")] string? LabelId,
        [property: JsonPropertyName("partner_id")] string? PartnerId,
        [property: JsonPropertyName("status_id")] int StatusId,
        [property: JsonPropertyName("action_time")] DateTimeOffset? ActionTime,
        [property: JsonPropertyName("reason")] string? Reason);

    public CarrierEvent? VerifyWebhook(InboundWebhook webhook)
    {
        if (!ProviderText.SameSecret(webhook.QueryValue("token"), options.WebhookToken)) return null;
        Callback? c;
        var raw = webhook.Body;
        if (webhook.Header("Content-Type")?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
        {
            var form = QueryHelpers.ParseQuery(webhook.Body);
            // Stored as jsonb with the shipment event
            raw = JsonSerializer.Serialize(form.ToDictionary(f => f.Key, f => f.Value.ToString()));
            c = new Callback(form.GetValueOrDefault("label_id"), form.GetValueOrDefault("partner_id"),
                int.TryParse(form.GetValueOrDefault("status_id"), out var id) ? id : 0,
                DateTimeOffset.TryParse(form.GetValueOrDefault("action_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null,
                form.GetValueOrDefault("reason"));
        }
        else
        {
            try
            {
                c = JsonSerializer.Deserialize<Callback>(webhook.Body, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        if (c is null || string.IsNullOrEmpty(c.LabelId) || c.StatusId == 0) return null;
        var at = c.ActionTime ?? DateTimeOffset.UtcNow;
        return new CarrierEvent($"ghtk:{c.LabelId}:{c.StatusId}:{at.ToUnixTimeSeconds()}", c.LabelId, Map(c.StatusId), null, Describe(c.StatusId, c.Reason),
            at, raw);
    }

    private sealed record StatusInfo([property: JsonPropertyName("label_id")] string LabelId, int Status, DateTimeOffset? Modified);

    private sealed record StatusReply(bool Success, StatusInfo? Order);

    public async Task<IReadOnlyList<CarrierEvent>> TrackAsync(Carrier carrier, string trackingNo, CancellationToken ct)
    {
        var reply = await SendAsync<StatusReply>(HttpMethod.Get, $"/services/shipment/v2/{Uri.EscapeDataString(trackingNo)}", null, ct);
        if (reply is not { Success: true, Order: { } o }) return [];
        var at = o.Modified ?? DateTimeOffset.UtcNow;
        return [new CarrierEvent($"ghtk:{trackingNo}:{o.Status}:{at.ToUnixTimeSeconds()}", trackingNo, Map(o.Status), null, Describe(o.Status, null), at,
            JsonSerializer.Serialize(o, Json))];
    }

    private HttpRequestMessage Request(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, options.Endpoint.TrimEnd('/') + path);
        request.Headers.Add("Token", options.Token);
        if (options.ClientSource is { } source) request.Headers.Add("X-Client-Source", source);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = Request(method, path, body);
        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
            if ((int)response.StatusCode >= 500) throw new CarrierUnavailableException("GHTK đang gián đoạn, vui lòng thử lại sau.");
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GHTK call {Path} failed", path.Split('?')[0]);
            throw new CarrierUnavailableException("Không kết nối được GHTK, vui lòng thử lại sau.");
        }
    }
}
