using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.Application;

/// <summary>
/// L206 / #218: the double-day sales run from the 8th to the end of the 12th, Vietnam time; a seed made after one is over
/// schedules next year's, never one that has passed.
/// </summary>
public class SaleWindowTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    [Theory]
    // On 12/10 (VN) the 10.10 sale still runs; the 11.11 one is ahead this year
    [InlineData("2026-10-12T23:59:00+07:00", 10, 2026)]
    [InlineData("2026-10-12T23:59:00+07:00", 11, 2026)]
    // From 13/10 00:00 VN (12/10 17:00 UTC) the 10.10 sale is next year's
    [InlineData("2026-10-12T17:00:00+00:00", 10, 2027)]
    [InlineData("2026-11-13T08:00:00+07:00", 11, 2027)]
    public void A_sale_runs_from_the_8th_to_the_end_of_the_12th_of_its_month(string now, int month, int year)
    {
        var (start, end) = MarketingSeeder.SaleWindow(DateTimeOffset.Parse(now), month);
        start.Should().Be(new DateTimeOffset(year, month, 8, 0, 0, 0, Vn));
        end.Should().Be(new DateTimeOffset(year, month, 13, 0, 0, 0, Vn));
    }
}
