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

    /// <summary>
    /// The name-based scan above misses a variable not called "order" (L083); the compiler-level rule: Order.Status has
    /// a private setter, and the one internal door to it (TransitionTo) is used only by OrderStateMachine.
    /// </summary>
    [Fact]
    public void Order_status_has_no_public_or_internal_setter_and_only_the_state_machine_moves_it()
    {
        var status = typeof(ShopHub.Domain.Sales.Order).GetProperty(nameof(ShopHub.Domain.Sales.Order.Status))!;
        status.SetMethod.Should().NotBeNull();
        status.SetMethod!.IsPrivate.Should().BeTrue("không setter public / internal");
        var callers = RepoFiles.AllSourceFiles()
            .Where(f => !f.EndsWith("OrderStateMachine.cs", StringComparison.Ordinal) && !f.EndsWith($"{Path.DirectorySeparatorChar}Order.cs", StringComparison.Ordinal))
            .Where(f => RepoFiles.WithoutComments(File.ReadAllText(f)).Contains(".TransitionTo(", StringComparison.Ordinal))
            .Select(RepoFiles.Relative).ToList();
        callers.Should().BeEmpty("chỉ OrderStateMachine gọi TransitionTo");
    }
}

public class MoneyTypeTests
{
    // C5 / L083: every price is computed in PricingEngine; arithmetic on a unit price or a shipping fee anywhere else
    // must be one of these, with its reason (an order's settlement, a parcel's declared value…)
    private static readonly Dictionary<string, string> PriceArithmeticAllowed = new()
    {
        ["PricingEngine.cs"] = "the price itself",
        ["SettlementCalculator.cs"] = "what a completed order is worth to the shop, from the prices PricingEngine fixed",
        ["LoyaltyFeatures.cs"] = "member spending: what was paid for goods (grand total less the shipping paid)",
        ["Deals.cs"] = "free-gift threshold on the already-priced lines (feeds PricingEngine)",
        ["ReturnFeatures.cs"] = "declared value of a return parcel for the carrier",
        ["SellerOrderFeatures.cs"] = "COD amount split between the parcels of one order",
    };

    private static readonly Regex PriceArithmetic = new(@"\b(UnitPrice|ShippingFee)\s*[-+*]\s*[\w(]|[\w)]\s*[-+*]\s*\(?\s*[\w.]*\b(UnitPrice|ShippingFee)\b");

    [Fact]
    public void Unit_prices_and_shipping_fees_are_only_computed_in_the_allowed_places()
    {
        var offenders = RepoFiles.SourceFiles("ShopHub.Domain", "ShopHub.Application")
            .Where(f => !PriceArithmeticAllowed.ContainsKey(Path.GetFileName(f)))
            .SelectMany(f => RepoFiles.WithoutComments(File.ReadAllText(f)).Split('\n').Select((line, i) => (f, line, i)))
            .Where(x => PriceArithmetic.IsMatch(x.line))
            .Select(x => $"{RepoFiles.Relative(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("mọi tính giá đi qua PricingEngine (mục 8: MoneyTypeTests)");
    }

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
    private const string Include = "include /etc/nginx/snippets/security-headers.inc;";

    private static readonly string[] Headers =
        ["X-Content-Type-Options", "Strict-Transport-Security", "X-Frame-Options", "Content-Security-Policy", "Referrer-Policy"];

    private static readonly string[] Required =
    [
        Include,
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

    [Fact]
    public void The_shared_security_header_file_carries_every_header()
    {
        var text = File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "deploy", "nginx", "security-headers.inc"));
        Headers.Where(h => !text.Contains($"add_header {h} ", StringComparison.Ordinal)).Should().BeEmpty();
    }

    /// <summary>
    /// nginx does not inherit add_header into a block that has its own (L076): every location (and the server) that
    /// sets any add_header must include the shared file, and no config writes a security header by hand.
    /// </summary>
    [Theory]
    [MemberData(nameof(Configs))]
    public void Every_block_with_its_own_add_header_includes_the_security_headers(string relativePath)
    {
        var text = string.Join('\n', File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, relativePath)).Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Split('#')[0]));
        var offenders = new List<string>();
        foreach (var (header, body) in Blocks(text).Where(b => b.Header.StartsWith("location", StringComparison.Ordinal) || b.Header.StartsWith("server", StringComparison.Ordinal)))
        {
            var own = Direct(body);
            if (own.Contains("add_header", StringComparison.Ordinal) && !own.Contains(Include, StringComparison.Ordinal)) offenders.Add(header);
            if (Headers.Any(h => own.Contains($"add_header {h}", StringComparison.Ordinal))) offenders.Add($"{header}: viết tay tiêu đề bảo mật");
        }
        offenders.Should().BeEmpty($"{relativePath}: khối có add_header riêng phải include security-headers.inc");
    }

    /// <summary>(header, body) of every { } block, nested ones included.</summary>
    private static IEnumerable<(string Header, string Body)> Blocks(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            var start = text.LastIndexOfAny([';', '}', '{'], Math.Max(0, i - 1)) + 1;
            var header = text[start..i].Trim();
            var depth = 0;
            var j = i;
            for (; j < text.Length; j++)
            {
                if (text[j] == '{') depth++;
                else if (text[j] == '}' && --depth == 0) break;
            }
            yield return (header, text[(i + 1)..j]);
        }
    }

    /// <summary>A block's own directives (nested blocks cut out).</summary>
    private static string Direct(string body)
    {
        var sb = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in body)
        {
            if (c == '{') depth++;
            if (depth == 0) sb.Append(c);
            if (c == '}') depth--;
        }
        return sb.ToString();
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

public class WorkingDayTests
{
    // A2 / L064: every working-day sum reads the configured holidays and weekly days off — never an empty set
    private static readonly Regex EmptyDateSet = new(@"HashSet<(System\.)?(DateOnly|DayOfWeek)>\s*(\(\s*\)|\{\s*\})|Array\.Empty<(System\.)?DateOnly>|ImmutableHashSet<(System\.)?DateOnly>\.Empty");
    private static readonly Regex PureRule = new(@"WorkingCalendar\.Add\s*\(|VietnamTime\.AddWorkingDays\s*\(");

    [Fact]
    public void Working_days_are_only_added_through_the_working_calendar()
    {
        var offenders = RepoFiles.AllSourceFiles()
            .Where(f => !f.EndsWith("WorkingCalendar.cs", StringComparison.Ordinal))
            .Where(f => RepoFiles.WithoutComments(File.ReadAllText(f)) is var code && (EmptyDateSet.IsMatch(code) || PureRule.IsMatch(code)))
            .Select(RepoFiles.Relative)
            .ToList();

        offenders.Should().BeEmpty("ngày làm việc chỉ được cộng qua IWorkingCalendar (đọc LOGISTICS.HOLIDAYS và LOGISTICS.WEEKLY_OFF_DAYS)");
    }
}

public class InventoryWriteTests
{
    // A6 / L068: stock and reserved change only through InventoryWriter (one conditional UPDATE + its movement row, in a transaction)
    private static readonly Regex RawStockWrite = new(
        @"skus\s+SET[^;""]*\b(stock|reserved)\s*=|SetProperty\(\s*\w+\s*=>\s*\w+\.(Stock|Reserved)\b", RegexOptions.IgnoreCase);

    [Fact]
    public void Stock_and_reserved_are_only_changed_through_the_inventory_writer()
    {
        var offenders = RepoFiles.AllSourceFiles()
            .Where(f => !f.EndsWith("InventoryWriter.cs", StringComparison.Ordinal) && !f.Contains("Migrations", StringComparison.Ordinal))
            .Where(f => RawStockWrite.IsMatch(RepoFiles.WithoutComments(File.ReadAllText(f))))
            .Select(RepoFiles.Relative)
            .ToList();

        offenders.Should().BeEmpty("tồn kho chỉ đổi qua InventoryWriter: UPDATE có điều kiện + dòng inventory_movements trong cùng giao dịch");
    }
}

public class SeedDateTests
{
    // C4 / L082: sample data is dated from the day it is loaded — a date written in a seeder ages and breaks (fees "not yet valid", orders in the future)
    private static readonly Regex FixedDate = new(@"new\s+(System\.)?(DateTime|DateOnly|DateTimeOffset)\s*\(\s*20\d{2}|""20\d{2}-\d{2}-\d{2}|DateTime(Offset)?\.Parse\(\s*""20\d{2}");

    [Fact]
    public void Seeders_never_write_a_fixed_calendar_date()
    {
        var offenders = RepoFiles.SourceFiles("ShopHub.Infrastructure")
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Seed{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => RepoFiles.WithoutComments(File.ReadAllText(f)).Split('\n').Select((line, i) => (f, line, i)))
            .Where(x => FixedDate.IsMatch(x.line))
            .Select(x => $"{RepoFiles.Relative(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("mọi mốc thời gian của dữ liệu gieo tính từ ngày nạp (đặc tả mục 7)");
    }
}
