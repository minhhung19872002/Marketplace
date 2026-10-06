using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Security;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>A buyer reports a shop from the chat; the platform reads the conversation (logged) and decides once.</summary>
[Collection(ApiCollection.Name)]
public class ChatReportTests(ApiFactory factory)
{
    [Fact]
    public async Task A_reported_chat_is_reviewed_with_an_audit_row_and_two_admins_deciding_at_once_penalize_only_once()
    {
        var store = await factory.CreateStoreAsync();
        var buyer = await factory.CreateUserAsync();
        var conversationId = (await (await buyer.Client.PostAsJsonAsync("/api/chat/conversations", new { shopId = store.ShopId })).ReadEnvelopeAsync())
            .Data.Str("id");
        (await buyer.Client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new { type = "Text", text = "Hàng có chính hãng không?" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await buyer.Client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/report", new { reason = "Shop xin chuyển khoản ngoài sàn" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await buyer.Client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/report", new { reason = "Lại nhắn số tài khoản" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Only staff holding ENGAGE.CHAT.REVIEW read reported chats
        var outsider = await factory.ClientWithPermissionsAsync(Permissions.OrderView);
        (await outsider.GetAsync("/api/admin/chat-reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var reviewer = await factory.CreateUserAsync(Permissions.ChatReview, Permissions.ShopPenalty);
        var list = (await (await reviewer.Client.GetAsync("/api/admin/chat-reports?status=Open&pageSize=100")).ReadEnvelopeAsync()).Data.GetProperty("items");
        var rows = list.EnumerateArray().Where(r => r.Str("conversationId") == conversationId).ToList();
        rows.Should().HaveCount(2);
        rows[0].GetProperty("reportsOnConversation").GetInt32().Should().Be(2);
        var reportId = rows[0].Str("id");

        var detailRes = await reviewer.Client.GetAsync($"/api/admin/chat-reports/{reportId}");
        detailRes.StatusCode.Should().Be(HttpStatusCode.OK, await detailRes.Content.ReadAsStringAsync());
        var detail = (await detailRes.ReadEnvelopeAsync()).Data;
        detail.GetProperty("messages").EnumerateArray().Select(m => m.Str("body")).Should().Contain("Hàng có chính hãng không?");
        (await factory.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.UserId == reviewer.Id && a.Action == "VIEW" && a.EntityId == conversationId)))
            .Should().Be(1, "reading a private conversation is logged");

        // Two reviewers press "Phạt" at the same moment: one decision, one penalty row
        var second = await factory.CreateUserAsync(Permissions.ChatReview, Permissions.ShopPenalty);
        var decisions = await Task.WhenAll(new[] { reviewer.Client, second.Client }.Select(c => Task.Run(() =>
            c.PostAsJsonAsync($"/api/admin/chat-reports/{reportId}/resolve", new { resolution = "Gửi số tài khoản riêng cho người mua", penaltyPoints = 3 }))));
        decisions.Select(d => d.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await factory.WithDbAsync(db => db.ShopPenalties.CountAsync(p => p.ShopId == store.ShopId))).Should().Be(1);
        (await factory.WithDbAsync(db => db.Shops.Where(s => s.Id == store.ShopId).Select(s => s.PenaltyPoints).SingleAsync())).Should().Be(3);

        // The sibling report of the same conversation was settled by the same decision
        var after = (await (await reviewer.Client.GetAsync("/api/admin/chat-reports?pageSize=100")).ReadEnvelopeAsync()).Data.GetProperty("items")
            .EnumerateArray().Where(r => r.Str("conversationId") == conversationId).ToList();
        after.Should().OnlyContain(r => r.Str("status") == "Penalized");
    }
}
