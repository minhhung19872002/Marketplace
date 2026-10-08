using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Logistics;
using ShopHub.Infrastructure.Configuration;

namespace ShopHub.Infrastructure.Commerce.Providers;

/// <summary>
/// Giao Hàng Nhanh (dev-online-gateway sandbox by default). GHN has its own district / ward ids: they are looked up
/// from GHN's master data by name (cached), then fee / create / cancel / detail go to shiip/public-api. GHN's status
/// callback is not signed, so the URL registered at GHN carries a secret token (SH_GHN_WEBHOOK_TOKEN).
/// </summary>
public sealed class GhnCarrier(
    IHttpClientFactory http,
    GhnOptions options,
    DivisionNameResolver divisions,
    IMemoryCache cache,
    ILogger<GhnCarrier> logger) : ICarrier
{
    public const string ProviderName = "GHN";
    public const string HttpClientName = "ghn";
    // 2 = hàng nhẹ thương mại điện tử (the standard e-commerce service)
    private const int ServiceType = 2;
    // GHN insures parcels up to ₫5.000.000
    private const long MaxInsurance = 5_000_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Provider => ProviderName;

    // ---------- locations ----------

    private sealed record GhnProvince(int ProvinceID, string ProvinceName, List<string>? NameExtension);

    private sealed record GhnDistrict(int DistrictID, string DistrictName, List<string>? NameExtension);

    private sealed record GhnWard(string WardCode, string WardName, List<string>? NameExtension);

    public sealed record Location(int DistrictId, string WardCode, DivisionNames Names);

    /// <summary>GHN's district id and ward code for a ShopHub route point; null when GHN does not know the place.</summary>
    public async Task<Location?> LocateAsync(RoutePoint point, CancellationToken ct)
    {
        var names = await divisions.NamesAsync(point, ct);
        if (names is null) return null;
        var provinces = await MasterAsync<GhnProvince>("ghn:provinces", "/master-data/province", ct);
        var province = provinces.FirstOrDefault(p => DivisionNameResolver.Matches(names.Province, p.ProvinceName, p.NameExtension));
        if (province is null) return null;
        var districts = await MasterAsync<GhnDistrict>($"ghn:districts:{province.ProvinceID}", $"/master-data/district?province_id={province.ProvinceID}", ct);
        // Two-level address (no district since 2025-07-01): look for the ward by name in every district of the province
        var candidates = names.District is null
            ? districts
            : districts.Where(d => DivisionNameResolver.Matches(names.District, d.DistrictName, d.NameExtension)).Take(1).ToList();
        foreach (var district in candidates)
        {
            var wards = await MasterAsync<GhnWard>($"ghn:wards:{district.DistrictID}", $"/master-data/ward?district_id={district.DistrictID}", ct);
            var ward = wards.FirstOrDefault(w => DivisionNameResolver.Matches(names.Ward, w.WardName, w.NameExtension));
            if (ward is not null) return new Location(district.DistrictID, ward.WardCode, names with { District = names.District ?? district.DistrictName });
        }
        return null;
    }

    private async Task<IReadOnlyList<T>> MasterAsync<T>(string key, string path, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<T>? hit) && hit is not null) return hit;
        var reply = await SendAsync<List<T>>(HttpMethod.Get, path, null, shop: false, ct);
        var list = (IReadOnlyList<T>?)reply?.Data ?? throw new CarrierUnavailableException("Không tải được danh mục địa chỉ của GHN.");
        cache.Set(key, list, TimeSpan.FromHours(12));
        return list;
    }

    // ---------- fee / booking ----------

    private sealed record FeeData(long Total);

    public async Task<long?> QuoteFeeAsync(Carrier carrier, CarrierQuote quote, CancellationToken ct)
    {
        var from = await LocateAsync(quote.From, ct);
        var to = await LocateAsync(quote.To, ct);
        if (from is null || to is null) return null;
        var reply = await SendAsync<FeeData>(HttpMethod.Post, "/v2/shipping-order/fee", new
        {
            service_type_id = ServiceType,
            from_district_id = from.DistrictId,
            from_ward_code = from.WardCode,
            to_district_id = to.DistrictId,
            to_ward_code = to.WardCode,
            weight = quote.ChargeableWeightG,
            insurance_value = Math.Min(quote.ParcelValue, MaxInsurance),
        }, shop: true, ct);
        // GHN answers 400 with a message when it does not serve the route: no option, not an outage
        return reply is { Code: 200, Data: not null } ? reply.Data.Total : null;
    }

    private sealed record CreateData([property: JsonPropertyName("order_code")] string OrderCode);

    public async Task<string> CreateShipmentAsync(Carrier carrier, CarrierParcel parcel, CancellationToken ct)
    {
        if (parcel.Sender is null || parcel.Receiver is null) throw new CarrierUnavailableException("Thiếu địa chỉ người gửi / người nhận.");
        var from = await LocateAsync(parcel.Sender.Point, ct) ?? throw new CarrierUnavailableException("GHN chưa phục vụ địa chỉ lấy hàng của shop.");
        var to = await LocateAsync(parcel.Receiver.Point, ct) ?? throw new CarrierUnavailableException("GHN chưa phục vụ địa chỉ nhận hàng.");
        var reply = await SendAsync<CreateData>(HttpMethod.Post, "/v2/shipping-order/create", new
        {
            payment_type_id = 1, // the shop pays the fee (it was collected from the buyer at checkout)
            note = parcel.Note,
            required_note = "CHOXEMHANGKHONGTHU",
            client_order_code = parcel.OrderCode,
            from_name = parcel.Sender.Name,
            from_phone = parcel.Sender.Phone,
            from_address = parcel.Sender.Street,
            from_ward_name = from.Names.Ward,
            from_district_name = from.Names.District,
            from_province_name = from.Names.Province,
            to_name = parcel.Receiver.Name,
            to_phone = parcel.Receiver.Phone,
            to_address = $"{parcel.Receiver.Street}, {to.Names.Ward}, {to.Names.District}, {to.Names.Province}",
            to_ward_code = to.WardCode,
            to_district_id = to.DistrictId,
            cod_amount = parcel.CodAmount,
            weight = parcel.WeightG,
            insurance_value = Math.Min(parcel.ParcelValue, MaxInsurance),
            service_type_id = ServiceType,
            items = (parcel.Items ?? []).Select(i => new { name = i.Name, quantity = i.Quantity, weight = i.WeightG }).ToList(),
        }, shop: true, ct);
        if (reply is not { Code: 200, Data.OrderCode.Length: > 0 })
            throw new CarrierUnavailableException($"GHN không nhận đơn: {reply?.Message ?? "không kết nối được"}.");
        return reply.Data.OrderCode;
    }

    public async Task CancelShipmentAsync(Carrier carrier, string trackingNo, CancellationToken ct)
    {
        var reply = await SendAsync<JsonElement>(HttpMethod.Post, "/v2/switch-status/cancel", new { order_codes = new[] { trackingNo } }, shop: true, ct);
        if (reply is not { Code: 200 }) throw new CarrierUnavailableException($"GHN không huỷ được vận đơn {trackingNo}: {reply?.Message ?? "không kết nối được"}.");
    }

    // ---------- status ----------

    /// <summary>GHN status code → ShopHub shipment status (statuses with no ShopHub meaning stay at Created and are ignored).</summary>
    public static ShipmentStatus Map(string? status) => status?.ToLowerInvariant() switch
    {
        "picked" => ShipmentStatus.Picked,
        "storing" or "transporting" or "sorting" => ShipmentStatus.InTransit,
        "delivering" or "money_collect_delivering" => ShipmentStatus.OutForDelivery,
        "delivered" => ShipmentStatus.Delivered,
        "delivery_fail" => ShipmentStatus.Failed,
        "waiting_to_return" or "return" or "return_transporting" or "return_sorting" or "returning" or "return_fail" => ShipmentStatus.Returning,
        "returned" => ShipmentStatus.Returned,
        "cancel" => ShipmentStatus.Cancelled,
        _ => ShipmentStatus.Created,
    };

    private static string Describe(string? status) => status?.ToLowerInvariant() switch
    {
        "picked" => "GHN đã lấy hàng",
        "storing" => "Hàng đã nhập kho GHN",
        "transporting" or "sorting" => "Đang trung chuyển",
        "delivering" or "money_collect_delivering" => "Đang giao hàng",
        "delivered" => "Giao hàng thành công",
        "delivery_fail" => "Giao hàng không thành công",
        "returned" => "Đã hoàn hàng về người gửi",
        "cancel" => "Vận đơn đã huỷ",
        _ when status?.StartsWith("return", StringComparison.OrdinalIgnoreCase) == true || status == "waiting_to_return" => "Đang hoàn hàng",
        _ => $"GHN: {status}",
    };

    private sealed record Callback(string OrderCode, string Status, DateTimeOffset? Time, string? Reason, string? Warehouse);

    public CarrierEvent? VerifyWebhook(InboundWebhook webhook)
    {
        if (!ProviderText.SameSecret(webhook.QueryValue("token"), options.WebhookToken)) return null;
        Callback? c;
        try
        {
            c = JsonSerializer.Deserialize<Callback>(webhook.Body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (c is null || string.IsNullOrEmpty(c.OrderCode) || string.IsNullOrEmpty(c.Status)) return null;
        var at = c.Time ?? DateTimeOffset.UtcNow;
        var description = string.IsNullOrWhiteSpace(c.Reason) ? Describe(c.Status) : $"{Describe(c.Status)} — {c.Reason}";
        return new CarrierEvent($"ghn:{c.OrderCode}:{c.Status}:{at.ToUnixTimeSeconds()}", c.OrderCode, Map(c.Status), c.Warehouse, description, at, webhook.Body);
    }

    private sealed record DetailLog(string Status, [property: JsonPropertyName("updated_date")] DateTimeOffset UpdatedDate);

    private sealed record Detail(string Status, List<DetailLog>? Log);

    public async Task<IReadOnlyList<CarrierEvent>> TrackAsync(Carrier carrier, string trackingNo, CancellationToken ct)
    {
        var reply = await SendAsync<Detail>(HttpMethod.Post, "/v2/shipping-order/detail", new { order_code = trackingNo }, shop: false, ct);
        if (reply is not { Code: 200, Data: not null }) return [];
        return (reply.Data.Log ?? []).OrderBy(l => l.UpdatedDate)
            .Select(l => new CarrierEvent($"ghn:{trackingNo}:{l.Status}:{l.UpdatedDate.ToUnixTimeSeconds()}", trackingNo, Map(l.Status), null, Describe(l.Status),
                l.UpdatedDate, JsonSerializer.Serialize(l, Json)))
            .ToList();
    }

    // ---------- HTTP ----------

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private async Task<Envelope<T>?> SendAsync<T>(HttpMethod method, string path, object? body, bool shop, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, options.Endpoint.TrimEnd('/') + path);
        request.Headers.Add("Token", options.Token);
        if (shop) request.Headers.Add("ShopId", options.ShopId.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
            if ((int)response.StatusCode >= 500) throw new CarrierUnavailableException("GHN đang gián đoạn, vui lòng thử lại sau.");
            return await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GHN call {Path} failed", path);
            throw new CarrierUnavailableException("Không kết nối được GHN, vui lòng thử lại sau.");
        }
    }
}
