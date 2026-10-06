using System.Globalization;
using ShopHub.Application.Common;
using ShopHub.Domain.Common;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Reports;

public enum Granularity
{
    Day,
    Week,
    Month,
}

/// <summary>
/// A report period in Vietnam days [From, To] (both included) and its buckets. The one place where a reversed or
/// too long range is refused (spec 8, trap 6): every report and export goes through <see cref="Of"/>.
/// </summary>
public sealed record ReportRange(DateOnly From, DateOnly To, Granularity Granularity)
{
    public const int MaxDays = 366;
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    public static ReportRange Of(DateOnly? from, DateOnly? to, Granularity granularity, DateTimeOffset now)
    {
        var end = to ?? VietnamTime.Today(now);
        var start = from ?? end.AddDays(-29);
        if (start > end) throw new BusinessRuleException("Khoảng ngày không hợp lệ: ngày bắt đầu sau ngày kết thúc.");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) throw new BusinessRuleException($"Mỗi báo cáo tối đa {MaxDays} ngày.");
        return new ReportRange(start, end, granularity);
    }

    public int Days => To.DayNumber - From.DayNumber + 1;

    // UTC bounds [Start, End)
    public DateTimeOffset Start => new DateTimeOffset(From.ToDateTime(TimeOnly.MinValue), Vn).ToUniversalTime();
    public DateTimeOffset End => new DateTimeOffset(To.AddDays(1).ToDateTime(TimeOnly.MinValue), Vn).ToUniversalTime();

    /// <summary>The same number of days just before this period ("so với kỳ trước").</summary>
    public ReportRange Previous() => new(From.AddDays(-Days), From.AddDays(-1), Granularity);

    public static DateOnly DayOf(DateTimeOffset utc) => DateOnly.FromDateTime(VietnamTime.ToLocal(utc).DateTime);

    public DateOnly BucketOf(DateOnly day) => Granularity switch
    {
        // ISO weeks start on Monday
        Granularity.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        Granularity.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    public DateOnly BucketOf(DateTimeOffset utc) => BucketOf(DayOf(utc));

    public IReadOnlyList<DateOnly> Buckets()
    {
        var list = new List<DateOnly>();
        for (var b = BucketOf(From); b <= To; b = Granularity switch { Granularity.Week => b.AddDays(7), Granularity.Month => b.AddMonths(1), _ => b.AddDays(1) })
            list.Add(b);
        return list;
    }

    // Labels are Vietnam dates — the chart's period names too (spec 8, trap 10)
    public string Label(DateOnly bucket) => Granularity switch
    {
        Granularity.Week => $"Tuần {bucket.ToString("dd/MM", CultureInfo.InvariantCulture)}",
        Granularity.Month => bucket.ToString("MM/yyyy", CultureInfo.InvariantCulture),
        _ => bucket.ToString("dd/MM", CultureInfo.InvariantCulture),
    };

    public string Describe() =>
        $"Từ {From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} đến {To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (giờ Việt Nam)";
}

public static class ReportOrders
{
    /// <summary>
    /// Orders that count in GMV: placed (COD, Ví, or paid online) — online orders that were never paid (still waiting or
    /// expired) do not count; orders cancelled after being placed do count (GMV is what buyers ordered).
    /// </summary>
    public static IQueryable<Order> Placed(this IQueryable<Order> orders) =>
        orders.Where(o => o.Status != OrderStatus.PendingPayment
                          && !(o.Status == OrderStatus.Cancelled && o.PaymentStatus == OrderPaymentStatus.Unpaid && o.PaymentMethod != PaymentMethod.Cod));

    /// <summary>Basis points of part / whole (0 when whole is 0).</summary>
    public static long Bp(long part, long whole) => whole == 0 ? 0 : (long)Math.Round(part * 10_000m / whole, MidpointRounding.AwayFromZero);
}

/// <summary>A chart point: label + up to two series.</summary>
public record ChartPoint(string Label, long Value, long? Value2 = null);

public record ChartDto(string Kind, string Series, string? Series2, IReadOnlyList<ChartPoint> Points);

public record ReportResult(Abstractions.ReportTable Table, ChartDto Chart);
