using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace ShopHub.IntegrationTests.Infrastructure;

public record RecordedCall(string Host, string Method, string PathAndQuery, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// In-process stand-ins for the VNPay, MoMo, ZaloPay, GHN and GHTK sandboxes. They check what the documentation requires
/// (credentials headers, signatures computed here from the documented formulas — not with ShopHub's code), answer in
/// each provider's format and keep the state a test steers (paid transactions, parcel history, outages).
/// </summary>
public sealed class FakeProviders
{
    public const string VnPayTmn = "SHTEST01";
    public const string VnPaySecret = "VNPAYTESTHASHSECRET0123456789ABCD";
    public const string MoMoPartner = "MOMOSHTEST";
    public const string MoMoAccess = "momo-access-key";
    public const string MoMoSecret = "momo-secret-key-0123456789";
    public const string ZaloPayAppId = "2553";
    public const string ZaloPayKey1 = "zalopay-key1-0123456789";
    public const string ZaloPayKey2 = "zalopay-key2-9876543210";
    // The contract code ZaloPay would give the merchant for trả góp
    public const string ZaloPayInstallment = "installment_credit_card";
    public const string GhnToken = "ghn-test-token";
    public const int GhnShopId = 885123;
    public const string GhnWebhookToken = "ghn-webhook-secret";
    public const string GhtkToken = "ghtk-test-token";
    public const string GhtkWebhookToken = "ghtk-webhook-secret";

    public const string VnPayHost = "vnpay.test";
    public const string MoMoHost = "momo.test";
    public const string ZaloPayHost = "zalopay.test";
    public const string GhnHost = "ghn.test";
    public const string GhtkHost = "ghtk.test";

    private int _seq;

    public ConcurrentQueue<RecordedCall> Calls { get; } = new();

    // txnRef → (transaction no, amount ₫) the "bank" says was paid
    public ConcurrentDictionary<string, (string TxnNo, long Amount)> VnPayPaid { get; } = new();
    // MoMo orderId → (transId, amount)
    public ConcurrentDictionary<string, (long TransId, long Amount)> MoMoPaid { get; } = new();
    // ZaloPay app_trans_id → (zp_trans_id, amount)
    public ConcurrentDictionary<string, (long ZpTransId, long Amount)> ZaloPayPaid { get; } = new();
    public ConcurrentDictionary<string, List<(string Status, DateTimeOffset At)>> GhnLogs { get; } = new();
    public ConcurrentDictionary<string, (int Status, DateTimeOffset At)> GhtkStatus { get; } = new();
    public volatile bool GhnDown;

    /// <summary>Children of an administrative division (null = provinces): code + name, from the test database.</summary>
    public Func<string?, Task<IReadOnlyList<(string Code, string Name)>>>? Divisions { get; set; }

    public IEnumerable<RecordedCall> CallsTo(string host, string pathPrefix) =>
        Calls.Where(c => c.Host == host && c.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));

    public static string Hmac512(string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(VnPaySecret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    public static string Hmac256(string data) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(MoMoSecret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    public static string ZaloPayMac(string key, string data) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    /// <summary>VNPay "hashData": vnp_* fields sorted by key, URL-encoded with spaces as '+', joined by '&amp;'.</summary>
    public static string VnPayHashData(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Where(f => f.Value.Length > 0).OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{WebUtility.UrlEncode(f.Key)}={WebUtility.UrlEncode(f.Value)}"));

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var uri = request.RequestUri!;
        Calls.Enqueue(new RecordedCall(uri.Host, request.Method.Method, uri.PathAndQuery, body, headers));
        return uri.Host switch
        {
            VnPayHost => VnPay(body),
            MoMoHost => MoMo(uri.AbsolutePath, body),
            ZaloPayHost => ZaloPay(uri.AbsolutePath, body),
            GhnHost => await GhnAsync(uri, headers, body),
            GhtkHost => Ghtk(uri, headers),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    // ---------- VNPay merchant_webapi ----------

    private HttpResponseMessage VnPay(string body)
    {
        var r = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;
        string V(string k) => r.GetValueOrDefault(k) ?? "";
        var command = V("vnp_Command");
        var expected = command == "refund"
            ? string.Join('|', V("vnp_RequestId"), V("vnp_Version"), command, V("vnp_TmnCode"), V("vnp_TransactionType"), V("vnp_TxnRef"), V("vnp_Amount"),
                V("vnp_TransactionNo"), V("vnp_TransactionDate"), V("vnp_CreateBy"), V("vnp_CreateDate"), V("vnp_IpAddr"), V("vnp_OrderInfo"))
            : string.Join('|', V("vnp_RequestId"), V("vnp_Version"), command, V("vnp_TmnCode"), V("vnp_TxnRef"), V("vnp_TransactionDate"),
                V("vnp_CreateDate"), V("vnp_IpAddr"), V("vnp_OrderInfo"));
        if (V("vnp_TmnCode") != VnPayTmn || V("vnp_SecureHash") != Hmac512(expected)) return Json(new { vnp_ResponseCode = "97", vnp_Message = "Invalid Checksum" });

        var paid = VnPayPaid.TryGetValue(V("vnp_TxnRef"), out var p) ? p : default;
        var reply = new Dictionary<string, string>
        {
            ["vnp_ResponseId"] = Guid.NewGuid().ToString("N"), ["vnp_Command"] = command, ["vnp_TmnCode"] = VnPayTmn, ["vnp_TxnRef"] = V("vnp_TxnRef"),
            ["vnp_ResponseCode"] = command == "refund" && paid == default ? "91" : "00",
            ["vnp_Message"] = "OK", ["vnp_Amount"] = command == "refund" ? V("vnp_Amount") : ((paid.Amount) * 100).ToString(), ["vnp_BankCode"] = "NCB",
            ["vnp_PayDate"] = "20261006120000", ["vnp_TransactionNo"] = paid.TxnNo ?? "0", ["vnp_TransactionType"] = command == "refund" ? V("vnp_TransactionType") : "01",
            ["vnp_TransactionStatus"] = paid == default ? "01" : "00", ["vnp_OrderInfo"] = V("vnp_OrderInfo"), ["vnp_PromotionCode"] = "", ["vnp_PromotionAmount"] = "",
        };
        string R(string k) => reply.GetValueOrDefault(k) ?? "";
        var data = command == "refund"
            ? string.Join('|', R("vnp_ResponseId"), command, R("vnp_ResponseCode"), R("vnp_Message"), R("vnp_TmnCode"), R("vnp_TxnRef"), R("vnp_Amount"),
                R("vnp_BankCode"), R("vnp_PayDate"), R("vnp_TransactionNo"), R("vnp_TransactionType"), R("vnp_TransactionStatus"), R("vnp_OrderInfo"))
            : string.Join('|', R("vnp_ResponseId"), command, R("vnp_ResponseCode"), R("vnp_Message"), R("vnp_TmnCode"), R("vnp_TxnRef"), R("vnp_Amount"),
                R("vnp_BankCode"), R("vnp_PayDate"), R("vnp_TransactionNo"), R("vnp_TransactionType"), R("vnp_TransactionStatus"), R("vnp_OrderInfo"),
                R("vnp_PromotionCode"), R("vnp_PromotionAmount"));
        reply["vnp_SecureHash"] = Hmac512(data);
        return Json(reply);
    }

    // ---------- MoMo v2 ----------

    private HttpResponseMessage MoMo(string path, string body)
    {
        var r = JsonNode.Parse(body)!.AsObject();
        string V(string k) => r[k]?.ToString() ?? "";
        var raw = path switch
        {
            "/v2/gateway/api/create" =>
                $"accessKey={MoMoAccess}&amount={V("amount")}&extraData={V("extraData")}&ipnUrl={V("ipnUrl")}&orderId={V("orderId")}&orderInfo={V("orderInfo")}" +
                $"&partnerCode={V("partnerCode")}&redirectUrl={V("redirectUrl")}&requestId={V("requestId")}&requestType={V("requestType")}",
            "/v2/gateway/api/query" => $"accessKey={MoMoAccess}&orderId={V("orderId")}&partnerCode={V("partnerCode")}&requestId={V("requestId")}",
            "/v2/gateway/api/refund" =>
                $"accessKey={MoMoAccess}&amount={V("amount")}&description={V("description")}&orderId={V("orderId")}&partnerCode={V("partnerCode")}" +
                $"&requestId={V("requestId")}&transId={V("transId")}",
            _ => null,
        };
        if (raw is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (V("partnerCode") != MoMoPartner || V("signature") != Hmac256(raw))
            return Json(new { resultCode = 11007, message = "Chữ ký không hợp lệ" }, HttpStatusCode.BadRequest);
        return path switch
        {
            "/v2/gateway/api/create" => Json(new { resultCode = 0, message = "Thành công.", payUrl = $"https://test-payment.momo.vn/v2/gateway/pay?t={V("orderId")}" }),
            "/v2/gateway/api/query" => MoMoPaid.TryGetValue(V("orderId"), out var p)
                ? Json(new { resultCode = 0, message = "Thành công.", transId = p.TransId, amount = p.Amount })
                : Json(new { resultCode = 1000, message = "Giao dịch đã được khởi tạo, chờ người dùng xác nhận thanh toán.", transId = 0, amount = 0 }),
            _ => Json(new { resultCode = 0, message = "Thành công.", transId = 9_000_000 + Interlocked.Increment(ref _seq) }),
        };
    }

    // ---------- ZaloPay v2 (form posts, mac with key1) ----------

    private HttpResponseMessage ZaloPay(string path, string body)
    {
        var f = QueryHelpers.ParseQuery(body).ToDictionary(q => q.Key, q => q.Value.ToString());
        string V(string k) => f.GetValueOrDefault(k) ?? "";
        var raw = path switch
        {
            "/v2/create" => $"{V("app_id")}|{V("app_trans_id")}|{V("app_user")}|{V("amount")}|{V("app_time")}|{V("embed_data")}|{V("item")}",
            "/v2/query" => $"{V("app_id")}|{V("app_trans_id")}|{ZaloPayKey1}",
            "/v2/refund" => $"{V("app_id")}|{V("zp_trans_id")}|{V("amount")}|{V("description")}|{V("timestamp")}",
            _ => null,
        };
        if (raw is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (V("app_id") != ZaloPayAppId || V("mac") != ZaloPayMac(ZaloPayKey1, raw))
            return Json(new { return_code = 2, return_message = "Giao dịch thất bại", sub_return_code = -402, sub_return_message = "Mac không hợp lệ" });
        return path switch
        {
            "/v2/create" => Json(new
            {
                return_code = 1, return_message = "Giao dịch thành công", sub_return_code = 1, sub_return_message = "Giao dịch thành công",
                order_url = $"https://qcgateway.zalopay.vn/openinapp?order={V("app_trans_id")}", zp_trans_token = "AC" + V("app_trans_id"),
            }),
            "/v2/query" => ZaloPayPaid.TryGetValue(V("app_trans_id"), out var p)
                ? Json(new { return_code = 1, return_message = "Giao dịch thành công", zp_trans_id = p.ZpTransId, amount = p.Amount, is_processing = false })
                : Json(new { return_code = 3, return_message = "Giao dịch chưa thực hiện", zp_trans_id = 0, amount = 0, is_processing = true }),
            _ => Json(new { return_code = 3, return_message = "Đang hoàn tiền", refund_id = 7_000_000 + Interlocked.Increment(ref _seq) }),
        };
    }

    // ---------- GHN shiip/public-api ----------

    private async Task<HttpResponseMessage> GhnAsync(Uri uri, Dictionary<string, string> headers, string body)
    {
        if (headers.GetValueOrDefault("Token") != GhnToken) return Json(new { code = 401, message = "Token không hợp lệ" }, HttpStatusCode.Unauthorized);
        var path = uri.AbsolutePath.Replace("/shiip/public-api", "", StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (path.StartsWith("/master-data/", StringComparison.Ordinal))
        {
            // GHN ids differ from the GSO codes on purpose: ShopHub must map, not pass codes through
            switch (path)
            {
                case "/master-data/province":
                    return Json(new { code = 200, data = (await Divisions!(null)).Select(d => new { ProvinceID = int.Parse(d.Code) + 200, ProvinceName = d.Name, NameExtension = new[] { d.Name } }) });
                case "/master-data/district":
                    var province = (int.Parse(query["province_id"]!) - 200).ToString("00");
                    return Json(new { code = 200, data = (await Divisions!(province)).Select(d => new { DistrictID = int.Parse(d.Code) + 100_000, DistrictName = d.Name, NameExtension = Array.Empty<string>() }) });
                case "/master-data/ward":
                    var district = (int.Parse(query["district_id"]!) - 100_000).ToString("000");
                    return Json(new { code = 200, data = (await Divisions!(district)).Select(d => new { WardCode = $"W{d.Code}", WardName = d.Name, NameExtension = Array.Empty<string>() }) });
            }
        }
        var shopCall = path is "/v2/shipping-order/fee" or "/v2/shipping-order/create" or "/v2/switch-status/cancel";
        if (shopCall && headers.GetValueOrDefault("ShopId") != GhnShopId.ToString()) return Json(new { code = 400, message = "ShopId không hợp lệ" }, HttpStatusCode.BadRequest);
        var r = JsonNode.Parse(body)?.AsObject();
        switch (path)
        {
            case "/v2/shipping-order/fee":
                if (GhnDown) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                return Json(new { code = 200, message = "Success", data = new { total = 21_000 + r!["weight"]!.GetValue<int>() / 100 } });
            case "/v2/shipping-order/create":
                var code = $"GHN{Interlocked.Increment(ref _seq):D6}";
                GhnLogs[code] = [("ready_to_pick", DateTimeOffset.UtcNow)];
                return Json(new { code = 200, message = "Success", data = new { order_code = code, total_fee = 21_000 } });
            case "/v2/switch-status/cancel":
                return Json(new { code = 200, message = "Success", data = Array.Empty<object>() });
            case "/v2/shipping-order/detail":
                var orderCode = r!["order_code"]!.GetValue<string>();
                var log = GhnLogs.GetValueOrDefault(orderCode) ?? [];
                return Json(new
                {
                    code = 200, message = "Success",
                    data = new { order_code = orderCode, status = log.LastOrDefault().Status, log = log.Select(l => new { status = l.Status, updated_date = l.At }) },
                });
        }
        return Json(new { code = 404, message = "Không tìm thấy" }, HttpStatusCode.NotFound);
    }

    // ---------- GHTK services ----------

    private HttpResponseMessage Ghtk(Uri uri, Dictionary<string, string> headers)
    {
        if (headers.GetValueOrDefault("Token") != GhtkToken) return Json(new { success = false, message = "Token không hợp lệ" }, HttpStatusCode.Unauthorized);
        var path = uri.AbsolutePath;
        if (path == "/services/shipment/fee")
        {
            var q = QueryHelpers.ParseQuery(uri.Query);
            var named = new[] { "pick_province", "pick_district", "province", "district" }.All(k => !string.IsNullOrEmpty(q.GetValueOrDefault(k)));
            return Json(named ? new { success = true, fee = new { fee = 26_000, delivery = true } } : new { success = false, fee = (object?)null, message = "Thiếu địa chỉ" });
        }
        if (path.StartsWith("/services/shipment/order", StringComparison.Ordinal))
        {
            var label = $"S1.A1.{Interlocked.Increment(ref _seq)}";
            GhtkStatus[label] = (2, DateTimeOffset.UtcNow);
            return Json(new { success = true, message = "", order = new { label, fee = 26_000 } });
        }
        if (path.StartsWith("/services/shipment/cancel/", StringComparison.Ordinal)) return Json(new { success = true, message = "" });
        if (path.StartsWith("/services/label/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("%PDF-1.4\n% GHTK label\n%%EOF"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } },
            };
        if (path.StartsWith("/services/shipment/v2/", StringComparison.Ordinal))
        {
            var label = Uri.UnescapeDataString(path["/services/shipment/v2/".Length..]);
            return GhtkStatus.TryGetValue(label, out var s)
                ? Json(new { success = true, order = new { label_id = label, status = s.Status, modified = s.At } })
                : Json(new { success = false, message = "Không tìm thấy đơn" });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>A fresh handler per HttpClientFactory rotation, all forwarding to the one shared fake.</summary>
public sealed class FakeProvidersHandler(FakeProviders fakes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        fakes.HandleAsync(request, cancellationToken);
}
