using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.IntegrationTests.Infrastructure;
using StackExchange.Redis;

namespace ShopHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FoundationTests(ApiFactory factory)
{
    private const string ParamsUrl = "/api/admin/system-parameters";

    [Fact]
    public async Task Migration_creates_schemas_extensions_and_helpers()
    {
        var result = await factory.WithDbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT (SELECT string_agg(extname, ',' ORDER BY extname) FROM pg_extension),
                       public.immutable_unaccent('Điện Thoại Đẹp'),
                       to_regclass('sys.logs') IS NOT NULL,
                       to_regclass('iam.audit_logs') IS NOT NULL
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (Ext: reader.GetString(0), Unaccent: reader.GetString(1), Logs: reader.GetBoolean(2), Audit: reader.GetBoolean(3));
        });

        result.Ext.Split(',').Should().Contain(["btree_gist", "pg_trgm", "pgcrypto", "unaccent"]);
        // 'Đ' is not decomposed by NFD; the unaccent dictionary maps it to 'D'
        result.Unaccent.Should().Be("Dien Thoai Dep");
        result.Logs.Should().BeTrue();
        result.Audit.Should().BeTrue();
    }

    [Fact]
    public async Task Seeder_inserts_every_catalog_parameter_once()
    {
        var keys = await factory.WithDbAsync(db => db.SystemParameters.Select(p => p.Key).ToListAsync());

        keys.Should().BeEquivalentTo(ParameterCatalog.All.Select(d => d.Key));
    }

    [Fact]
    public async Task Site_info_is_public_and_comes_from_parameters()
    {
        var response = await factory.CreateClient().GetAsync("/api/site/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadEnvelopeAsync<SiteInfoDto>();
        body.Success.Should().BeTrue();
        body.Data!.PlatformName.Should().Be("ShopHub");
    }

    [Fact]
    public async Task Social_links_in_the_footer_are_the_https_ones_set_in_parameters_and_both_site_routes_agree()
    {
        var keys = new[] { ParameterKeys.SiteSocialFacebook, ParameterKeys.SiteSocialYoutube, ParameterKeys.SiteSocialTiktok, ParameterKeys.SiteZaloOaId };
        var old = await factory.WithDbAsync(db => db.SystemParameters.Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, p => p.Value));
        async Task Set(string key, string value)
        {
            await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).ExecuteUpdateAsync(u => u.SetProperty(p => p.Value, value)));
            factory.Services.GetRequiredService<ISystemParameters>().Invalidate(key);
        }
        try
        {
            await Set(ParameterKeys.SiteSocialFacebook, "https://www.facebook.com/shophub.vn");
            await Set(ParameterKeys.SiteSocialYoutube, "");
            // Not https → never rendered as a link
            await Set(ParameterKeys.SiteSocialTiktok, "javascript:alert(1)");
            await Set(ParameterKeys.SiteZaloOaId, "");
            (await (await factory.CreateClient().GetAsync("/api/site")).ReadEnvelopeAsync()).Data.GetProperty("zaloOaId").ValueKind
                .Should().Be(JsonValueKind.Null, "chưa có mã OA thì không có nút chia sẻ Zalo");
            await Set(ParameterKeys.SiteZaloOaId, "4318485735551577347");
            (await (await factory.CreateClient().GetAsync("/api/site")).ReadEnvelopeAsync()).Data.Str("zaloOaId").Should().Be("4318485735551577347");
            foreach (var url in new[] { "/api/site", "/api/site/info" })
            {
                var data = (await (await factory.CreateClient().GetAsync(url)).ReadEnvelopeAsync()).Data;
                data.GetProperty("social").EnumerateArray().Select(x => (x.Str("name"), x.Str("url")))
                    .Should().Equal([("Facebook", "https://www.facebook.com/shophub.vn")], url);
            }
        }
        finally
        {
            foreach (var (key, value) in old) await Set(key, value);
        }
    }

    [Fact]
    public async Task Admin_endpoint_requires_token_then_permission()
    {
        var anonymous = await factory.CreateClient().GetAsync(ParamsUrl);
        var noPermission = await (await factory.ClientWithPermissionsAsync("SOMETHING.ELSE")).GetAsync(ParamsUrl);
        var allowed = await (await factory.ClientWithPermissionsAsync(Permissions.SystemParameterView)).GetAsync(ParamsUrl);
        var superAdmin = await (await factory.ClientWithPermissionsAsync(Permissions.All)).GetAsync(ParamsUrl);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.ReadEnvelopeAsync()).Message.Should().Be("Bạn cần đăng nhập để tiếp tục.");
        noPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noPermission.ReadEnvelopeAsync()).Message.Should().Be("Bạn không có quyền thực hiện thao tác này.");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        superAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Expired_or_forged_tokens_are_rejected()
    {
        var expired = factory.CreateClient();
        expired.DefaultRequestHeaders.Authorization = new("Bearer",
            ApiFactory.IssueToken(Guid.NewGuid(), [Permissions.All], TimeSpan.FromMinutes(-5)));
        var forged = factory.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new("Bearer",
            ApiFactory.IssueToken(Guid.NewGuid(), [Permissions.All])[..^4] + "AAAA");

        (await expired.GetAsync(ParamsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await forged.GetAsync(ParamsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Updating_a_parameter_writes_audit_and_outbox_in_the_same_transaction()
    {
        var admin = await factory.CreateUserAsync(Permissions.SystemParameterUpdate);
        var userId = admin.Id;
        var client = admin.Client;

        var response = await client.PutAsJsonAsync($"{ParamsUrl}/{ParameterKeys.SiteHotline}", new { value = "1900 1234" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await factory.WithDbAsync(db => db.AuditLogs
            .Where(a => a.Entity == nameof(SystemParameter) && a.UserId == userId)
            .SingleAsync());
        audit.Action.Should().Be(AuditActions.Update);
        audit.OldValue.Should().Contain("1900 6000");
        audit.NewValue.Should().Contain("1900 1234");

        var outbox = await factory.WithDbAsync(db => db.OutboxMessages
            .Where(m => m.Type == OutboxTypes.SystemParameterChanged)
            .ToListAsync());
        outbox.Should().Contain(m => m.Payload.Contains(ParameterKeys.SiteHotline));

        // Public site info sees the new value right away (local cache invalidated)
        var info = await factory.CreateClient().GetAsync("/api/site/info");
        (await info.ReadEnvelopeAsync<SiteInfoDto>()).Data!.Hotline.Should().Be("1900 1234");
    }

    [Fact]
    public async Task Wrong_type_is_a_400_with_field_errors_in_vietnamese()
    {
        var client = await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate);

        var response = await client.PutAsJsonAsync($"{ParamsUrl}/{ParameterKeys.JobOutboxRetentionDays}", new { value = "mười" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadEnvelopeAsync();
        body.Errors.Should().ContainSingle(e => e.Field == "value" && e.Message == "Giá trị phải là số nguyên.");
    }

    [Fact]
    public async Task Unknown_parameter_is_404_and_stale_version_is_409()
    {
        var client = await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate);

        var missing = await client.PutAsJsonAsync($"{ParamsUrl}/KHONG.CO", new { value = "1" });
        var stale = await client.PutAsJsonAsync($"{ParamsUrl}/{ParameterKeys.SiteTaxCode}", new { value = "0101010101", version = 1u });

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.ReadEnvelopeAsync()).Message.Should().Contain("vừa được người khác sửa");
    }

    [Fact]
    public async Task Parallel_edits_with_the_same_version_let_exactly_one_win()
    {
        var key = ParameterKeys.JobOutboxBatchSize;
        var version = await factory.WithDbAsync(db => db.SystemParameters.Where(p => p.Key == key).Select(p => p.Version).SingleAsync());

        // Clients are prepared first, then the 20 requests fire truly in parallel
        var clients = new List<HttpClient>();
        for (var i = 0; i < 20; i++) clients.Add(await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate));
        var results = await Task.WhenAll(clients.Select((c, i) =>
            c.PutAsJsonAsync($"{ParamsUrl}/{key}", new { value = (200 + i).ToString(), version })));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(19);
    }

    [Fact]
    public async Task Unknown_route_is_a_json_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/khong-ton-tai");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadEnvelopeAsync()).Message.Should().Be("Không tìm thấy đường dẫn yêu cầu.");
    }

    [Theory]
    [InlineData("{\"value\":\"a\\u0000b\"}")]
    [InlineData("{\"value\":\"a\\U0000b\"}")]
    public async Task Null_character_in_json_body_is_rejected(string json)
    {
        var client = await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate);

        var response = await client.PutAsync($"{ParamsUrl}/{ParameterKeys.SiteHotline}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadEnvelopeAsync()).Message.Should().Contain("U+0000");
    }

    [Fact]
    public async Task Null_character_in_query_is_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/site/info?x=a%00b");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Malformed_json_never_leaks_framework_english()
    {
        var client = await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate);

        var response = await client.PutAsync($"{ParamsUrl}/{ParameterKeys.SiteHotline}",
            new StringContent("{\"value\": 12,", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadEnvelopeAsync();
        body.Message.Should().Be("Dữ liệu gửi lên chưa hợp lệ.");
        body.Errors.Should().OnlyContain(e => e.Message.Any(c => c > 127));
    }

    [Fact]
    public async Task Audit_log_search_rejects_inverted_range_and_pages_stably()
    {
        var client = await factory.ClientWithPermissionsAsync(Permissions.AuditLogView);

        var inverted = await client.GetAsync("/api/admin/audit-logs?from=2026-10-06T00:00:00Z&to=2026-10-01T00:00:00Z");
        var tooBig = await client.GetAsync("/api/admin/audit-logs?pageSize=1000");
        var page = await client.GetAsync("/api/admin/audit-logs?entity=SystemParameter&page=1&pageSize=5");

        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await inverted.ReadEnvelopeAsync()).Errors.Should().Contain(e => e.Message.Contains("Từ ngày"));
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await page.ReadEnvelopeAsync()).Data;
        data.GetProperty("pageSize").GetInt32().Should().Be(5);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Outbox_dispatch_publishes_to_redis_and_marks_processed()
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(factory.RedisEndpoint);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await redis.GetSubscriber().SubscribeAsync(RedisChannels.ParameterChanged, (_, v) => received.TrySetResult(v.ToString()));

        var client = await factory.ClientWithPermissionsAsync(Permissions.SystemParameterUpdate);
        (await client.PutAsJsonAsync($"{ParamsUrl}/{ParameterKeys.SiteSupportEmail}", new { value = "cskh@shophub.local" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await DispatchAsync();

        result.Processed.Should().BeGreaterThan(0);
        (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().NotBeNullOrEmpty();
        var pending = await factory.WithDbAsync(db => db.OutboxMessages
            .CountAsync(m => m.ProcessedAt == null && m.Type == OutboxTypes.SystemParameterChanged));
        pending.Should().Be(0);
        await redis.DisposeAsync();
    }

    [Fact]
    public async Task Outbox_message_without_handler_is_retried_then_parked()
    {
        var id = await factory.WithDbAsync(async db =>
        {
            var message = new OutboxMessage("khong.co.bo.xu.ly", "{}", DateTimeOffset.UtcNow);
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        });

        for (var i = 0; i < 12; i++) await DispatchAsync();

        var parked = await factory.WithDbAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id));
        parked.ProcessedAt.Should().BeNull();
        parked.Attempts.Should().Be(10, "dừng thử ở JOB.OUTBOX_MAX_ATTEMPTS");
        parked.LastError.Should().Contain("Không có bộ xử lý");
    }

    [Fact]
    public async Task Parallel_dispatchers_never_deliver_the_same_message_twice()
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(factory.RedisEndpoint);
        var delivered = new System.Collections.Concurrent.ConcurrentBag<string>();
        await redis.GetSubscriber().SubscribeAsync(RedisChannels.ParameterChanged, (_, v) =>
        {
            if (v.ToString().StartsWith("PARALLEL.", StringComparison.Ordinal)) delivered.Add(v.ToString());
        });

        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 30; i++)
                db.OutboxMessages.Add(new OutboxMessage(OutboxTypes.SystemParameterChanged,
                    JsonSerializer.Serialize(new { key = $"PARALLEL.{i}" }), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        // Five workers at once; FOR UPDATE SKIP LOCKED must split the rows between them
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(DispatchAsync)));
        await Task.Delay(500);

        delivered.Should().HaveCount(30).And.OnlyHaveUniqueItems();
        var pending = await factory.WithDbAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync());
        pending.Should().NotContain(m => m.Payload.Contains("PARALLEL."));
        await redis.DisposeAsync();
    }

    [Fact]
    public async Task Soft_delete_keeps_the_row_hides_it_and_journals_it()
    {
        var id = await factory.WithDbAsync(async db =>
        {
            var p = new SystemParameter("TEST.SOFT_DELETE", "1", ParameterDataType.Int, "TEST", "Thử", "Thử xoá mềm");
            db.SystemParameters.Add(p);
            await db.SaveChangesAsync();
            db.SystemParameters.Remove(p);
            await db.SaveChangesAsync();
            return p.Id;
        });

        await factory.WithDbAsync(async db =>
        {
            (await db.SystemParameters.AnyAsync(p => p.Id == id)).Should().BeFalse();
            var row = await db.SystemParameters.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
            row.DeletedAt.Should().NotBeNull();
            (await db.AuditLogs.AnyAsync(a => a.EntityId == id.ToString() && a.Action == AuditActions.Delete)).Should().BeTrue();
        });
    }

    [Fact]
    public async Task Readiness_tells_the_public_only_the_overall_status_and_each_dependency_only_inside_the_network()
    {
        var client = factory.CreateClient();
        var open = await client.GetAsync("/health/ready");
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        (await open.Content.ReadAsStringAsync()).Should().Be("Healthy", "ngoài mạng nội bộ chỉ thấy trạng thái tổng, không thấy tên máy / lỗi");

        // Through the gateway (forwarded) the detailed report does not exist
        var forwarded = new HttpRequestMessage(HttpMethod.Get, "/health/ready/details");
        forwarded.Headers.Add("X-Forwarded-For", "203.0.113.7");
        (await client.SendAsync(forwarded)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var response = await client.GetAsync("/health/ready/details");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("entries");

        json.GetProperty("postgres").GetProperty("status").GetString().Should().Be("Healthy");
        json.GetProperty("redis").GetProperty("status").GetString().Should().Be("Healthy");
        json.GetProperty("minio").GetProperty("status").GetString().Should().Be("Healthy");
        json.GetProperty("meilisearch").GetProperty("status").GetString().Should().Be("Healthy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<OutboxDispatchResult> DispatchAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(CancellationToken.None);
    }
}
