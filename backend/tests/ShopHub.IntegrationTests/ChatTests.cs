using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Features.Chat;
using ShopHub.Application.Security;
using ShopHub.Domain.Engage;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ChatTests(ApiFactory factory)
{
    private sealed record Store(TestStore Shop, TestUser Staff);

    private async Task<Store> StoreAsync(params string[] permissions)
    {
        var shop = await factory.CreateStoreAsync("79", products: [new("Ấm Chén Gốm", "Bình Giữ Nhiệt", 120_000, 30, "Việt Nam")]);
        var staff = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(shop.ShopId, staff.Id, Domain.Shops.ShopStaffRole.Manager,
                permissions.Length == 0 ? ShopPermissions.All : permissions));
            await db.SaveChangesAsync();
        });
        return new Store(shop, staff);
    }

    /// <summary>A real SignalR connection to the in-memory server (long polling), collecting every event it receives.</summary>
    private async Task<(HubConnection Hub, ConcurrentQueue<(string Event, JsonElement Data)> Events)> ConnectAsync(TestUser user)
    {
        var events = new ConcurrentQueue<(string, JsonElement)>();
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/realtime"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(user.AccessToken);
            })
            .Build();
        foreach (var name in new[] { "chat.message", "chat.read", "chat.typing", "notification" })
            hub.On<JsonElement>(name, data => events.Enqueue((name, data)));
        await hub.StartAsync();
        return (hub, events);
    }

    private static async Task<JsonElement> WaitForAsync(ConcurrentQueue<(string Event, JsonElement Data)> events, string name, Func<JsonElement, bool>? match = null)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = events.FirstOrDefault(e => e.Event == name && (match is null || match(e.Data)));
            if (hit.Event is not null) return hit.Data;
            await Task.Delay(100);
        }
        throw new TimeoutException($"No \"{name}\" event received");
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, string url, object body)
    {
        var res = await client.PostAsJsonAsync(url, body);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.ReadEnvelopeAsync()).Data;
    }

    [Fact]
    public async Task Buyer_and_shop_talk_live_with_read_receipts_typing_and_unread_counters()
    {
        var store = await StoreAsync();
        var buyer = await factory.CreateUserAsync();
        var (staffHub, staffEvents) = await ConnectAsync(store.Staff);
        var (buyerHub, buyerEvents) = await ConnectAsync(buyer);

        // "Chat ngay" pressed twice at once → one conversation
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            buyer.Client.PostAsJsonAsync("/api/chat/conversations", new { shopId = store.Shop.ShopId }))));
        var ids = new HashSet<string>();
        foreach (var r in starts) ids.Add((await r.ReadEnvelopeAsync()).Data.Str("id"));
        ids.Should().HaveCount(1);
        var conversationId = ids.Single();

        await SendAsync(buyer.Client, $"/api/chat/conversations/{conversationId}/messages", new { type = "Text", text = "Shop ơi ấm này còn màu xanh không?" });
        var live = await WaitForAsync(staffEvents, "chat.message");
        live.Str("body").Should().Be("Shop ơi ấm này còn màu xanh không?");
        live.Str("senderRole").Should().Be("Buyer");

        var inbox = (await (await store.Staff.Client.GetAsync($"/api/seller/shops/{store.Shop.ShopId}/chat/conversations?filter=Unread")).ReadEnvelopeAsync()).Data;
        inbox.GetProperty("items")[0].GetProperty("unread").GetInt32().Should().Be(1);

        await staffHub.InvokeAsync("Typing", Guid.Parse(conversationId));
        (await WaitForAsync(buyerEvents, "chat.typing")).Str("side").Should().Be("Shop");

        var url = $"/api/seller/shops/{store.Shop.ShopId}/chat/conversations/{conversationId}";
        (await store.Staff.Client.PostAsync($"{url}/read", null)).EnsureSuccessStatusCode();
        (await WaitForAsync(buyerEvents, "chat.read")).Str("side").Should().Be("Shop");
        await SendAsync(store.Staff.Client, $"{url}/messages", new { type = "Text", text = "Dạ còn ạ, bạn đặt luôn nhé!" });
        (await WaitForAsync(buyerEvents, "chat.message", e => e.Str("senderRole") == "Shop")).Str("body").Should().Contain("Dạ còn");

        var mine = (await (await buyer.Client.GetAsync("/api/chat/unread")).ReadEnvelopeAsync()).Data.GetInt32();
        mine.Should().Be(1);
        var messages = (await (await buyer.Client.GetAsync($"/api/chat/conversations/{conversationId}/messages")).ReadEnvelopeAsync()).Data;
        messages.GetArrayLength().Should().Be(2);
        messages[1].GetProperty("readAt").ValueKind.Should().NotBe(JsonValueKind.Null, "the shop read the buyer's message");

        await staffHub.DisposeAsync();
        await buyerHub.DisposeAsync();
    }

    [Fact]
    public async Task Unread_counters_are_counted_from_the_messages_when_both_sides_write_and_read_at_the_same_time()
    {
        var store = await StoreAsync();
        var buyer = await factory.CreateUserAsync();
        var c = await SendAsync(buyer.Client, "/api/chat/conversations", new { shopId = store.Shop.ShopId });
        var id = Guid.Parse(c.Str("id"));
        var buyerUrl = $"/api/chat/conversations/{id}/messages";
        var shopUrl = $"/api/seller/shops/{store.Shop.ShopId}/chat/conversations/{id}";

        // 15 buyer messages and 10 shop answers at once, while both sides keep pressing "read"
        var sends = Enumerable.Range(0, 15).Select(i => Task.Run(() => buyer.Client.PostAsJsonAsync(buyerUrl, new { type = "Text", text = $"Câu hỏi {i}" })))
            .Concat(Enumerable.Range(0, 10).Select(i => Task.Run(() => store.Staff.Client.PostAsJsonAsync($"{shopUrl}/messages", new { type = "Text", text = $"Trả lời {i}" }))))
            .Concat(Enumerable.Range(0, 5).Select(_ => Task.Run(() => buyer.Client.PostAsync($"/api/chat/conversations/{id}/read", null))))
            .ToList();
        var results = await Task.WhenAll(sends);
        results.Should().OnlyContain(r => r.IsSuccessStatusCode, "gửi song song không được lỗi xung đột");

        var (shopUnread, buyerUnread, counted) = await factory.WithDbAsync(async db =>
        {
            var conv = await db.Conversations.AsNoTracking().SingleAsync(x => x.Id == id);
            var fromBuyer = await db.ChatMessages.CountAsync(m => m.ConversationId == id && m.ReadAt == null && m.SenderRole == Domain.Engage.ChatRole.Buyer);
            var toBuyer = await db.ChatMessages.CountAsync(m => m.ConversationId == id && m.ReadAt == null && m.SenderRole != Domain.Engage.ChatRole.Buyer);
            return (conv.ShopUnread, conv.BuyerUnread, (fromBuyer, toBuyer));
        });
        shopUnread.Should().Be(15, "shop chưa đọc tin nào");
        shopUnread.Should().Be(counted.fromBuyer);
        buyerUnread.Should().Be(counted.toBuyer, "bộ đếm của người mua bằng đúng số tin chưa đọc của shop");

        (await store.Staff.Client.PostAsync($"{shopUrl}/read", null)).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.Conversations.AsNoTracking().Where(x => x.Id == id).Select(x => x.ShopUnread).SingleAsync())).Should().Be(0);
    }

    [Fact]
    public async Task Contact_details_are_flagged_not_blocked_and_cards_only_show_what_the_sender_may_share()
    {
        var store = await StoreAsync();
        var buyer = await factory.CreateUserAsync();
        var c = await SendAsync(buyer.Client, "/api/chat/conversations", new { shopId = store.Shop.ShopId });
        var url = $"/api/chat/conversations/{c.Str("id")}/messages";

        var phone = await SendAsync(buyer.Client, url, new { type = "Text", text = "Gọi mình số 0912 345 678 nhé" });
        phone.GetProperty("flagged").GetBoolean().Should().BeTrue();
        (await SendAsync(buyer.Client, url, new { type = "Text", text = "Cảm ơn shop" })).GetProperty("flagged").GetBoolean().Should().BeFalse();

        var product = await SendAsync(buyer.Client, url, new { type = "Product", productId = store.Shop.Products["Ấm Chén Gốm"] });
        product.GetProperty("payload").Str("name").Should().StartWith("Ấm Chén Gốm");

        // An order of another buyer cannot be shown as a card
        var other = await factory.CreateUserAsync();
        var otherOrderCode = await factory.WithDbAsync(db => db.Orders.Where(o => o.BuyerId != buyer.Id).Select(o => o.Code).FirstOrDefaultAsync());
        if (otherOrderCode is not null)
            (await buyer.Client.PostAsJsonAsync(url, new { type = "Order", orderCode = otherOrderCode })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Somebody else's conversation does not exist for them
        (await other.Client.GetAsync($"/api/chat/conversations/{c.Str("id")}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherShop = await StoreAsync();
        (await otherShop.Staff.Client.GetAsync($"/api/seller/shops/{store.Shop.ShopId}/chat/conversations")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var noChat = await StoreAsync(ShopPermissions.OrderView);
        (await noChat.Staff.Client.GetAsync($"/api/seller/shops/{noChat.Shop.ShopId}/chat/conversations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Outside_working_hours_the_buyer_gets_the_automatic_reply_once_an_hour_and_a_blocked_shop_cannot_write()
    {
        var store = await StoreAsync();
        // Opening hours that never include the current Vietnam time: a one-minute window an hour ago
        var vn = DateTime.UtcNow.AddHours(7).AddHours(-1);
        var from = new TimeOnly(vn.Hour, 0);
        (await store.Staff.Client.PutAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/chat/settings", new
        {
            autoReplyEnabled = true, autoReplyText = "Shop ngoài giờ, sẽ trả lời sau 8h sáng.", openFrom = from.ToString("HH:mm:ss"), openTo = from.AddMinutes(1).ToString("HH:mm:ss"),
        })).EnsureSuccessStatusCode();
        var buyer = await factory.CreateUserAsync();
        var c = await SendAsync(buyer.Client, "/api/chat/conversations", new { shopId = store.Shop.ShopId });
        var url = $"/api/chat/conversations/{c.Str("id")}/messages";
        await SendAsync(buyer.Client, url, new { type = "Text", text = "Còn hàng không shop?" });
        await SendAsync(buyer.Client, url, new { type = "Text", text = "Shop ơi?" });

        var messages = (await (await buyer.Client.GetAsync(url)).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        messages.Count(m => m.Str("senderRole") == "System").Should().Be(1);
        messages.Single(m => m.Str("senderRole") == "System").Str("body").Should().Contain("ngoài giờ");

        (await buyer.Client.PostAsJsonAsync($"/api/chat/conversations/{c.Str("id")}/block", new { blocked = true })).EnsureSuccessStatusCode();
        (await store.Staff.Client.PostAsJsonAsync($"/api/seller/shops/{store.Shop.ShopId}/chat/conversations/{c.Str("id")}/messages",
            new { type = "Text", text = "Xin chào" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_new_message_notifies_the_shop_live_and_by_email_when_chosen_and_messages_are_rate_limited()
    {
        var store = await StoreAsync();
        await factory.WithDbAsync(db => db.Users.Where(u => u.Id == store.Staff.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Email, $"nv{store.Staff.Phone}@shophub.local")));
        // Activity notifications by email are off by default: the staff member turns them on
        (await store.Staff.Client.PutAsJsonAsync("/api/notifications/prefs", new
        {
            prefs = new[] { new { category = "Activity", channel = "Email", enabled = true } },
        })).EnsureSuccessStatusCode();
        var (hub, events) = await ConnectAsync(store.Staff);
        var buyer = await factory.CreateUserAsync();
        var c = await SendAsync(buyer.Client, "/api/chat/conversations", new { shopId = store.Shop.ShopId });
        await SendAsync(buyer.Client, $"/api/chat/conversations/{c.Str("id")}/messages", new { type = "Text", text = "Hỏi về đơn hàng" });

        for (var i = 0; i < 10 && (await factory.DispatchOutboxAsync()).Processed > 0; i++) { }
        var pushed = await WaitForAsync(events, "notification", e => e.Str("title").StartsWith("Tin nhắn mới"));
        pushed.GetProperty("unread").GetInt32().Should().BeGreaterThan(0);
        factory.Emails.Sent.Should().Contain(m => m.To == $"nv{store.Staff.Phone}@shophub.local" && m.Subject.StartsWith("Tin nhắn mới"));

        // A second message in the same hour adds no second notification
        await SendAsync(buyer.Client, $"/api/chat/conversations/{c.Str("id")}/messages", new { type = "Text", text = "Shop ơi" });
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == store.Staff.Id && n.RefId == Guid.Parse(c.Str("id"))))).Should().Be(1);

        var flood = await Task.WhenAll(Enumerable.Range(0, 70).Select(i =>
            buyer.Client.PostAsJsonAsync($"/api/chat/conversations/{c.Str("id")}/messages", new { type = "Text", text = $"tin {i}" })));
        flood.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task A_broadcast_reaches_its_segment_once_and_nobody_gets_two_promotions_the_same_day()
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        var fresh = await factory.CreateUserAsync();
        var first = (await (await admin.PostAsJsonAsync("/api/admin/marketing/broadcasts",
            new { title = "Siêu sale cuối tuần", body = "Giảm đến 50%", link = "/su-kien/sieu-sale-10-10", segment = "NoOrderYet" })).ReadEnvelopeAsync()).Data;
        first.GetProperty("recipients").GetInt32().Should().BeGreaterThan(0);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == fresh.Id && n.Category == NotificationCategory.Promotion))).Should().Be(1);

        var second = (await (await admin.PostAsJsonAsync("/api/admin/marketing/broadcasts",
            new { title = "Thêm một ưu đãi", body = "Freeship", link = (string?)null, segment = "NoOrderYet" })).ReadEnvelopeAsync()).Data;
        second.GetProperty("skippedToday").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == fresh.Id && n.Category == NotificationCategory.Promotion))).Should().Be(1);

        (await admin.PostAsJsonAsync("/api/admin/marketing/broadcasts", new { title = "X", body = "Y", link = "javascript:alert(1)", segment = "Everyone" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Two_broadcasts_sent_at_the_same_moment_reach_a_person_once_and_never_whoever_turned_promotions_off()
    {
        var admin = await factory.ClientWithPermissionsAsync(Permissions.MarketingManage);
        var fresh = await factory.CreateUserAsync();
        var quiet = await factory.CreateUserAsync();
        (await quiet.Client.PutAsJsonAsync("/api/notifications/prefs",
            new { prefs = new[] { new { category = "Promotion", channel = "InApp", enabled = false } } })).EnsureSuccessStatusCode();

        var sends = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => admin.PostAsJsonAsync("/api/admin/marketing/broadcasts",
            new { title = $"Ưu đãi {i}", body = "Giảm giá", link = (string?)null, segment = "NoOrderYet" }))));
        sends.Should().OnlyContain(r => r.IsSuccessStatusCode);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == fresh.Id && n.Category == NotificationCategory.Promotion)))
            .Should().Be(1, "bốn đợt gửi cùng lúc vẫn chỉ một tin khuyến mãi mỗi ngày");
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == quiet.Id && n.Category == NotificationCategory.Promotion)))
            .Should().Be(0, "người đã tắt khuyến mãi trong ứng dụng không nhận");
    }

    [Fact]
    public async Task Reminders_tell_about_orders_completing_soon_once()
    {
        var buyer = await factory.CreateUserAsync();
        var order = await factory.WithDbAsync(db => db.Orders.Where(o => o.Status == Domain.Sales.OrderStatus.Delivered).Select(o => new { o.Id, o.BuyerId }).FirstOrDefaultAsync());
        if (order is null) return; // no delivered order in this run (other tests complete theirs)
        await factory.WithDbAsync(db => db.Orders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.AutoCompleteAt, DateTimeOffset.UtcNow.AddHours(5))));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(CancellationToken.None);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(CancellationToken.None);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == order.BuyerId && n.DedupeKey == $"auto-complete:{order.Id}"))).Should().Be(1);
        _ = buyer;
    }

    [Fact]
    public async Task The_chat_notification_says_what_the_admin_wrote_in_its_message_template()
    {
        const string key = Application.Features.Admin.TemplateCatalog.ChatToShop;
        var channel = Domain.SystemConfig.TemplateChannel.InApp;
        var original = await factory.WithDbAsync(db => db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel));
        await factory.WithDbAsync(async db =>
        {
            if (original is null)
                db.MessageTemplates.Add(new Domain.SystemConfig.MessageTemplate(key, channel, "Chat", "Khách {{sender}} vừa nhắn", "Nội dung: {{message}}",
                    "sender,message", DateTimeOffset.UtcNow));
            else
                await db.MessageTemplates.Where(t => t.Id == original.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.Subject, "Khách {{sender}} vừa nhắn").SetProperty(t => t.Body, "Nội dung: {{message}}"));
            await db.SaveChangesAsync();
            return 0;
        });
        try
        {
            var store = await StoreAsync();
            var buyer = await factory.CreateUserAsync();
            var c = await SendAsync(buyer.Client, "/api/chat/conversations", new { shopId = store.Shop.ShopId });
            await SendAsync(buyer.Client, $"/api/chat/conversations/{c.Str("id")}/messages", new { type = "Text", text = "Còn hàng không shop?" });
            var notice = await factory.WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == store.Staff.Id && n.RefType == "conversation"));
            var name = await factory.WithDbAsync(db => db.Users.Where(u => u.Id == buyer.Id).Select(u => u.FullName).SingleAsync());
            notice.Title.Should().Be($"Khách {name} vừa nhắn");
            notice.Body.Should().Be("Nội dung: Còn hàng không shop?");
        }
        finally
        {
            await factory.WithDbAsync(async db =>
            {
                if (original is null) await db.MessageTemplates.Where(t => t.Key == key && t.Channel == channel).ExecuteDeleteAsync();
                else
                    await db.MessageTemplates.Where(t => t.Id == original.Id)
                        .ExecuteUpdateAsync(u => u.SetProperty(t => t.Subject, original.Subject).SetProperty(t => t.Body, original.Body));
                return 0;
            });
        }
    }
}
