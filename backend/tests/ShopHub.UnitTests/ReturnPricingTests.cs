using FluentAssertions;
using ShopHub.Application.Features.Returns;
using ShopHub.Domain.Common;
using ShopHub.Domain.Engage;

namespace ShopHub.UnitTests;

public class ReturnPricingTests
{
    [Fact]
    public void Successive_partial_returns_add_up_to_exactly_what_was_paid_for_the_line()
    {
        // 3 units, paid 100.000 after discounts (not divisible by 3) and 1.000 xu
        var first = ReturnPricing.ForUnits(100_000, 1_000, 3, 0, 1);
        var second = ReturnPricing.ForUnits(100_000, 1_000, 3, 1, 2);

        first.Should().Be((33_334L, 334L));
        (first.Money + second.Money).Should().Be(100_000);
        (first.Coins + second.Coins).Should().Be(1_000);
    }

    [Fact]
    public void Returning_more_than_is_left_is_refused_in_vietnamese()
    {
        var act = () => ReturnPricing.ForUnits(50_000, 0, 2, 1, 2);
        act.Should().Throw<BusinessRuleException>().WithMessage("Chỉ còn 1 sản phẩm có thể trả.");
    }

    [Theory]
    [InlineData("Nguyễn Văn An", false, "ng****an")]
    [InlineData("Bo", false, "b****")]
    [InlineData("Ai Đó", true, "Người mua ẩn danh")]
    public void Reviewer_names_are_masked(string name, bool anonymous, string expected) =>
        Review.MaskName(name, anonymous).Should().Be(expected);
}
