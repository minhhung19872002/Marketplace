using System.Text.Json;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;

namespace ShopHub.Application.Common;

/// <summary>
/// The one place that knows which days count (spec V "bỏ qua Chủ nhật/lễ"): delivery estimates, the shop's preparation
/// deadline (auto-cancel + penalty) and return parcels all go through it, so a holiday can never be skipped in one
/// path and counted in another.
/// </summary>
public interface IWorkingCalendar
{
    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/> (Vietnam dates).</summary>
    Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, CancellationToken ct);

    /// <summary>The holidays configured in LOGISTICS.HOLIDAYS.</summary>
    Task<IReadOnlySet<DateOnly>> HolidaysAsync(CancellationToken ct);
}

/// <summary>
/// Reads LOGISTICS.HOLIDAYS and LOGISTICS.WEEKLY_OFF_DAYS once per scope (one request / one job run) — the parameters
/// themselves are cached by <see cref="ISystemParameters"/>, so an admin change is picked up by the next request.
/// </summary>
public sealed class WorkingCalendar(ISystemParameters parameters) : IWorkingCalendar
{
    private (IReadOnlySet<DateOnly> Holidays, IReadOnlySet<DayOfWeek> OffDays)? _loaded;

    public async Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, CancellationToken ct)
    {
        var (holidays, offDays) = await LoadAsync(ct);
        return Add(from, days, holidays, offDays);
    }

    public async Task<IReadOnlySet<DateOnly>> HolidaysAsync(CancellationToken ct) => (await LoadAsync(ct)).Holidays;

    /// <summary>The pure rule; only this class calls it (code rule), always with the configured days.</summary>
    public static DateOnly Add(DateOnly from, int days, IReadOnlySet<DateOnly> holidays, IReadOnlySet<DayOfWeek> offDays)
    {
        // A misconfiguration that makes every day a day off must not loop forever
        if (offDays.Count >= 7) offDays = new HashSet<DayOfWeek>();
        var date = from;
        var added = 0;
        while (added < days)
        {
            date = date.AddDays(1);
            if (offDays.Contains(date.DayOfWeek) || holidays.Contains(date)) continue;
            added++;
        }
        return date;
    }

    public static IReadOnlySet<DateOnly> ParseHolidays(string raw)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(raw) ?? [])
                .Select(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out var date) ? date : (DateOnly?)null)
                .Where(d => d is not null).Select(d => d!.Value).ToHashSet();
        }
        catch (JsonException)
        {
            return new HashSet<DateOnly>();
        }
    }

    /// <summary>"[0]" = Sunday (0 = Chủ nhật … 6 = Thứ bảy).</summary>
    public static IReadOnlySet<DayOfWeek> ParseOffDays(string raw)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<int>>(raw) ?? []).Where(d => d is >= 0 and <= 6).Select(d => (DayOfWeek)d).ToHashSet();
        }
        catch (JsonException)
        {
            return new HashSet<DayOfWeek> { DayOfWeek.Sunday };
        }
    }

    private async Task<(IReadOnlySet<DateOnly> Holidays, IReadOnlySet<DayOfWeek> OffDays)> LoadAsync(CancellationToken ct)
    {
        _loaded ??= (ParseHolidays(await parameters.GetStringAsync(ParameterKeys.LogisticsHolidays, ct)),
            ParseOffDays(await parameters.GetStringAsync(ParameterKeys.LogisticsWeeklyOffDays, ct)));
        return _loaded.Value;
    }
}
