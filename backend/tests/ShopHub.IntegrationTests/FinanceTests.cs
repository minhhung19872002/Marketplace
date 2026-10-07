using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Finance;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Infrastructure.Commerce;
using ShopHub.IntegrationTests.Infrastructure;
using UglyToad.PdfPig;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FinanceTests(ApiFactory factory)
{
    private const string Product = "Bình Giữ Nhiệt Inox";
    private const long Price = 99_999;

    private sealed record Store(TestStore Shop, TestUser Staff, Guid LeafId);

    private sealed record Bought(TestUser Buyer, Guid OrderId, string Code, Guid ItemId);

    private async Task<Store> StoreAsync(int stock = 50)
    {
        var shop = await factory.CreateStoreAsync("79", products: [new(Product, "Bình Giữ Nhiệt", Price, stock, "Việt Nam")]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(shop.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });
        var leaf = await factory.WithDbAsync(db => db.Products.Where(p => p.Id == shop.Products[Product]).Select(p => p.CategoryId).SingleAsync());
        return new Store(shop, staff, leaf);
    }

    private async Task SetParameterAsync(string key, string value)
    {
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
        factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
    }

    private async Task<string> ParameterAsync(string key) =>
        await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).Select(p => p.Value).SingleAsync());

    private async Task WithParametersAsync(Func<Task> work, params (string Key, string Value)[] values)
    {
        var old = new List<(string, string)>();
        foreach (var (key, value) in values)
        {
            old.Add((key, await ParameterAsync(key)));
            await SetParameterAsync(key, value);
        }
        try
        {
            await work();
        }
        finally
        {
            foreach (var (key, value) in old) await SetParameterAsync(key, value);
        }
    }

    private async Task DrainOutboxAsync()
    {
        for (var i = 0; i < 50 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<long> BalanceAsync(AccountKey key) => InScopeAsync(sp => sp.GetRequiredService<Ledger>().BalanceAsync(key, CancellationToken.None));

    private async Task CarrierStepsAsync(int times) =>
        await WithParametersAsync(async () =>
        {
            for (var i = 0; i < times; i++) await InScopeAsync(async sp => { await sp.GetRequiredService<CarrierSimulator>().RunAsync(CancellationToken.None); return 0; });
        }, (ParameterKeys.LogisticsSimStepSeconds, "0"));

    private static HttpRequestMessage PlaceMessage(object request, long total, string? pin = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { checkout = request, expectedGrandTotal = total, walletPin = pin }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return msg;
    }

    private static object CheckoutRequest(Guid addressId, Guid shopId, string method, string? shopVoucher = null, string? platformVoucher = null) => new
    {
        addressId,
        shops = new[] { new { shopId, voucherCode = shopVoucher, carrierCode = (string?)null, note = (string?)null } },
        platformVoucherCode = platformVoucher, freeshipVoucherCode = (string?)null, useCoins = false, paymentMethod = method,
    };

    private async Task<Bought> BuyAsync(Store store, int quantity, string method = "Cod", bool deliver = true, bool complete = true, string? shopVoucher = null,
        string? platformVoucher = null, TestUser? buyer = null, string? pin = null)
    {
        buyer ??= await factory.CreateUserAsync();
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Shop.Skus[Product], quantity })).EnsureSuccessStatusCode();
        var request = CheckoutRequest(addressId, store.Shop.ShopId, method, shopVoucher, platformVoucher);
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var res = await buyer.Client.SendAsync(PlaceMessage(request, quote.GetProperty("grandTotal").GetInt64(), pin));
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var placed = (await res.ReadEnvelopeAsync()).Data;
        if (method == "Simulated")
            (await factory.CreateClient().PostAsync($"/api/payments/simulated/{placed.GetProperty("payment").Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
        var order = placed.GetProperty("orders")[0];
        var orderId = Guid.Parse(order.Str("id"));
        if (deliver)
        {
            (await store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/orders/prepare",
                new { orderIds = new[] { orderId }, pickupMethod = "DropOff", pickupSlot = (string?)null })).EnsureSuccessStatusCode();
            await CarrierStepsAsync(4);
            if (complete) (await buyer.Client.PostAsync($"/api/orders/{order.Str("code")}/received", null)).EnsureSuccessStatusCode();
        }
        await DrainOutboxAsync();
        var itemId = await factory.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == orderId).Select(i => i.Id).SingleAsync());
        return new Bought(buyer, orderId, order.Str("code"), itemId);
    }

    private async Task<SettlementService.RunResult> ReleaseAsync() =>
        await InScopeAsync(sp => sp.GetRequiredService<SettlementService>().RunAsync(CancellationToken.None));

    private async Task<LedgerCheckResult> CheckLedgerAsync() =>
        await InScopeAsync(sp => sp.GetRequiredService<LedgerCheckService>().RunAsync(CancellationToken.None));

    private async Task SetFixedFeeAsync(Guid categoryId, int bp)
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceFeeManage);
        var res = await admin.PostAsJsonAsync("/api/admin/finance/fee-rules",
            new { categoryId, feeType = "Fixed", rateBp = bp, validFrom = DateTimeOffset.UtcNow.AddSeconds(-1), note = "Phí thử" });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    private async Task<int> PaymentFeeBpAsync() =>
        await factory.WithDbAsync(db => db.FeeRules.Where(r => r.FeeType == FeeType.Payment && r.CategoryId == null && r.ValidTo == null)
            .Select(r => r.RateBp).SingleAsync());

    private static long Bp(long amount, int bp) => (long)Math.Round(amount * (decimal)bp / 10_000, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task A_drifted_cached_balance_is_overwritten_with_the_sum_of_its_entries_and_finance_admins_are_alerted()
    {
        var shop = await factory.CreateStoreAsync("79", products: [new(Product, "Bình Giữ Nhiệt", Price, 5, "Việt Nam")]);
        await FundAsync(shop.ShopId, 500_000);
        var admin = await factory.CreateUserAsync([Permissions.FinanceLedgerView]);
        // Someone (a bad migration, a hand fix) moved the cached column without an entry
        await factory.WithDbAsync(db => db.LedgerAccounts
            .Where(a => a.OwnerType == LedgerOwnerType.Shop && a.OwnerId == shop.ShopId && a.Type == LedgerAccountType.ShopAvailable)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Balance, a => a.Balance + 77_777)));
        (await CheckLedgerAsync()).Mismatches.Should().ContainSingle(m => m.OwnerId == shop.ShopId && m.Cached == 577_777 && m.FromEntries == 500_000);

        var result = await InScopeAsync(sp => sp.GetRequiredService<LedgerCheckService>().RepairAsync(CancellationToken.None));
        result.Repaired.Should().BeGreaterThanOrEqualTo(1);
        (await factory.WithDbAsync(db => db.LedgerAccounts.AsNoTracking()
            .SingleAsync(a => a.OwnerType == LedgerOwnerType.Shop && a.OwnerId == shop.ShopId && a.Type == LedgerAccountType.ShopAvailable)))
            .Balance.Should().Be(500_000, "số dư là tổng bút toán");
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
        (await factory.WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == admin.Id).Select(n => n.Title).ToListAsync()))
            .Should().Contain("Kiểm tra sổ cái phát hiện chênh lệch", "lệch phải tới tay quản trị tài chính, không chỉ nằm trong log");
    }

    [Fact]
    public async Task A_ledger_posting_outside_a_transaction_is_refused()
    {
        var act = () => InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<Ledger>().PostAsync("TEST_FUND", "test", Guid.NewGuid(), "Không có giao dịch",
            [
                new LedgerLine(AccountKey.Platform(LedgerAccountType.PlatformCash), LedgerDirection.Debit, 1_000),
                new LedgerLine(AccountKey.Platform(LedgerAccountType.PlatformSubsidy), LedgerDirection.Credit, 1_000),
            ], null, CancellationToken.None);
            return 0;
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Gives a shop money it can withdraw (a released order is the real way; the tests that only need a balance post it).</summary>
    private async Task FundAsync(Guid shopId, long amount) =>
        await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<IApplicationDbContext>();
            await using var tx = await db.BeginTransactionAsync(CancellationToken.None);
            await sp.GetRequiredService<Ledger>().PostAsync("TEST_FUND", "test", Guid.NewGuid(), "Nạp số dư thử",
            [
                new LedgerLine(AccountKey.Platform(LedgerAccountType.PlatformCash), LedgerDirection.Debit, amount),
                new LedgerLine(AccountKey.Shop(shopId, LedgerAccountType.ShopAvailable), LedgerDirection.Credit, amount),
            ], null, CancellationToken.None);
            await db.SaveChangesAsync();
            await tx.CommitAsync(CancellationToken.None);
            return 0;
        });

    /// <summary>Ask for a finance OTP and read it from the simulated SMS inbox (no resend cooldown inside one test).</summary>
    private async Task<string> OtpAsync(TestUser user, string url)
    {
        var code = "";
        await WithParametersAsync(async () =>
        {
            var res = await user.Client.PostAsync(url, null);
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
            await DrainOutboxAsync();
            code = await factory.LatestOtpAsync(user.Phone);
        }, (ParameterKeys.AuthOtpResendSeconds, "0"));
        return code;
    }

    private async Task<Guid> AddShopBankAsync(Store store, string accountNo = "0123456789")
    {
        var code = await OtpAsync(store.Staff, $"/api/seller/shops/{store.Shop.ShopId}/finance/otp");
        var res = await store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/finance/bank-accounts",
            new { bankCode = "VCB", accountNo, accountName = "Nguyen Van Thu", otpCode = code, makeDefault = true });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return Guid.Parse((await res.ReadEnvelopeAsync()).Data.GetString()!);
    }

    // ---------- earnings, release, reports ----------

    [Fact]
    public async Task A_completed_order_waits_for_release_with_exact_earnings_then_is_released_once_and_the_report_ties_out()
    {
        var store = await StoreAsync();
        await SetFixedFeeAsync(store.LeafId, 500);
        var shopVoucher = await factory.CreateVoucherAsync(VoucherOwner.Shop, store.Shop.ShopId, VoucherType.Amount, value: 20_000);
        var platformVoucher = await factory.CreateVoucherAsync(VoucherOwner.Platform, null, VoucherType.Amount, value: 30_000);

        var b = await BuyAsync(store, 3, shopVoucher: shopVoucher.Code, platformVoucher: platformVoucher.Code);

        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == b.OrderId));
        order.Status.Should().Be(OrderStatus.Completed);
        var goods = 3 * Price;
        var expectedNet = goods - 20_000 - Bp(goods - 20_000, 500) - Bp(order.GrandTotal, await PaymentFeeBpAsync());
        var pending = AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopPending);
        var available = AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopAvailable);
        (await BalanceAsync(pending)).Should().Be(expectedNet, "the platform voucher is the platform's cost, never the shop's");
        (await BalanceAsync(available)).Should().Be(0);

        // The shop sees the same order and amount "chờ giải ngân"
        var list = (await (await store.Staff.Client.GetAsync($"/api/seller/shops/{store.Shop.ShopId}/finance/pending")).ReadEnvelopeAsync()).Data;
        var row = list.GetProperty("items").EnumerateArray().Single();
        row.Str("orderCode").Should().Be(b.Code);
        row.GetProperty("net").GetInt64().Should().Be(expectedNet);
        row.GetProperty("shopDiscount").GetInt64().Should().Be(20_000);

        // Not before the return window is over
        (await ReleaseAsync()).Orders.Should().Be(0);
        await WithParametersAsync(async () =>
        {
            var run = await ReleaseAsync();
            run.Orders.Should().BeGreaterThanOrEqualTo(1);
            (await ReleaseAsync()).Orders.Should().Be(0, "an order is released once");
        }, (ParameterKeys.ReturnWindowDays, "0"));

        (await BalanceAsync(pending)).Should().Be(0);
        (await BalanceAsync(available)).Should().Be(expectedNet);
        var check = await CheckLedgerAsync();
        check.Mismatches.Should().BeEmpty();
        check.UnbalancedTransactions.Should().Be(0);
        check.TotalDebits.Should().Be(check.TotalCredits);

        // Báo cáo đối soát: Excel and PDF total = the released amount, to the đồng
        var from = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var xlsx = await store.Staff.Client.GetAsync(
            $"/api/seller/shops/{store.Shop.ShopId}/finance/report?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&format=Xlsx");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync()))
        {
            var sheet = book.Worksheet(1);
            var codeRow = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == b.Code);
            codeRow.Cell(10).GetValue<long>().Should().Be(expectedNet);
            sheet.RowsUsed().Last().Cell(10).GetValue<long>().Should().Be(expectedNet);
        }
        var pdf = await store.Staff.Client.GetAsync(
            $"/api/seller/shops/{store.Shop.ShopId}/finance/report?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&format=Pdf");
        using (var doc = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync()))
        {
            var text = string.Join(" ", doc.GetPages().Select(p => p.Text));
            text.Should().Contain(b.Code).And.Contain("BÁO CÁO ĐỐI SOÁT");
            text.Should().Contain($"₫{expectedNet.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}");
        }
        var invoice = await store.Staff.Client.GetAsync(
            $"/api/seller/shops/{store.Shop.ShopId}/finance/fee-invoice?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        using (var doc = PdfDocument.Open(await invoice.Content.ReadAsByteArrayAsync()))
            string.Join(" ", doc.GetPages().Select(p => p.Text)).Should().Contain("HOÁ ĐƠN PHÍ DỊCH VỤ SÀN");
    }

    [Fact]
    public async Task An_open_return_holds_the_release_and_a_cod_refund_goes_to_the_buyers_wallet_and_out_of_the_shop_share()
    {
        var store = await StoreAsync();
        await SetFixedFeeAsync(store.LeafId, 400);
        var b = await BuyAsync(store, 3);
        var pending = AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopPending);

        // Return 1 of 3 (refund only) — the release must wait
        var evidence = await UploadAsync(b.Buyer.Client);
        var created = await b.Buyer.Client.PostAsJsonAsync($"/api/orders/{b.Code}/returns", new
        {
            type = "RefundOnly", reason = "Damaged", description = "Bình bị móp khi mở hộp, có ảnh",
            lines = new[] { new { orderItemId = b.ItemId, quantity = 1 } }, evidenceAssetIds = new[] { evidence },
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var returnId = Guid.Parse((await created.ReadEnvelopeAsync()).Data.Str("id"));
        await WithParametersAsync(async () => await ReleaseAsync(), (ParameterKeys.ReturnWindowDays, "0"));
        (await factory.WithDbAsync(db => db.SettlementItems.AnyAsync(i => i.OrderId == b.OrderId))).Should().BeFalse("its return is still open");

        (await store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/returns/{returnId}/actions", new { action = "Approve" }))
            .EnsureSuccessStatusCode();
        await DrainOutboxAsync();

        // The buyer's wallet got exactly the unit's paid share; the shop loses one unit's value and its fee on it
        (await BalanceAsync(AccountKey.Wallet(b.Buyer.Id))).Should().Be(Price);
        var refund = await factory.WithDbAsync(db => db.Refunds.AsNoTracking().SingleAsync(r => r.ReturnId == returnId));
        refund.Status.Should().Be(RefundStatus.Succeeded);
        refund.Destination.Should().Be(RefundDestination.Wallet);
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == b.OrderId));
        var expected = 2 * Price - Bp(2 * Price, 400) - Bp(order.GrandTotal - Price, await PaymentFeeBpAsync());
        (await BalanceAsync(pending)).Should().Be(expected);

        await WithParametersAsync(async () => await ReleaseAsync(), (ParameterKeys.ReturnWindowDays, "0"));
        (await factory.WithDbAsync(db => db.SettlementItems.AnyAsync(i => i.OrderId == b.OrderId))).Should().BeTrue();
        (await BalanceAsync(AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopAvailable))).Should().Be(expected);
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
    }

    private async Task<Guid> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        using var bmp = new SkiaSharp.SKBitmap(300, 200);
        using (var canvas = new SkiaSharp.SKCanvas(bmp)) canvas.Clear(new SkiaSharp.SKColor(30, 120, 200));
        using var encoded = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        var png = new ByteArrayContent(encoded.ToArray());
        png.Headers.ContentType = new("image/png");
        form.Add(png, "file", "evidence.png");
        var res = await client.PostAsync("/api/media/evidence", form);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return Guid.Parse((await res.ReadEnvelopeAsync()).Data.Str("id"));
    }

    // ---------- withdrawals ----------

    [Fact]
    public async Task Parallel_withdrawals_never_take_more_than_the_available_balance()
    {
        var store = await StoreAsync();
        var bank = await AddShopBankAsync(store);
        await FundAsync(store.Shop.ShopId, 200_000);

        await WithParametersAsync(async () =>
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/finance/withdrawals", new { bankAccountId = bank, amount = 60_000 }))));

            var bodies = await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()));
            results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(3, string.Join(" | ", bodies.Distinct()));
            results.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        }, (ParameterKeys.FinanceWithdrawPerWeek, "20"));

        (await BalanceAsync(AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopAvailable))).Should().Be(20_000);
        var done = await factory.WithDbAsync(db => db.Withdrawals.Where(w => w.OwnerId == store.Shop.ShopId).Select(w => w.Status).ToListAsync());
        done.Should().HaveCount(3).And.OnlyContain(s => s == WithdrawalStatus.Done, "small amounts are paid out at once");
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Withdrawals_need_a_verified_account_a_minimum_and_a_weekly_limit_and_a_refusal_gives_the_money_back()
    {
        var store = await StoreAsync();
        var available = AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopAvailable);
        await FundAsync(store.Shop.ShopId, 500_000);
        var url = $"/api/seller/shops/{store.Shop.ShopId}/finance/withdrawals";

        // An account that was never verified (e.g. typed in before the shop was approved) cannot receive money
        var unverified = await factory.WithDbAsync(async db =>
        {
            var a = new Domain.Shops.ShopBankAccount(store.Shop.ShopId, "ACB", "x", "9999", "CHUA XAC MINH", false);
            db.ShopBankAccounts.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        (await store.Staff.Client.PostAsJsonAsync(url, new { bankAccountId = unverified, amount = 100_000 })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var bank = await AddShopBankAsync(store);
        var small = await store.Staff.Client.PostAsJsonAsync(url, new { bankAccountId = bank, amount = 10_000 });
        small.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await small.Content.ReadAsStringAsync()).Should().Contain("tối thiểu");

        await WithParametersAsync(async () =>
        {
            // Above the auto-approval limit → waits for an admin, money already held
            var big = await store.Staff.Client.PostAsJsonAsync(url, new { bankAccountId = bank, amount = 300_000 });
            big.StatusCode.Should().Be(HttpStatusCode.OK);
            var bigData = (await big.ReadEnvelopeAsync()).Data;
            var id = Guid.Parse(bigData.Str("id"));
            bigData.Str("status").Should().Be("Pending");
            (await BalanceAsync(available)).Should().Be(200_000);

            (await store.Staff.Client.PostAsJsonAsync(url, new { bankAccountId = bank, amount = 50_000 })).StatusCode
                .Should().Be(HttpStatusCode.Conflict, "one withdrawal per 7 days in this test");

            var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceWithdrawalApprove);
            (await admin.PostAsJsonAsync($"/api/admin/finance/withdrawals/{id}/reject", new { reason = "Thông tin chủ tài khoản không khớp" }))
                .EnsureSuccessStatusCode();
            (await BalanceAsync(available)).Should().Be(500_000);
        }, (ParameterKeys.FinanceWithdrawAutoApproveMax, "0"), (ParameterKeys.FinanceWithdrawPerWeek, "1"));

        // The bank refuses the transfer (simulated: account ending 0000) → back to the balance by itself
        var refusing = await AddShopBankAsync(store, "1234560000");
        var refused = await store.Staff.Client.PostAsJsonAsync(url, new { bankAccountId = refusing, amount = 60_000 });
        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refused.ReadEnvelopeAsync()).Data.Str("status").Should().Be("Rejected");
        (await BalanceAsync(available)).Should().Be(500_000);
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
    }

    // ---------- Ví ShopHub ----------

    private async Task SetPinAsync(TestUser user, string pin)
    {
        var code = await OtpAsync(user, "/api/wallet/otp");
        var res = await user.Client.PostAsJsonAsync("/api/wallet/pin", new { otpCode = code, pin });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    private async Task TopupAsync(TestUser user, long amount)
    {
        var started = (await (await user.Client.PostAsJsonAsync("/api/wallet/topups", new { amount, method = "Simulated" })).ReadEnvelopeAsync()).Data;
        (await factory.CreateClient().PostAsync($"/api/payments/simulated/{started.Str("paymentId")}/success", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Wallet_is_topped_up_once_pays_orders_with_the_pin_and_takes_refunds_back()
    {
        var store = await StoreAsync();
        var buyer = await factory.CreateUserAsync();
        await SetPinAsync(buyer, "246813");
        await TopupAsync(buyer, 500_000);
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(500_000);

        // The gateway sends the same notification again: nothing changes
        var paymentId = await factory.WithDbAsync(db => db.Payments.Where(p => p.Purpose == PaymentPurpose.WalletTopup)
            .Join(db.WalletTopups.Where(t => t.UserId == buyer.Id), p => p.CheckoutId, t => t.Id, (p, _) => p.Id).SingleAsync());
        var gateway = factory.Services.CreateScope().ServiceProvider.GetRequiredService<SimulatedGateway>();
        var txn = await factory.WithDbAsync(db => db.Payments.Where(p => p.Id == paymentId).Select(p => p.ProviderTxnId).SingleAsync());
        var body = JsonSerializer.Serialize(new SimulatedGateway.CallbackBody(Guid.NewGuid().ToString("N"), paymentId, txn!, 500_000, "SUCCESS", null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/simulated") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        replay.Headers.Add(SimulatedGateway.SignatureHeader, gateway.Sign(body));
        (await factory.CreateClient().SendAsync(replay)).EnsureSuccessStatusCode();
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(500_000);

        // Wrong PIN: refused, nothing placed
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Shop.Skus[Product], quantity = 2 })).EnsureSuccessStatusCode();
        var request = CheckoutRequest(addressId, store.Shop.ShopId, "Wallet");
        var quote = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data;
        quote.GetProperty("canPlace").GetBoolean().Should().BeTrue(quote.GetProperty("problems").ToString());
        var total = quote.GetProperty("grandTotal").GetInt64();
        var wrong = await buyer.Client.SendAsync(PlaceMessage(request, total, "135790"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("còn 4 lần");

        var placed = await buyer.Client.SendAsync(PlaceMessage(request, total, "246813"));
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync());
        var code = (await placed.ReadEnvelopeAsync()).Data.GetProperty("orders")[0].Str("code");
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(500_000 - total);
        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Code == code));
        order.Status.Should().Be(OrderStatus.PendingConfirmation);
        order.PaymentStatus.Should().Be(OrderPaymentStatus.Paid);

        // Cancelled before the shop confirms → the money is back in the wallet
        (await buyer.Client.PostAsJsonAsync($"/api/orders/{code}/cancel", new { reason = "Đổi ý" })).EnsureSuccessStatusCode();
        await DrainOutboxAsync();
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(500_000);
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();

        // Withdraw to a bank account (added with an OTP), PIN required
        var otp = await OtpAsync(buyer, "/api/wallet/otp");
        var bank = Guid.Parse((await (await buyer.Client.PostAsJsonAsync("/api/wallet/bank-accounts",
            new { bankCode = "TCB", accountNo = "1903123456", accountName = "Nguyen Thi Mua", otpCode = otp })).ReadEnvelopeAsync()).Data.GetString()!);
        var w = await buyer.Client.PostAsJsonAsync("/api/wallet/withdrawals", new { bankAccountId = bank, amount = 200_000, pin = "246813" });
        w.StatusCode.Should().Be(HttpStatusCode.OK, await w.Content.ReadAsStringAsync());
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(300_000);
    }

    [Fact]
    public async Task Parallel_wallet_checkouts_cannot_spend_more_than_the_balance()
    {
        var store = await StoreAsync(stock: 100);
        var buyer = await factory.CreateUserAsync();
        await SetPinAsync(buyer, "975310");
        await TopupAsync(buyer, 150_000);
        var addressId = await factory.AddAddressAsync(buyer.Id, "01");
        (await buyer.Client.PostAsJsonAsync("/api/cart/items", new { skuId = store.Shop.Skus[Product], quantity = 1 })).EnsureSuccessStatusCode();
        var request = CheckoutRequest(addressId, store.Shop.ShopId, "Wallet");
        var total = (await (await buyer.Client.PostAsJsonAsync("/api/checkout/quote", request)).ReadEnvelopeAsync()).Data.GetProperty("grandTotal").GetInt64();
        total.Should().BeGreaterThan(75_000, "two of them must not fit in ₫150.000");

        // Five checkouts of the same cart line at once (different keys): the wallet pays for exactly one
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => buyer.Client.SendAsync(PlaceMessage(request, total, "975310")))));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await BalanceAsync(AccountKey.Wallet(buyer.Id))).Should().Be(150_000 - total);
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
    }

    // ---------- manual refund by the platform (VI.5) ----------

    private static object ManualRefund(Guid itemId, int quantity, long amount, bool platformBorne = false, string reason = "Khách khiếu nại qua tổng đài, đã xác minh") =>
        new { lines = new[] { new { orderItemId = itemId, quantity } }, amount, platformBorne, reason };

    [Fact]
    public async Task Manual_refunds_never_exceed_what_was_paid_even_in_parallel_and_the_shop_bears_them_before_release()
    {
        var store = await StoreAsync();
        var b = await BuyAsync(store, 2, complete: false);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.OrderIntervene);
        var url = $"/api/admin/orders/{b.Code}/manual-refund";
        var item = await factory.WithDbAsync(db => db.OrderItems.Include(i => i.Discounts).AsNoTracking().SingleAsync(i => i.Id == b.ItemId));
        var paid = item.LineTotal - item.Discounts.Sum(d => d.Amount);
        var perUnit = Application.Features.Returns.ReturnPricing.ForUnits(paid, 0, 2, 0, 1).Money;

        // More than the unit's paid share → refused with the ceiling
        var tooMuch = await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, perUnit + 1));
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tooMuch.ReadEnvelopeAsync()).Message.Should().Contain("tối đa");
        (await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, perUnit, reason: "ngắn"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, perUnit))).StatusCode.Should().Be(HttpStatusCode.OK);
        // One unit left: two admins refunding it at the same moment → exactly one goes through
        var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, paid - perUnit)))));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, 1))).StatusCode.Should().Be(HttpStatusCode.Conflict, "không còn sản phẩm nào để hoàn");

        var refunds = await factory.WithDbAsync(db => db.Refunds.Where(r => r.OrderId == b.OrderId).SumAsync(r => r.Amount));
        refunds.Should().Be(paid, "tổng hoàn bằng đúng phần đã trả, không hơn");
        await DrainOutboxAsync();
        // COD → the buyer's Ví ShopHub
        (await BalanceAsync(AccountKey.Wallet(b.Buyer.Id))).Should().Be(paid);
        (await b.Buyer.Client.PostAsync($"/api/orders/{b.Code}/received", null)).EnsureSuccessStatusCode();
        await DrainOutboxAsync();
        var breakdown = (await (await store.Staff.Client.GetAsync($"/api/seller/shops/{store.Shop.ShopId}/finance/pending")).ReadEnvelopeAsync()).Data
            .GetProperty("items").EnumerateArray().Single(r => r.Str("orderCode") == b.Code);
        breakdown.GetProperty("refundsBorne").GetInt64().Should().Be(item.LineTotal - item.Discounts.Where(d => d.Source is DiscountSource.Shop or DiscountSource.Combo).Sum(d => d.Amount),
            "shop chịu: toàn bộ giá trị hàng đã hoàn trừ vào doanh thu chờ giải ngân");
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();

        var noRight = await factory.ClientWithPermissionsAsync(Permissions.OrderView);
        (await noRight.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task After_release_a_manual_refund_is_only_possible_on_the_platform_and_leaves_the_shops_money_untouched()
    {
        var store = await StoreAsync();
        var b = await BuyAsync(store, 1);
        await WithParametersAsync(async () => (await ReleaseAsync()).Orders.Should().BeGreaterThanOrEqualTo(1), (ParameterKeys.ReturnWindowDays, "0"));
        var available = AccountKey.Shop(store.Shop.ShopId, LedgerAccountType.ShopAvailable);
        var before = await BalanceAsync(available);
        before.Should().BeGreaterThan(0);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.OrderIntervene);
        var url = $"/api/admin/orders/{b.Code}/manual-refund";

        var shopPays = await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, 10_000));
        shopPays.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await shopPays.ReadEnvelopeAsync()).Message.Should().Contain("sàn chịu");
        (await admin.PostAsJsonAsync(url, ManualRefund(b.ItemId, 1, 10_000, platformBorne: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainOutboxAsync();

        (await BalanceAsync(available)).Should().Be(before, "sàn chịu: tiền của shop không đổi");
        (await BalanceAsync(AccountKey.Wallet(b.Buyer.Id))).Should().Be(10_000);
        (await CheckLedgerAsync()).Mismatches.Should().BeEmpty();
        var history = await factory.WithDbAsync(db => db.ReturnRequests.Where(r => r.OrderId == b.OrderId).Select(r => new { r.Reason, r.PlatformBorne, r.Status }).SingleAsync());
        history.Reason.Should().Be(ReturnReason.ManualRefund);
        history.PlatformBorne.Should().BeTrue();
        history.Status.Should().Be(ReturnStatus.Refunded);
    }

    // ---------- reconciliation & access ----------

    [Fact]
    public async Task Reconciliation_matches_every_transaction_and_lists_each_difference()
    {
        var store = await StoreAsync();
        var paid = await BuyAsync(store, 1, method: "Simulated", deliver: false);
        var cod = await BuyAsync(store, 1, complete: false);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceReconcile);
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        var range = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var statement = await (await admin.GetAsync($"/api/admin/finance/statements/gateway?{range}")).Content.ReadAsStringAsync();
        var txn = await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == paid.OrderId)
            .Join(db.Payments, o => o.CheckoutId, p => p.CheckoutId, (_, p) => p.ProviderTxnId).SingleAsync());
        statement.Should().Contain(txn!);

        Task<JsonElement> ReconcileAsync(string provider, string csv, string? source = null) =>
            ReconcileFileAsync(admin, provider, source ?? "Simulated", from, to, csv);

        var clean = await ReconcileAsync("gateway", statement);
        clean.GetProperty("issues").EnumerateArray().Where(i => i.Str("reference") == txn).Should().BeEmpty();

        // Tamper: our transaction's amount changed, plus one the system never saw
        var lines = statement.TrimEnd('\n').Split('\n').ToList();
        var mine = lines.FindIndex(l => l.StartsWith(txn!));
        var parts = lines[mine].Split(',');
        parts[2] = (long.Parse(parts[2]) + 1_000).ToString();
        lines[mine] = string.Join(',', parts);
        lines.Add($"SIMFAKE123,{Guid.NewGuid()},77000,0,{DateTimeOffset.UtcNow:O}");
        var tampered = await ReconcileAsync("gateway", string.Join('\n', lines));
        var issues = tampered.GetProperty("issues").EnumerateArray().ToList();
        issues.Should().Contain(i => i.Str("reference") == txn && i.Str("issue") == "AmountMismatch");
        issues.Should().Contain(i => i.Str("reference") == "SIMFAKE123" && i.Str("issue") == "MissingInSystem");

        // Dropping our line from the file → "ShopHub has it, the file does not"
        var dropped = await ReconcileAsync("gateway", string.Join('\n', lines.Where((_, k) => k != mine)));
        dropped.GetProperty("issues").EnumerateArray().Should().Contain(i => i.Str("reference") == txn && i.Str("issue") == "MissingInStatement");

        // Carrier COD statement: the delivered COD parcel matches (amount and fee)
        var (tracking, code) = await factory.WithDbAsync(async db => await db.Shipments.Where(s => s.OrderId == cod.OrderId)
            .Select(s => new ValueTuple<string, string>(s.TrackingNo, s.CarrierCode)).SingleAsync());
        var carrier = await (await admin.GetAsync($"/api/admin/finance/statements/carrier?{range}&carrier={code}")).Content.ReadAsStringAsync();
        carrier.Should().Contain(tracking);
        (await ReconcileAsync("carrier", carrier, code)).GetProperty("issues").EnumerateArray().Where(i => i.Str("reference") == tracking).Should().BeEmpty();
    }

    private static async Task<JsonElement> ReconcileFileAsync(HttpClient admin, string provider, string source, DateTimeOffset from, DateTimeOffset to, string csv)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(from.ToString("O")), "from" },
            { new StringContent(to.ToString("O")), "to" },
            { new StringContent(source), "source" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "sao-ke.csv" },
        };
        var res = await admin.PostAsync($"/api/admin/finance/reconcile/{provider}", form);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    [Fact]
    public async Task Each_reconciliation_only_compares_the_chosen_carrier_or_gateway_and_reports_a_repeated_line_once()
    {
        var store = await StoreAsync(stock: 50);
        var first = await BuyAsync(store, 1, complete: false);
        var second = await BuyAsync(store, 1, complete: false);
        var simulatedPaid = await BuyAsync(store, 1, method: "Simulated", deliver: false);
        var vnpayPaid = await BuyAsync(store, 1, method: "Simulated", deliver: false);
        var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceReconcile);
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        // The second parcel went with another carrier
        var (firstTracking, carrierA) = await factory.WithDbAsync(async db => await db.Shipments.Where(s => s.OrderId == first.OrderId)
            .Select(s => new ValueTuple<string, string>(s.TrackingNo, s.CarrierCode)).SingleAsync());
        var carrierB = await factory.WithDbAsync(db => db.Carriers.Where(c => c.Code != carrierA).OrderBy(c => c.Code).Select(c => c.Code).FirstAsync());
        await factory.WithDbAsync(db => db.Shipments.Where(s => s.OrderId == second.OrderId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CarrierCode, carrierB)));
        var secondTracking = await factory.WithDbAsync(db => db.Shipments.Where(s => s.OrderId == second.OrderId).Select(s => s.TrackingNo).SingleAsync());
        var (cod, fee) = await factory.WithDbAsync(async db => await db.Shipments.Where(s => s.OrderId == first.OrderId)
            .Select(s => new ValueTuple<long, long>(s.CodAmount, s.Fee)).SingleAsync());
        var carrierFile = $"tracking_no,cod_amount,shipping_fee,delivered_at\n{firstTracking},{cod},{fee},x\n{firstTracking},{cod},{fee},x\n";
        var byCarrier = await ReconcileFileAsync(admin, "carrier", carrierA, from, to, carrierFile);
        var issues = byCarrier.GetProperty("issues").EnumerateArray().ToList();
        issues.Should().NotContain(i => i.Str("reference") == secondTracking, "vận đơn của hãng khác không thuộc tệp này");
        issues.Should().ContainSingle(i => i.Str("reference") == firstTracking && i.Str("issue") == "DuplicateInStatement");
        byCarrier.GetProperty("statementTotal").GetInt64().Should().Be(cod, "dòng lặp không được cộng hai lần");

        // One payment went through VNPay: its file is compared with VNPay payments only
        var payment = await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == vnpayPaid.OrderId)
            .Join(db.Payments, o => o.CheckoutId, x => x.CheckoutId, (_, x) => x).SingleAsync());
        await factory.WithDbAsync(db => db.Payments.Where(x => x.Id == payment.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Method, PaymentMethod.VnPay)));
        var simulatedTxn = await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == simulatedPaid.OrderId)
            .Join(db.Payments, o => o.CheckoutId, x => x.CheckoutId, (_, x) => x.ProviderTxnId).SingleAsync());
        var vnpayFile = $"STT,Mã GD VNPAY,Số tiền,Phí,Loại GD\n1,{payment.ProviderTxnId},{payment.Amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))},1.000,Thanh toán\n";
        var byGateway = await ReconcileFileAsync(admin, "gateway", "VnPay", from, to, vnpayFile);
        byGateway.GetProperty("issues").EnumerateArray().Should().NotContain(i => i.Str("reference") == simulatedTxn, "giao dịch của cổng khác không thuộc tệp VNPay");
        byGateway.GetProperty("matched").GetInt32().Should().Be(1);
        byGateway.GetProperty("statementFees").GetInt64().Should().Be(1_000);

        // No gateway / carrier chosen → refused, not "everything missing"
        using var none = new MultipartFormDataContent
        {
            { new StringContent(from.ToString("O")), "from" }, { new StringContent(to.ToString("O")), "to" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(carrierFile)), "file", "sao-ke.csv" },
        };
        (await admin.PostAsync("/api/admin/finance/reconcile/carrier", none)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Finance_of_another_shop_or_another_buyer_is_not_found_and_fees_cannot_be_backdated()
    {
        var mine = await StoreAsync();
        var other = await StoreAsync();
        (await other.Staff.Client.GetAsync($"/api/seller/shops/{mine.Shop.ShopId}/finance/summary")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{mine.Shop.ShopId}/finance/withdrawals",
            new { bankAccountId = Guid.NewGuid(), amount = 100_000 })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var a = await factory.CreateUserAsync();
        var b = await factory.CreateUserAsync();
        var topup = (await (await a.Client.PostAsJsonAsync("/api/wallet/topups", new { amount = 50_000, method = "Simulated" })).ReadEnvelopeAsync()).Data.Str("topupId");
        (await b.Client.GetAsync($"/api/wallet/topups/{topup}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var admin = await factory.ClientWithPermissionsAsync(Permissions.FinanceFeeManage);
        var backdated = await admin.PostAsJsonAsync("/api/admin/finance/fee-rules",
            new { categoryId = mine.LeafId, feeType = "Fixed", rateBp = 100, validFrom = DateTimeOffset.UtcNow.AddDays(-3), note = (string?)null });
        backdated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var plain = await factory.CreateUserAsync();
        (await plain.Client.GetAsync("/api/admin/finance/ledger")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
