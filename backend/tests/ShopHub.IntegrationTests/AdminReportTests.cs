using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.IntegrationTests.Infrastructure;
using UglyToad.PdfPig;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 12: reports that tie out with independent SQL, exports, penalty consequences and the admin interventions.</summary>
[Collection(ApiCollection.Name)]
public class AdminReportTests(ApiFactory factory)
{
    private const string Product = "Gối Tựa Lưng";

    private sealed record Placed(TestUser Buyer, TestStore Store, Guid OrderId, string Code, long Total, JsonElement Payment);

    private async Task<Placed> PlaceAsync(string method = "Cod", TestStore? store = null, TestUser? buyer = null, int quantity = 1)
    {
        store ??= await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 210_000, 50, "Việt Nam")]);
        buyer ??= await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Skus[Product], quantity })).EnsureSuccessStatusCode();
        var request = new
        {
            addressId, shops = new[] { new { shopId = store.ShopId, voucherCode = (string?)null, carrierCode = (string?)null, note = (string?)null } },
            platformVoucherCode = (string?)null, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = method,
        };
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = quote.GetProperty("grandTotal").GetInt64() }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res = await buyer.Client.SendAsync(msg);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var placed = (await res.ReadEnvelopeAsync()).Data;
        var order = placed.GetProperty("orders")[0];
        return new Placed(buyer, store, Guid.Parse(order.Str("id")), order.Str("code"), placed.GetProperty("grandTotal").GetInt64(), placed.GetProperty("payment"));
    }

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).ToString("yyyy-MM-dd");

    private async Task<long> SqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // Written independently of ReportOrders.Placed: online orders never paid do not count
    private const string PlacedSql =
        "status <> 'PendingPayment' AND NOT (status = 'Cancelled' AND payment_status = 'Unpaid' AND payment_method <> 'Cod')";

    private static string TodaySql =>
        "created_at >= (date_trunc('day', now() AT TIME ZONE 'Asia/Ho_Chi_Minh') AT TIME ZONE 'Asia/Ho_Chi_Minh') " +
        "AND created_at < ((date_trunc('day', now() AT TIME ZONE 'Asia/Ho_Chi_Minh') + interval '1 day') AT TIME ZONE 'Asia/Ho_Chi_Minh')";

    [Fact]
    public async Task Gmv_reports_tie_out_with_independent_sql_and_the_excel_and_pdf_carry_the_same_numbers()
    {
        var a = await PlaceAsync();
        await PlaceAsync(store: a.Store, quantity: 2);
        // Placed but cancelled: still GMV; online and never paid: not GMV
        var cancelled = await PlaceAsync(store: a.Store);
        (await cancelled.Buyer.Client.PostAsJsonAsync($"/api/orders/{cancelled.Code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        await PlaceAsync("Simulated", store: a.Store);

        var admin = await factory.ClientWithPermissionsAsync(Permissions.ReportView);
        var report = (await (await admin.GetAsync($"/api/admin/reports/GmvByTime?from={Today}&to={Today}")).ReadEnvelopeAsync()).Data;
        var totals = report.GetProperty("table").GetProperty("totals");
        var gmv = totals[3].GetInt64();
        gmv.Should().Be(await SqlAsync($"SELECT COALESCE(SUM(subtotal), 0) FROM sales.orders WHERE {TodaySql} AND {PlacedSql}"));
        totals[1].GetInt64().Should().Be(await SqlAsync($"SELECT COUNT(*) FROM sales.orders WHERE {TodaySql} AND {PlacedSql}"));

        var shops = (await (await admin.GetAsync($"/api/admin/reports/TopShops?from={Today}&to={Today}")).ReadEnvelopeAsync()).Data;
        var ours = shops.GetProperty("table").GetProperty("rows").EnumerateArray().Single(r => r[0].GetString() == $"Shop {a.Store.Marker}");
        ours[1].GetInt64().Should().Be(3, "3 đơn tính GMV (kể cả đơn huỷ sau khi đặt), đơn online chưa trả không tính");
        ours[3].GetInt64().Should().Be(4 * 210_000);
        ours[4].GetInt64().Should().Be(3333, "1 / 3 đơn bị huỷ = 33,33%");

        var xlsx = await admin.GetAsync($"/api/admin/reports/GmvByTime/export?from={Today}&to={Today}&format=Xlsx");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync()))
        {
            var sheet = book.Worksheet(1);
            var last = sheet.LastRowUsed()!.RowNumber();
            sheet.Cell(last, 1).GetString().Should().Be("Tổng");
            sheet.Cell(last, 4).GetValue<long>().Should().Be(gmv);
        }
        var pdf = await admin.GetAsync($"/api/admin/reports/GmvByTime/export?from={Today}&to={Today}&format=Pdf");
        using (var doc = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync()))
        {
            var text = string.Join(" ", doc.GetPages().Select(p => p.Text));
            text.Should().Contain("GMV theo thời gian");
            text.Should().Contain($"₫{gmv.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}");
        }

        // A reversed range is refused in one place, with a Vietnamese message
        var bad = await admin.GetAsync("/api/admin/reports/GmvByTime?from=2026-10-10&to=2026-10-01");
        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bad.ReadEnvelopeAsync()).Message.Should().Contain("ngày bắt đầu sau ngày kết thúc");
        var noRight = await factory.ClientWithPermissionsAsync(Permissions.UserView);
        (await noRight.GetAsync("/api/admin/reports/overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_overview_warns_when_no_holiday_of_next_year_is_configured()
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.ReportView);
        var key = Application.SystemConfig.ParameterKeys.LogisticsHolidays;
        var parameters = factory.Services.GetRequiredService<Application.Abstractions.ISystemParameters>();
        var old = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).Select(p => p.Value).SingleAsync());
        var nextYear = Application.Common.VietnamTime.Today(DateTimeOffset.UtcNow).Year + 1;
        try
        {
            (await (await admin.GetAsync("/api/admin/reports/overview")).ReadEnvelopeAsync()).Data.GetProperty("warnings").GetArrayLength()
                .Should().Be(0, "danh sách gieo sẵn có ngày lễ của năm sau");
            await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, $"[\"{nextYear - 1}-09-02\"]")));
            parameters.Invalidate(key);
            var warnings = (await (await admin.GetAsync("/api/admin/reports/overview")).ReadEnvelopeAsync()).Data.GetProperty("warnings");
            warnings.EnumerateArray().Select(w => w.GetString()).Should().ContainSingle(w => w!.Contains($"năm {nextYear}"));
        }
        finally
        {
            await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, old)));
            parameters.Invalidate(key);
        }
    }

    [Fact]
    public async Task Funnel_and_seller_analytics_follow_views_cart_adds_and_orders()
    {
        var store = await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 99_000, 20, "Việt Nam")]);
        var buyer = await factory.CreateUserAsync();
        var productId = store.Products[Product];
        (await buyer.Client.PostAsync($"/api/products/{productId}/views?source=Search", null)).EnsureSuccessStatusCode();
        await PlaceAsync(store: store, buyer: buyer);

        var admin = await factory.ClientWithPermissionsAsync(Permissions.ReportView);
        var funnel = (await (await admin.GetAsync($"/api/admin/reports/Funnel?from={Today}&to={Today}")).ReadEnvelopeAsync()).Data;
        var rows = funnel.GetProperty("table").GetProperty("rows").EnumerateArray().Select(r => r[1].GetInt64()).ToList();
        rows[0].Should().Be(await SqlAsync($"SELECT COUNT(DISTINCT user_id) FROM engage.product_views WHERE user_id IS NOT NULL AND viewed_at >= now() - interval '2 days' AND {TodaySql.Replace("created_at", "viewed_at")}"));
        rows[1].Should().BeGreaterThan(0);

        var seller = await SellerAsync(store);
        var analytics = (await (await seller.GetAsync($"/api/seller/shops/{store.ShopId}/analytics?from={Today}&to={Today}")).ReadEnvelopeAsync()).Data;
        var current = analytics.GetProperty("current");
        current.GetProperty("sales").GetInt64().Should().Be(99_000);
        current.GetProperty("orders").GetInt64().Should().Be(1);
        current.GetProperty("views").GetInt64().Should().Be(1);
        current.GetProperty("conversionBp").GetInt64().Should().Be(10_000, "1 người xem, 1 người mua");
        analytics.GetProperty("traffic")[0].Str("source").Should().Be("Search");
        analytics.GetProperty("topProducts")[0].GetProperty("sold").GetInt64().Should().Be(1);
        var xlsx = await seller.GetAsync($"/api/seller/shops/{store.ShopId}/analytics/export?from={Today}&to={Today}");
        xlsx.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // Another shop's numbers are not reachable
        var stranger = await factory.CreateUserAsync();
        (await stranger.Client.GetAsync($"/api/seller/shops/{store.ShopId}/analytics")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_funnel_follows_one_cohort_so_no_step_is_larger_than_the_one_before_and_the_pdf_draws_its_chart()
    {
        // Someone who never viewed a product page in the period but added to cart and ordered (e.g. "Mua lại")
        var store = await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 99_000, 20, "Việt Nam")]);
        await PlaceAsync(store: store, buyer: await factory.CreateUserAsync());
        var viewer = await factory.CreateUserAsync();
        (await viewer.Client.PostAsync($"/api/products/{store.Products[Product]}/views?source=Search", null)).EnsureSuccessStatusCode();

        var admin = await factory.ClientWithPermissionsAsync(Permissions.ReportView);
        var funnel = (await (await admin.GetAsync($"/api/admin/reports/Funnel?from={Today}&to={Today}")).ReadEnvelopeAsync()).Data;
        var rows = funnel.GetProperty("table").GetProperty("rows").EnumerateArray().Select(r => (Count: r[1].GetInt64(), Rate: r[2].GetInt64())).ToList();
        rows.Should().OnlyContain(r => r.Rate <= 10_000, "không bước nào vượt 100% bước trước");
        string Day(string col) => TodaySql.Replace("created_at", col);
        // Independent SQL: the same cohort, nested step by step
        var viewed = $"SELECT DISTINCT user_id FROM engage.product_views WHERE user_id IS NOT NULL AND {Day("viewed_at")}";
        var carted = $"SELECT DISTINCT user_id FROM engage.cart_adds WHERE user_id IN ({viewed}) AND {Day("added_at")}";
        var ordered = $"SELECT DISTINCT user_id FROM sales.checkout_sessions WHERE user_id IN ({carted}) AND {Day("created_at")}";
        var paid = $"SELECT DISTINCT buyer_id FROM sales.orders WHERE buyer_id IN ({ordered}) AND {Day("created_at")} AND (payment_status <> 'Unpaid' OR (payment_method = 'Cod' AND delivered_at IS NOT NULL))";
        rows.Select(r => r.Count).Should().Equal(
            await SqlAsync($"SELECT COUNT(*) FROM ({viewed}) x"), await SqlAsync($"SELECT COUNT(*) FROM ({carted}) x"),
            await SqlAsync($"SELECT COUNT(*) FROM ({ordered}) x"), await SqlAsync($"SELECT COUNT(*) FROM ({paid}) x"));

        var pdf = await admin.GetAsync($"/api/admin/reports/Funnel/export?from={Today}&to={Today}&format=Pdf");
        using var doc = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", doc.GetPages().Select(p => p.Text));
        text.Should().Contain("Biểu đồ: Số người", "bản PDF có cả biểu đồ, không chỉ bảng");
    }

    private async Task<HttpClient> SellerAsync(TestStore store)
    {
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new ShopStaff(store.ShopId, staff.Id, ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        return staff.Client;
    }

    [Fact]
    public async Task Penalty_points_are_recomputed_from_their_rows_and_each_threshold_has_its_consequence()
    {
        var store = await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 120_000, 20, "Việt Nam")]);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.ShopPenalty, Permissions.ShopView);
        async Task<JsonElement> Add(int points)
        {
            var res = await admin.PostAsJsonAsync($"/api/admin/shops/{store.ShopId}/penalties", new { points, reason = "Đăng hàng vi phạm", expiresInDays = 30 });
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
            return (await res.ReadEnvelopeAsync()).Data;
        }

        (await Add(6)).Str("level").Should().Be("Restricted");
        var buyer = await factory.CreateUserAsync();
        var recommended = (await (await buyer.Client.GetAsync("/api/home/recommendations?pageSize=100")).ReadEnvelopeAsync()).Data;
        recommended.GetProperty("items").EnumerateArray().Select(i => i.Str("id")).Should().NotContain(store.Products[Product].ToString(), "hạn chế hiển thị");

        (await Add(3)).Str("level").Should().Be("CampaignBan");
        var seller = await SellerAsync(store);
        var marketing = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(Random.Shared.Next(20, 300)).ToString("yyyy-MM-dd");
        var slot = await marketing.PostAsJsonAsync("/api/admin/marketing/flash-slots", new { date = day, hour = 12, minDiscountBp = 0, minRating = 0, categoryIds = Array.Empty<Guid>() });
        slot.StatusCode.Should().Be(HttpStatusCode.OK, await slot.Content.ReadAsStringAsync());
        var reg = await seller.PostAsJsonAsync($"/api/seller/shops/{store.ShopId}/marketing/platform-slots/{(await slot.ReadEnvelopeAsync()).Data.GetString()}/items",
            new { items = new[] { new { skuId = store.Skus[Product], flashPrice = 60_000, quota = 5, perUserLimit = 1 } } });
        reg.StatusCode.Should().Be(HttpStatusCode.Conflict, await reg.Content.ReadAsStringAsync());
        (await reg.ReadEnvelopeAsync()).Message.Should().Contain("điểm phạt");

        // An expired penalty stops counting; the total never drifts from the rows
        await factory.WithDbAsync(async db =>
        {
            db.ShopPenalties.Add(new ShopPenalty(store.ShopId, 20, "Cũ, đã hết hạn", null, DateTimeOffset.UtcNow.AddDays(-100), DateTimeOffset.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        });
        var list = (await (await admin.GetAsync($"/api/admin/shops/{store.ShopId}/penalties")).ReadEnvelopeAsync()).Data;
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ShopPenaltyService>().RecomputeAsync(store.ShopId, CancellationToken.None)).Points.Should().Be(9);
        list.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("counts").GetBoolean()).Should().Be(2);

        var locked = await Add(6);
        locked.Str("level").Should().Be("Locked");
        (await factory.WithDbAsync(db => db.Shops.AsNoTracking().SingleAsync(s => s.Id == store.ShopId))).Status.Should().Be(ShopStatus.Locked);

        var first = list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("points").GetInt32() == 6);
        var revoked = await admin.PostAsJsonAsync($"/api/admin/shop-penalties/{first.Str("id")}/revoke", new { reason = "Shop đã khiếu nại thành công" });
        (await revoked.ReadEnvelopeAsync()).Data.GetProperty("points").GetInt32().Should().Be(9);
        (await factory.WithDbAsync(db => db.Shops.AsNoTracking().SingleAsync(s => s.Id == store.ShopId))).PenaltyPoints.Should().Be(9);
    }

    [Fact]
    public async Task Admin_cancels_an_order_with_a_reason_and_a_failed_refund_can_be_sent_to_the_wallet()
    {
        var p = await PlaceAsync("VnPay");
        var paymentId = Guid.Parse(p.Payment.Str("paymentId"));
        // Paid through VNPay, but VNPay will refuse the refund (its fake does not know the transaction)
        var fields = new Dictionary<string, string>
        {
            ["vnp_Amount"] = (p.Total * 100).ToString(), ["vnp_ResponseCode"] = "00", ["vnp_TransactionStatus"] = "00", ["vnp_TmnCode"] = FakeProviders.VnPayTmn,
            ["vnp_TransactionNo"] = "15000001", ["vnp_TxnRef"] = paymentId.ToString("N"),
        };
        var hash = FakeProviders.Hmac512(FakeProviders.VnPayHashData(fields));
        (await factory.CreateClient().GetAsync($"/api/payments/webhooks/vnpay?{FakeProviders.VnPayHashData(fields)}&vnp_SecureHash={hash}")).EnsureSuccessStatusCode();

        var noRight = await factory.ClientWithPermissionsAsync(Permissions.OrderView);
        (await noRight.PostAsJsonAsync($"/api/admin/orders/{p.Code}/cancel", new { reason = "Phát hiện gian lận thanh toán" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.OrderView, Permissions.OrderIntervene);
        (await admin.PostAsJsonAsync($"/api/admin/orders/{p.Code}/cancel", new { reason = "ngắn" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"/api/admin/orders/{p.Code}/cancel", new { reason = "Phát hiện gian lận thanh toán" })).EnsureSuccessStatusCode();

        var detail = (await (await admin.GetAsync($"/api/admin/orders/{p.Code}")).ReadEnvelopeAsync()).Data;
        detail.GetProperty("history").EnumerateArray().Last().Str("actor").Should().Be("Admin");
        var refund = detail.GetProperty("refunds").EnumerateArray().Single();
        refund.Str("status").Should().Be("Failed");

        var resolved = await admin.PostAsJsonAsync($"/api/admin/refunds/{refund.Str("id")}/resolve", new { toWallet = true, reason = "Cổng từ chối, hoàn về ví" });
        resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());
        await factory.DispatchOutboxAsync();
        await factory.DispatchOutboxAsync();
        (await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == p.OrderId))).PaymentStatus.Should().Be(OrderPaymentStatus.Refunded);
        var wallet = (await (await p.Buyer.Client.GetAsync("/api/wallet")).ReadEnvelopeAsync()).Data;
        wallet.GetProperty("balance").GetInt64().Should().Be(p.Total);
        // Resolving twice is refused
        (await admin.PostAsJsonAsync($"/api/admin/refunds/{refund.Str("id")}/resolve", new { toWallet = true, reason = "Lặp lại" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Buyer_reports_a_product_once_and_banning_it_closes_every_open_report()
    {
        var store = await factory.CreateStoreAsync("79", products: [new(Product, "Đèn Bàn", 120_000, 20, "Việt Nam")]);
        var productId = store.Products[Product];
        var a = await factory.CreateUserAsync();
        var b = await factory.CreateUserAsync();
        (await a.Client.PostAsJsonAsync($"/api/products/{productId}/reports", new { reason = "Counterfeit", details = "Logo sai" })).EnsureSuccessStatusCode();
        (await a.Client.PostAsJsonAsync($"/api/products/{productId}/reports", new { reason = "Counterfeit" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await b.Client.PostAsJsonAsync($"/api/products/{productId}/reports", new { reason = "WrongInfo" })).EnsureSuccessStatusCode();

        var admin = await factory.ClientWithPermissionsAsync(Permissions.ProductBan);
        var open = (await (await admin.GetAsync("/api/admin/product-reports?status=Open&pageSize=100")).ReadEnvelopeAsync()).Data;
        var report = open.GetProperty("items").EnumerateArray().First(r => r.Str("productId") == productId.ToString());
        report.GetProperty("openReportsOnProduct").GetInt32().Should().Be(2);
        (await admin.PostAsJsonAsync($"/api/admin/product-reports/{report.Str("id")}/resolve", new { ban = true, resolution = "Hàng giả" })).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.Products.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == productId))).Status.Should().Be(ProductStatus.Banned);
        (await factory.WithDbAsync(db => db.ProductReports.CountAsync(r => r.ProductId == productId && r.Status == ProductReportStatus.Open))).Should().Be(0);

        var tooMany = await admin.PostAsJsonAsync("/api/admin/products/bulk-ban", new { productIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()), reason = "x" });
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest, "lệnh hàng loạt có trần 100");
    }

    [Fact]
    public async Task Cms_help_templates_password_reset_and_gateway_switch_work_end_to_end()
    {
        var anon = factory.CreateClient();
        (await (await anon.GetAsync("/api/cms/quy-che-hoat-dong")).ReadEnvelopeAsync()).Data.Str("title").Should().Be("Quy chế hoạt động sàn");
        var help = (await (await anon.GetAsync("/api/help?q=huy%20don")).ReadEnvelopeAsync()).Data;
        help.EnumerateArray().Select(h => h.Str("slug")).Should().Contain("tro-giup-huy-don", "tìm không dấu");

        var content = await factory.ClientWithPermissionsAsync(Permissions.ContentManage);
        var saved = await content.PostAsJsonAsync("/api/admin/cms", new
        {
            kind = "Page", slug = "trang-thu-" + Guid.NewGuid().ToString("N")[..6], title = "Trang thử", content = "<p>Chào</p><script>alert(1)</script>", sortOrder = 9, isPublished = true,
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await factory.WithDbAsync(db => db.CmsPages.AsNoTracking().SingleAsync(x => x.Title == "Trang thử"))).Content.Should().NotContain("<script");

        var templates = (await (await content.GetAsync("/api/admin/message-templates")).ReadEnvelopeAsync()).Data;
        var otpSms = templates.EnumerateArray().Single(t => t.Str("key") == "OTP" && t.Str("channel") == "Sms");
        (await content.PutAsJsonAsync($"/api/admin/message-templates/{otpSms.Str("id")}", new { body = "Ma {{khong_co}}" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await content.PutAsJsonAsync($"/api/admin/message-templates/{otpSms.Str("id")}", new { body = "ShopHub ma xac thuc {{code}} ({{minutes}} phut)" }))
            .EnsureSuccessStatusCode();
        var phone = ApiFactory.NewPhone();
        (await anon.PostAsJsonAsync("/api/auth/otp/send", new { target = phone, purpose = "Register" })).EnsureSuccessStatusCode();
        await factory.DispatchOutboxAsync();
        (await factory.WithDbAsync(db => db.SimulatedSms.Where(s => s.To == phone).Select(s => s.Content).FirstAsync())).Should().StartWith("ShopHub ma xac thuc ");
        (await content.PutAsJsonAsync($"/api/admin/message-templates/{otpSms.Str("id")}", new
        {
            body = "{{platform}}: Ma {{code}} de {{action}}. Hieu luc {{minutes}} phut. KHONG chia se ma nay cho bat ky ai.",
        })).EnsureSuccessStatusCode();

        // Reset password: the old one stops working, the one-time one works and must be changed
        var user = await factory.CreateUserAsync();
        var ops = await factory.ClientWithPermissionsAsync(Permissions.UserView, Permissions.UserResetPassword);
        var reset = (await (await ops.PostAsync($"/api/admin/users/{user.Id}/reset-password", null)).ReadEnvelopeAsync()).Data.Str("temporaryPassword");
        (await anon.PostAsJsonAsync("/api/auth/login", new { identifier = user.Phone, password = ApiFactory.DefaultPassword })).StatusCode.Should().NotBe(HttpStatusCode.OK);
        var login = await factory.LoginAsync(user.Phone, reset);
        login.User.MustChangePassword.Should().BeTrue();
        (await user.Client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "phiên cũ bị cắt ngay");
        var detail = (await (await ops.GetAsync($"/api/admin/users/{user.Id}")).ReadEnvelopeAsync()).Data;
        detail.GetProperty("devices").GetArrayLength().Should().BeGreaterThan(0);

        // Switching MoMo off removes it from checkout and top-up; it stays queryable for old payments
        var providers = await factory.ClientWithPermissionsAsync(Permissions.ProviderManage, Permissions.SystemParameterUpdate);
        (await providers.PutAsJsonAsync("/api/admin/gateways/MoMo", new { enabled = false })).EnsureSuccessStatusCode();
        try
        {
            var buyer = await factory.CreateUserAsync();
            var gateways = (await (await buyer.Client.GetAsync("/api/wallet/topup-gateways")).ReadEnvelopeAsync()).Data;
            gateways.EnumerateArray().Select(g => g.Str("method")).Should().NotContain("MoMo").And.Contain("VnPay");
            var list = (await (await providers.GetAsync("/api/admin/providers")).ReadEnvelopeAsync()).Data;
            list.GetProperty("gateways").EnumerateArray().Single(g => g.Str("method") == "MoMo").GetProperty("enabled").GetBoolean().Should().BeFalse();
        }
        finally
        {
            (await providers.PutAsJsonAsync("/api/admin/gateways/MoMo", new { enabled = true })).EnsureSuccessStatusCode();
        }

        var audit = await factory.ClientWithPermissionsAsync(Permissions.AuditLogView);
        var started = await audit.PostAsJsonAsync("/api/admin/audit-logs/export-tasks", new { entity = "SystemParameter" });
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        var taskId = Guid.Parse((await started.ReadEnvelopeAsync()).Data.Str("id"));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Application.Features.Seller.BulkTaskRunner>().RunAsync(taskId, CancellationToken.None);
        (await (await audit.GetAsync($"/api/admin/my-tasks/{taskId}")).ReadEnvelopeAsync()).Data.Str("status").Should().Be("Done");
        var other = await factory.ClientWithPermissionsAsync(Permissions.AuditLogView);
        (await other.GetAsync($"/api/admin/my-tasks/{taskId}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound, "tệp của người khác");
        var file = await audit.GetAsync($"/api/admin/my-tasks/{taskId}/file");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        using var book = new XLWorkbook(await file.Content.ReadAsStreamAsync());
        book.Worksheet(1).LastRowUsed()!.RowNumber().Should().BeGreaterThan(4, "có dòng đổi tham số PAYMENT.DISABLED_METHODS");
    }

    [Fact]
    public async Task A_category_moves_with_its_subtree_but_never_deeper_than_three_levels()
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.CategoryManage);
        var (root, mid, leaf) = await factory.WithDbAsync(async db =>
        {
            var r = new Category(null, 1, "Ngành Thử " + Guid.NewGuid().ToString("N")[..4], "nganh-thu-" + Guid.NewGuid().ToString("N")[..6], null, 99, 500);
            db.Categories.Add(r);
            var m = new Category(r.Id, 2, "Nhóm Thử", "nhom-thu-" + Guid.NewGuid().ToString("N")[..6], null, 1, 500);
            db.Categories.Add(m);
            var l = new Category(m.Id, 3, "Lá Thử", "la-thu-" + Guid.NewGuid().ToString("N")[..6], null, 1, 500);
            db.Categories.Add(l);
            // Kept out of the public tree (other tests count its roots)
            foreach (var c in new[] { r, m, l }) c.SetActive(false);
            await db.SaveChangesAsync();
            return (r.Id, m.Id, l.Id);
        });
        // Under its own leaf → refused; a 2-level subtree under a level-2 node would make 4 levels → refused
        (await admin.PutAsJsonAsync($"/api/admin/categories/{mid}/move", new { parentId = leaf, sortOrder = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var otherMid = await factory.WithDbAsync(db => db.Categories.Where(c => c.Level == 2 && c.Id != mid).Select(c => c.Id).FirstAsync());
        (await admin.PutAsJsonAsync($"/api/admin/categories/{mid}/move", new { parentId = otherMid, sortOrder = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        // To the top: the subtree's levels follow
        (await admin.PutAsJsonAsync($"/api/admin/categories/{mid}/move", new { parentId = (Guid?)null, sortOrder = 50 })).EnsureSuccessStatusCode();
        var moved = await factory.WithDbAsync(db => db.Categories.AsNoTracking().Where(c => c.Id == mid || c.Id == leaf).ToListAsync());
        moved.Single(c => c.Id == mid).Level.Should().Be(1);
        moved.Single(c => c.Id == leaf).Level.Should().Be(2);
        _ = root;
    }
}
