using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopHub.Application.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.UnitTests.SourceScan;

// Source-scanning rules from spec section 8 — each one blocks a whole class of bugs
public class OrderStatusWriteTests
{
    [Fact]
    public void Order_status_is_only_assigned_inside_OrderStateMachine()
    {
        var pattern = new Regex(@"\b\w*[Oo]rder\w*\.Status\s*=[^=]");
        var offenders = RepoFiles.AllSourceFiles()
            .Where(f => !f.EndsWith("OrderStateMachine.cs", StringComparison.Ordinal))
            .Where(f => pattern.IsMatch(RepoFiles.WithoutComments(File.ReadAllText(f))))
            .Select(RepoFiles.Relative)
            .ToList();

        offenders.Should().BeEmpty("trạng thái đơn chỉ được đổi qua OrderStateMachine");
    }
}

public class MoneyTypeTests
{
    // decimal/double/float with a money-like name is forbidden in Domain/Application (use Money / long VND)
    private static readonly Regex MoneyFloat = new(
        @"\b(decimal|double|float)\??\s+\w*(Price|Amount|Total|Fee|Discount|Balance|Cost|Money|Subtotal|Refund|Payout)\w*\b",
        RegexOptions.IgnoreCase);

    [Fact]
    public void No_floating_point_money_in_domain_or_application()
    {
        var offenders = RepoFiles.SourceFiles("ShopHub.Domain", "ShopHub.Application")
            .SelectMany(f => RepoFiles.WithoutComments(File.ReadAllText(f)).Split('\n')
                .Select((line, i) => (f, line, i))
                .Where(x => MoneyFloat.IsMatch(x.line))
                .Select(x => $"{RepoFiles.Relative(x.f)}:{x.i + 1}: {x.line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty("tiền lưu số nguyên VND (Money/long), không decimal/double");
    }
}

public class StablePagingOrderTests
{
    // The ordering chain right before ToPagedResultAsync must end with a unique key (…ThenBy(x => x.Id))
    private static readonly Regex PagedCall = new(@"\.ToPagedResultAsync\s*\(", RegexOptions.Multiline);
    private static readonly Regex LastOrdering = new(
        @"\.(OrderBy|OrderByDescending|ThenBy|ThenByDescending)\s*\(\s*(\w+)\s*=>\s*\2\.(\w+)\s*\)\s*$",
        RegexOptions.Singleline);

    [Fact]
    public void Every_paged_query_ends_its_ordering_with_a_unique_key()
    {
        var problems = new List<string>();
        var files = RepoFiles.AllSourceFiles().Where(f => !f.EndsWith("Paging.cs", StringComparison.Ordinal));
        foreach (var file in files)
        {
            var src = RepoFiles.WithoutComments(File.ReadAllText(file));
            foreach (Match call in PagedCall.Matches(src))
            {
                var before = src[..call.Index].TrimEnd();
                var ordering = LastOrdering.Match(before);
                if (!ordering.Success || ordering.Groups[3].Value != "Id")
                    problems.Add($"{RepoFiles.Relative(file)}: chuỗi sắp xếp trước ToPagedResultAsync không kết thúc bằng khoá duy nhất (Id)");
            }
        }

        problems.Should().BeEmpty();
    }
}

public class SystemParameterReadersTests
{
    [Fact]
    public void Every_seeded_parameter_key_is_read_somewhere_in_code()
    {
        var keyFields = typeof(ParameterKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => f.Name).ToList();
        var seeded = ParameterCatalog.All.Select(d => d.Key).ToHashSet();

        // Code outside the key/catalog definition file
        var code = string.Join("\n", RepoFiles.AllSourceFiles()
            .Where(f => !f.EndsWith("ParameterKeys.cs", StringComparison.Ordinal))
            .Select(f => RepoFiles.WithoutComments(File.ReadAllText(f))));

        var dead = keyFields
            .Where(name => seeded.Contains((string)typeof(ParameterKeys).GetField(name)!.GetValue(null)!))
            .Where(name => !code.Contains($"ParameterKeys.{name}", StringComparison.Ordinal))
            .ToList();

        dead.Should().BeEmpty("tham số được gieo mà không ai đọc là công tắc chết");
    }

    [Fact]
    public void Catalog_covers_every_key_exactly_once()
    {
        var keys = typeof(ParameterKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetValue(null)!).ToList();

        ParameterCatalog.All.Select(d => d.Key).Should().BeEquivalentTo(keys).And.OnlyHaveUniqueItems();
    }
}

public class OutboxHandlerRegistrationTests
{
    [Fact]
    public void Every_outbox_type_has_a_handler()
    {
        var types = typeof(OutboxTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetValue(null)!).ToList();

        var handled = typeof(IOutboxHandler).Assembly.GetTypes()
            .Where(t => typeof(IOutboxHandler).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Select(t => (string)t.GetProperty(nameof(IOutboxHandler.Type))!.GetValue(
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t))!)
            .ToList();

        types.Should().BeSubsetOf(handled, "tin outbox không có bộ xử lý sẽ nằm chờ mãi");
    }
}

public class MigrationRegistrationTests
{
    [Fact]
    public void Every_migration_carries_the_Migration_attribute_and_DbContext()
    {
        var migrations = typeof(ShopHubDbContext).Assembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        migrations.Should().NotBeEmpty();
        foreach (var m in migrations)
        {
            m.GetCustomAttribute<MigrationAttribute>().Should().NotBeNull($"{m.Name} thiếu [Migration] — EF sẽ bỏ qua trong im lặng");
            m.GetCustomAttribute<DbContextAttribute>()?.ContextType.Should().Be(typeof(ShopHubDbContext));
        }
    }
}

public class NginxConfigParityTests
{
    private static readonly string[] Required =
    [
        "X-Content-Type-Options",
        "Strict-Transport-Security",
        "X-Frame-Options",
        "Content-Security-Policy",
        "Referrer-Policy",
        "limit_req_status 429",
        "resolver ",
        "default_type application/json",
        "error_page 429",
    ];

    public static IEnumerable<object[]> Configs() =>
        Directory.EnumerateFiles(Path.Combine(RepoFiles.RepoRoot, "deploy", "nginx"), "*.conf", SearchOption.AllDirectories)
            .Select(f => new object[] { RepoFiles.Relative(f) });

    [Theory]
    [MemberData(nameof(Configs))]
    public void Every_nginx_config_carries_the_shared_security_baseline(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, relativePath));

        Required.Where(r => !text.Contains(r, StringComparison.Ordinal)).Should().BeEmpty($"{relativePath} thiếu cấu hình chung");
    }

    /// <summary>The production HTTPS gateway serves the same routes as the dev gateway: everything from the first location on is identical.</summary>
    [Fact]
    public void The_https_gateway_routes_exactly_like_the_dev_gateway()
    {
        static string Routes(string file)
        {
            var text = File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "deploy", "nginx", file)).Replace("\r\n", "\n");
            var server = text.LastIndexOf("server {", StringComparison.Ordinal);
            return text[text.IndexOf("    proxy_set_header Host $host;", server, StringComparison.Ordinal)..];
        }

        Routes("gateway-https.conf").Should().Be(Routes("gateway.conf"), "sửa gateway.conf thì sửa cả gateway-https.conf");
    }
}

public class ReservedRouteTokenTests
{
    // MVC fills {action}/{controller}/{area}/{handler}/{page} itself: a route parameter with that name never binds (sổ lỗi L016, L029)
    private static readonly Regex Reserved = new(@"\[(Http\w+|Route)\(""[^""]*\{(action|controller|area|handler|page)(:[^}]*)?\}", RegexOptions.IgnoreCase);

    [Fact]
    public void No_route_template_uses_a_reserved_parameter_name()
    {
        var offenders = RepoFiles.SourceFiles("ShopHub.Api")
            .SelectMany(f => RepoFiles.WithoutComments(File.ReadAllText(f)).Split('\n')
                .Select((line, i) => (f, line, i))
                .Where(x => Reserved.IsMatch(x.line))
                .Select(x => $"{RepoFiles.Relative(x.f)}:{x.i + 1}: {x.line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty("tên tham số route dành riêng của MVC không bao giờ nhận giá trị");
    }
}

public class ValidatorNullTests
{
    // FluentValidation skips Matches / EmailAddress / Length on null: a rule that starts with one of them lets null reach the handler (L055)
    private static readonly Regex StartsWithNullTolerant = new(@"RuleFor\(\w+\s*=>\s*[\w.!]+\)\s*\.(Matches|EmailAddress|Length)\(");

    [Fact]
    public void No_validation_rule_starts_with_a_check_that_lets_null_through()
    {
        var offenders = RepoFiles.AllSourceFiles()
            .Where(f => StartsWithNullTolerant.IsMatch(RepoFiles.WithoutComments(File.ReadAllText(f))))
            .Select(RepoFiles.Relative)
            .ToList();

        offenders.Should().BeEmpty("Matches / EmailAddress / Length bỏ qua null — đặt NotEmpty (hoặc When) trước, kèm Cascade(CascadeMode.Stop)");
    }
}
