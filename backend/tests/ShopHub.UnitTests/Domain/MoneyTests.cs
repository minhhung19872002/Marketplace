using FluentAssertions;
using ShopHub.Domain.Common;

namespace ShopHub.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Allocate_splits_exactly_with_largest_remainder()
    {
        // 100 over three equal weights: 33.33 each → the leftover đồng goes to the first (stable tie-break)
        var parts = Money.Vnd(100).Allocate([1, 1, 1]);

        parts.Select(p => p.Value).Should().Equal(34, 33, 33);
    }

    [Fact]
    public void Allocate_gives_leftover_to_largest_fractions()
    {
        // 50.000đ discount over lines of 30k / 20k / 10k → 25.000 / 16.666,67 / 8.333,33
        var parts = Money.Vnd(50_000).Allocate([30_000, 20_000, 10_000]);

        parts.Select(p => p.Value).Should().Equal(25_000, 16_667, 8_333);
    }

    [Fact]
    public void Allocate_with_zero_weights_keeps_the_whole_amount()
    {
        var parts = Money.Vnd(999).Allocate([0, 0]);

        parts.Sum(p => p.Value).Should().Be(999);
    }

    [Fact]
    public void Allocate_always_sums_to_the_original_amount()
    {
        var random = new Random(20261006);
        for (var run = 0; run < 5_000; run++)
        {
            var amount = random.NextInt64(0, 50_000_000);
            var weights = Enumerable.Range(0, random.Next(1, 12)).Select(_ => random.NextInt64(0, 10_000_000)).ToArray();

            var parts = Money.Vnd(amount).Allocate(weights);

            parts.Sum(p => p.Value).Should().Be(amount, "phân bổ không được lệch một đồng (lượt {0})", run);
            parts.Should().OnlyContain(p => p.Value >= 0);
        }
    }

    [Fact]
    public void Allocate_rejects_negative_inputs()
    {
        var negativeWeight = () => Money.Vnd(10).Allocate([1, -1]);
        var negativeAmount = () => Money.Vnd(-10).Allocate([1]);

        negativeWeight.Should().Throw<ArgumentException>();
        negativeAmount.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(100_000, 250, 2_500)]   // 2.5%
    [InlineData(99_999, 150, 1_500)]    // 1.49998.5 → rounds half up
    [InlineData(10, 5_000, 5)]
    public void PercentBp_rounds_half_away_from_zero(long amount, int bp, long expected)
    {
        Money.Vnd(amount).PercentBp(bp).Value.Should().Be(expected);
    }

    [Fact]
    public void Arithmetic_overflow_is_detected()
    {
        var overflow = () => Money.Vnd(long.MaxValue) + Money.Vnd(1);

        overflow.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Formats_in_vietnamese_style()
    {
        Money.Vnd(1_250_000).ToString().Should().Be("₫1.250.000");
    }
}
