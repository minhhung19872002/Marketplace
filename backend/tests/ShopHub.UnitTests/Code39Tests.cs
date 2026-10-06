using FluentAssertions;
using ShopHub.Reporting;

namespace ShopHub.UnitTests;

public class Code39Tests
{
    [Fact]
    public void Every_symbol_has_nine_elements_of_which_exactly_three_are_wide_and_all_differ()
    {
        Code39.Patterns.Should().HaveCount(44 - 4, "0–9, A–Z, '-', '.', dấu cách và '*'");
        Code39.Patterns.Values.Should().OnlyContain(p => p.Length == 9 && p.Count(c => c == 'w') == 3);
        Code39.Patterns.Values.Should().OnlyHaveUniqueItems();
        Code39.Patterns['*'].Should().Be("nwnnwnwnn");
    }

    [Fact]
    public void Encodes_start_and_stop_around_the_data_and_refuses_unknown_characters()
    {
        // "*AB*": 4 symbols × 9 elements + 3 inter-character gaps
        Code39.Modules("AB").Should().HaveCount(4 * 9 + 3);
        var act = () => Code39.Modules("Đơn");
        act.Should().Throw<ArgumentException>();
        Code39.Svg("SIM123VN").Should().StartWith("<svg").And.Contain("<rect");
    }
}
