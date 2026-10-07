using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Security;
using ShopHub.Domain.Iam;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 14 B5: the Hangfire dashboard opens in a browser through a one-use ticket and an httpOnly cookie.</summary>
[Collection(ApiCollection.Name)]
public class JobDashboardTests(ApiFactory factory)
{
    private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private static string CookieOf(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sh_jobs=", StringComparison.Ordinal));

    [Fact]
    public async Task A_ticket_opens_the_dashboard_once_as_a_path_scoped_httponly_cookie_and_only_for_who_may_see_it()
    {
        var admin = await factory.CreateUserAsync(Permissions.JobDashboardView);
        var ticket = (await (await admin.Client.PostAsync("/api/admin/jobs/ticket", null)).ReadEnvelopeAsync()).Data.Str("url");
        ticket.Should().StartWith("/api/admin/jobs?ticket=");

        var browser = Browser();
        (await browser.GetAsync("/api/admin/jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "trình duyệt không có token thì không vào được");
        var open = await browser.GetAsync(ticket);
        open.StatusCode.Should().Be(HttpStatusCode.Redirect);
        open.Headers.Location!.ToString().Should().Be("/api/admin/jobs", "mã không nằm lại trên thanh địa chỉ");
        var cookie = CookieOf(open);
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("path=/api/admin/jobs").And.Contain("samesite=strict");

        var page = new HttpRequestMessage(HttpMethod.Get, "/api/admin/jobs");
        page.Headers.Add("Cookie", cookie.Split(';')[0]);
        var dashboard = await browser.SendAsync(page);
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("ShopHub — Việc nền");

        (await browser.GetAsync(ticket)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "mã chỉ dùng được một lần");

        // Locked since: the cookie no longer opens the dashboard
        await factory.WithDbAsync(db => db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Locked)));
        var again = new HttpRequestMessage(HttpMethod.Get, "/api/admin/jobs");
        again.Headers.Add("Cookie", cookie.Split(';')[0]);
        (await browser.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var plain = await factory.CreateUserAsync();
        (await plain.Client.PostAsync("/api/admin/jobs/ticket", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Schedules_are_read_in_vietnam_time_and_the_next_run_comes_from_hangfire()
    {
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Application.Abstractions.IJobScheduler>().RegisterRecurringJobsAsync(CancellationToken.None);
        var viewer = await factory.CreateUserAsync(Permissions.JobDashboardView);
        var jobs = (await (await viewer.Client.GetAsync("/api/admin/job-runs")).ReadEnvelopeAsync()).Data.EnumerateArray().ToList();
        var ledger = jobs.Single(j => j.Str("id") == "finance.ledger-check");
        ledger.Str("cron").Should().Be("30 2 * * *", "02:30 sáng giờ Việt Nam");
        ledger.Str("parameterKey").Should().Be(Application.SystemConfig.ParameterKeys.JobLedgerCheckCron);
        ledger.Str("timeZone").Should().Be("Asia/Ho_Chi_Minh");
        var next = DateTime.Parse(ledger.Str("nextExecution"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        (next.Hour, next.Minute).Should().Be((19, 30), "02:30 giờ Việt Nam là 19:30 UTC hôm trước");
    }

    [Fact]
    public async Task The_job_list_needs_the_view_permission()
    {
        var viewer = await factory.CreateUserAsync(Permissions.JobDashboardView);
        var list = await viewer.Client.GetAsync("/api/admin/job-runs");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        (await (await factory.CreateUserAsync()).Client.GetAsync("/api/admin/job-runs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
