using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Phase 14 C6 — every cross-account access in one place (spec 6.1 IDOR), discovered from the running API so a new
/// endpoint is covered the day it is added: shop staff on another shop's routes, a buyer on another buyer's resources,
/// an admin without the permission. Answers must be 404 for owner-bound data (never 403, never data) and 403 for admin RBAC.
/// Also the "only one" rules of spec 6.2 that had no truly parallel test yet.
/// </summary>
[Collection(ApiCollection.Name)]
public class CrossAccessTests(ApiFactory factory)
{
    private sealed record Route(string Method, string Template, IReadOnlyList<object> Metadata);

    private IReadOnlyList<Route> Routes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } t && t.StartsWith("api/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => new Route(m, e.RoutePattern.RawText!, e.Metadata.ToList())))
            .OrderBy(r => r.Template, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();

    private static readonly Regex Parameter = new(@"\{(\*?\*?)([A-Za-z]+)(:[^}]*)?\}");

    /// <summary>Fill the route's parameters: shopId → the other shop, known names → real ids, the rest a fresh guid / word.</summary>
    private static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        Parameter.Replace(template, m => values.TryGetValue(m.Groups[2].Value, out var v) ? v
            : m.Groups[3].Value.Contains("guid", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : "x");

    private static HttpRequestMessage Request(Route r, string url, bool form = false, object? body = null)
    {
        var msg = new HttpRequestMessage(new HttpMethod(r.Method), "/" + url);
        if (r.Method is "POST" or "PUT" or "PATCH")
            msg.Content = body is not null ? JsonContent.Create(body) : form
                ? new MultipartFormDataContent { { new ByteArrayContent("a,b"u8.ToArray()), "file", "tep.csv" } }
                : new StringContent("{}", Encoding.UTF8, "application/json");
        return msg;
    }

    /// <summary>JSON first; an endpoint that only takes a form (file upload) answers 415, then it gets a small form.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Route r, string url, object? body = null)
    {
        var res = await client.SendAsync(Request(r, url, body: body));
        return res.StatusCode == HttpStatusCode.UnsupportedMediaType ? await client.SendAsync(Request(r, url, form: true)) : res;
    }

    [Fact]
    public async Task Staff_of_one_shop_get_not_found_on_every_route_of_another_shop()
    {
        var mine = await factory.CreateStoreAsync("79", products: [new("Hộp Cơm Của Tôi", "Đèn Bàn", 90_000, 5, "Việt Nam")]);
        var theirs = await factory.CreateStoreAsync("01", products: [new("Hộp Cơm Của Họ", "Đèn Bàn", 90_000, 5, "Việt Nam")]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(mine.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
            return 0;
        });
        var values = new Dictionary<string, string>
        {
            ["shopId"] = theirs.ShopId.ToString(),
            ["productId"] = theirs.Products["Hộp Cơm Của Họ"].ToString(),
            ["skuId"] = theirs.Skus["Hộp Cơm Của Họ"].ToString(),
            ["kind"] = "ProductImport",
        };
        var routes = Routes().Where(r => r.Template.StartsWith("api/seller/shops/{shopId", StringComparison.Ordinal)).ToList();
        routes.Should().HaveCountGreaterThan(60, "mọi route của Kênh Người Bán được dò");
        var leaks = new List<string>();
        foreach (var r in routes)
        {
            var res = await SendAsync(staff.Client, r, Fill(r.Template, values));
            if (res.StatusCode != HttpStatusCode.NotFound) leaks.Add($"{r.Method} {r.Template} → {(int)res.StatusCode}");
        }
        leaks.Should().BeEmpty("nhân viên shop X gọi route của shop Y phải nhận 404, không 403, không dữ liệu: " + string.Join("; ", leaks));
    }

    [Fact]
    public async Task An_admin_without_the_permission_is_refused_on_every_admin_route()
    {
        // Holds one unrelated permission: signed in as an admin, but not the right one anywhere else
        var admin = await factory.CreateUserAsync(Permissions.CoinGrant);
        var routes = Routes().Where(r => r.Template.StartsWith("api/admin/", StringComparison.Ordinal)).ToList();
        var leaks = new List<string>();
        foreach (var r in routes)
        {
            var needed = r.Metadata.OfType<RequirePermissionAttribute>().Select(p => p.Permission).ToList();
            if (needed.Count == 0)
            {
                if (r.Metadata.OfType<IAllowAnonymous>().Any() || r.Metadata.OfType<OwnerGuardedAttribute>().Any()) continue;
                leaks.Add($"{r.Method} {r.Template}: không khai quyền");
                continue;
            }
            if (needed.Contains(Permissions.CoinGrant)) continue;
            var res = await SendAsync(admin.Client, r, Fill(r.Template, new Dictionary<string, string>()));
            if (res.StatusCode != HttpStatusCode.Forbidden) leaks.Add($"{r.Method} {r.Template} → {(int)res.StatusCode}");
        }
        leaks.Should().BeEmpty("quản trị không có đúng quyền phải bị từ chối 403 ở mọi route quản trị");
    }

    // ---------- buyer A's resources, reached by buyer B ----------

    /// <summary>Routes with an id that are not one buyer's private data (why), so B may use them with A's ids.</summary>
    private static readonly Dictionary<string, string> NotOwnerBound = new()
    {
        ["api/account/wishlist/{productId:guid}"] = "any public product can be liked",
        ["api/shops/{id:guid}/follow"] = "any shop can be followed",
        ["api/account/vouchers/{voucherId:guid}/claim"] = "public vouchers are saved by anyone (private ones need their code)",
        ["api/media/{purpose}"] = "upload: the purpose is not an id",
        ["api/reviews/{reviewId:guid}/report"] = "anyone may report a review they read",
        ["api/reviews/{reviewId:guid}/helpful"] = "anyone may find a review they read helpful (one vote each, 00 #186)",
        ["api/cart/items/{skuId:guid}"] = "the caller's own cart line of that SKU",
    };

    private async Task SetParameterAsync(string key, string value)
    {
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
    }

    private async Task<(string Code, Guid OrderId, Guid ItemId, Guid CheckoutId)> BuyAsync(TestUser buyer, TestStore store, HttpClient seller, bool complete)
    {
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus["Bình Của A"], quantity = 1 })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = "Cod",
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var placed = (await (await buyer.Client.SendAsync(msg)).ReadEnvelopeAsync()).Data;
        var order = placed.GetProperty("orders")[0];
        var orderId = Guid.Parse(order.Str("id"));
        (await seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/orders/prepare",
            new { orderIds = new[] { orderId }, pickupMethod = "DropOff", pickupSlot = (string?)null })).EnsureSuccessStatusCode();
        await SetParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "0");
        try
        {
            for (var i = 0; i < 4; i++)
            {
                using var scope = factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CarrierSimulator>().RunAsync(CancellationToken.None);
            }
        }
        finally
        {
            await SetParameterAsync(ParameterKeys.LogisticsSimStepSeconds, "120");
        }
        if (complete) (await buyer.Client.PostAsync($"/api/orders/{order.Str("code")}/received", null)).EnsureSuccessStatusCode();
        var itemId = await factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.Id).SingleAsync());
        return (order.Str("code"), orderId, itemId, Guid.Parse(placed.Str("checkoutId")));
    }

    [Fact]
    public async Task A_buyer_gets_not_found_on_every_route_that_holds_another_buyers_data()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Bình Của A", "Bình Giữ Nhiệt", 99_000, 20, "Việt Nam")]);
        var seller = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, seller.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
            return 0;
        });
        var a = await factory.CreateUserAsync();
        var delivered = await BuyAsync(a, store, seller.Client, complete: false);
        var completed = await BuyAsync(a, store, seller.Client, complete: true);
        var addressId = await factory.AddAddressAsync(a.Id, "79");
        static string IdOf(System.Text.Json.JsonElement d) => d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString()! : d.Str("id");
        var review = IdOf((await (await a.Client.PostAsJsonAsync($"/api/orders/{completed.Code}/items/{completed.ItemId}/review",
            new { rating = 5, content = "Bình giữ nhiệt tốt, giao nhanh", anonymous = false })).ReadEnvelopeAsync()).Data);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(FakeImageHostHandler.Png(300, 300)), "file", "bang-chung.png" } };
        var evidence = (await (await a.Client.PostAsync("/api/media/evidence", form)).ReadEnvelopeAsync()).Data.Str("id");
        var returnCode = (await (await a.Client.PostAsJsonAsync($"/api/orders/{delivered.Code}/returns", new
        {
            type = "RefundOnly", reason = "Damaged", description = "Bình bị móp khi mở hộp, có ảnh",
            lines = new[] { new { orderItemId = delivered.ItemId, quantity = 1 } }, evidenceAssetIds = new[] { evidence },
        })).ReadEnvelopeAsync()).Data.Str("code");
        var conversation = (await (await a.Client.PostAsJsonAsync("/api/chat/conversations", new { shopId = store.ShopId })).ReadEnvelopeAsync()).Data.Str("id");
        var session = (await (await a.Client.GetAsync("/api/account/sessions")).ReadEnvelopeAsync()).Data[0].Str("id");
        var topup = (await (await a.Client.PostAsJsonAsync("/api/wallet/topups", new { amount = 50_000, method = "Simulated" })).ReadEnvelopeAsync()).Data.Str("topupId");
        var (bank, notification) = await factory.WithDbAsync(async db =>
        {
            var account = new Domain.Finance.BankAccount(a.Id, "TCB", "ma-hoa", "1234", "NGUOI MUA A", DateTimeOffset.UtcNow);
            db.BankAccounts.Add(account);
            var n = new Domain.Engage.Notification(a.Id, Domain.Engage.NotificationCategory.Activity, "Tin của A", "Chỉ A đọc", null, null, null, DateTimeOffset.UtcNow);
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            return (account.Id, n.Id);
        });

        // Every parameter of a buyer route resolves to something of A's
        var values = new Dictionary<string, string>
        {
            ["id"] = addressId.ToString(), ["sessionId"] = session, ["conversationId"] = conversation, ["code"] = completed.Code,
            ["orderItemId"] = completed.ItemId.ToString(), ["reviewId"] = review, ["bankAccountId"] = bank.ToString(), ["topupId"] = topup,
            ["operation"] = "cancel",
        };
        string Url(Route r) => Fill(r.Template.StartsWith("api/returns/", StringComparison.Ordinal) ? r.Template.Replace("{code}", returnCode) :
            r.Template.StartsWith("api/checkout/", StringComparison.Ordinal) ? r.Template.Replace("{id:guid}", completed.CheckoutId.ToString()) :
            r.Template.StartsWith("api/notifications/", StringComparison.Ordinal) ? r.Template.Replace("{id:guid}", notification.ToString()) : r.Template, values);

        // A well-formed body where the route checks its shape first (a malformed one is refused with 400 before the owner
        // is looked at — that reveals nothing about A; 00 #160): then only the ownership check is left to answer
        var address = (await (await a.Client.GetAsync("/api/account/addresses")).ReadEnvelopeAsync()).Data.EnumerateArray().First(x => x.Str("id") == addressId.ToString());
        var bodies = new Dictionary<string, object>
        {
            ["PUT api/account/addresses/{id:guid}"] = new
            {
                receiverName = "Người B", phone = ApiFactory.NewPhone(), provinceCode = address.Str("provinceCode"),
                wardCode = address.Str("wardCode"), street = "1 Đường B", type = "Home", isDefault = false,
            },
            ["POST api/chat/conversations/{conversationId:guid}/messages"] = new { type = "Text", text = "Xin chào shop" },
            ["POST api/orders/{code}/cancel"] = new { reason = "Đổi ý, không muốn mua nữa" },
            ["POST api/orders/{code}/cancel-request"] = new { reason = "Đổi ý, không muốn mua nữa" },
            ["POST api/orders/{code}/items/{orderItemId:guid}/review"] = new { rating = 5, content = "Hàng tốt, đúng mô tả", anonymous = false },
            ["PUT api/reviews/{reviewId:guid}"] = new { rating = 4, content = "Sửa đánh giá của người khác", anonymous = false },
            ["POST api/orders/{code}/returns"] = new
            {
                type = "RefundOnly", reason = "Damaged", description = "Thử trả hàng đơn của người khác",
                lines = new[] { new { orderItemId = completed.ItemId, quantity = 1 } }, evidenceAssetIds = new[] { evidence },
            },
        };

        var b = await factory.CreateUserAsync();
        var routes = Routes().Where(r => r.Template.Contains('{') && !r.Template.StartsWith("api/seller/", StringComparison.Ordinal)
                                         && !r.Template.StartsWith("api/admin/", StringComparison.Ordinal)
                                         && !r.Metadata.OfType<IAllowAnonymous>().Any()).ToList();
        var leaks = new List<string>();
        foreach (var r in routes.Where(r => !NotOwnerBound.ContainsKey(r.Template)))
        {
            var res = await SendAsync(b.Client, r, Url(r), bodies.GetValueOrDefault($"{r.Method} {r.Template}"));
            if (res.StatusCode != HttpStatusCode.NotFound) leaks.Add($"{r.Method} {Url(r)} → {(int)res.StatusCode}");
        }
        leaks.Should().BeEmpty("người mua B dùng mã / id của A phải nhận 404: " + string.Join("; ", leaks));

        // And A still reaches them (the ids were real)
        (await a.Client.GetAsync($"/api/orders/{completed.Code}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.Client.GetAsync($"/api/returns/{returnCode}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.Client.GetAsync($"/api/chat/conversations/{conversation}/messages")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- "only one" rules under truly parallel requests (spec 6.2) ----------

    [Fact]
    public async Task Five_reviews_of_one_order_line_sent_at_once_store_exactly_one()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Bình Của A", "Bình Giữ Nhiệt", 99_000, 20, "Việt Nam")]);
        var seller = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, seller.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
            return 0;
        });
        var buyer = await factory.CreateUserAsync();
        var order = await BuyAsync(buyer, store, seller.Client, complete: true);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Task.Run(() =>
            buyer.Client.PostAsJsonAsync($"/api/orders/{order.Code}/items/{order.ItemId}/review", new { rating = 5, content = $"Đánh giá lần {i}", anonymous = false }))));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Where(r => !r.IsSuccessStatusCode).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await factory.WithDbAsync(db => db.Reviews.CountAsync(r => r.OrderItemId == order.ItemId))).Should().Be(1);
    }

    [Fact]
    public async Task Two_overlapping_price_programmes_for_one_sku_created_at_once_leave_exactly_one()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Ấm Trùng Giờ", "Bình Giữ Nhiệt", 200_000, 20, "Việt Nam")]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
            return 0;
        });
        var sku = store.Skus["Ấm Trùng Giờ"];
        var url = $"/api/seller/shops/{store.ShopId}/marketing/promotions";
        // 12 at once (L147 / L153: Postgres may answer concurrent exclusion checks with a deadlock — that must be a 409 too)
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => staff.Client.PostAsJsonAsync(url, new
        {
            type = "Discount", name = $"Giảm giá {i}", startAt = DateTimeOffset.UtcNow.AddHours(1 + i), endAt = DateTimeOffset.UtcNow.AddDays(2),
            skus = new[] { new { skuId = sku, price = 150_000 + i * 1_000 } },
        }))));
        // Keep what the server said: a failure of this test must show its cause (L147)
        var answers = await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1, "mọi khoảng thời gian đều chồng nhau: chỉ một chương trình giá được giữ — {0}", string.Join(" | ", answers));
        results.Where(r => !r.IsSuccessStatusCode).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict, string.Join(" | ", answers));
        (await factory.WithDbAsync(db => db.PricePrograms.CountAsync(p => p.SkuId == sku && p.IsActive))).Should().Be(1);
    }

    [Fact]
    public async Task Overlapping_programmes_on_two_skus_listed_in_opposite_orders_never_end_in_a_500()
    {
        // L147: two writers inserting the same pair of SKUs in opposite orders wait on each other inside the exclusion
        // check — Postgres breaks that with a deadlock error. Writers must take turns, every loser gets a 409.
        var store = await factory.CreateStoreAsync("79", products: [new("Ấm Đôi A", "Bình Giữ Nhiệt", 200_000, 50, "Việt Nam"), new("Ấm Đôi B", "Bình Giữ Nhiệt", 210_000, 50, "Việt Nam")]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(store.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
            return 0;
        });
        var (a, b) = (store.Skus["Ấm Đôi A"], store.Skus["Ấm Đôi B"]);
        var url = $"/api/seller/shops/{store.ShopId}/marketing/promotions";
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => staff.Client.PostAsJsonAsync(url, new
        {
            type = "Discount", name = $"Đôi {i}", startAt = DateTimeOffset.UtcNow.AddHours(1), endAt = DateTimeOffset.UtcNow.AddDays(2),
            skus = i % 2 == 0
                ? new[] { new { skuId = a, price = 150_000 }, new { skuId = b, price = 160_000 } }
                : new[] { new { skuId = b, price = 160_000 }, new { skuId = a, price = 150_000 } },
        }))));
        var answers = await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1, string.Join(" | ", answers));
        results.Where(r => !r.IsSuccessStatusCode).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict, string.Join(" | ", answers));
        answers.Should().NotContain(x => x.Contains("thao tác khác đang xử lý"), "chờ lượt thay vì deadlock (không phải thông báo thử lại)");
    }
}
