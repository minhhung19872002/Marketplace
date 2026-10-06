using FluentAssertions;
using ShopHub.Domain.Common;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.UnitTests.Domain;

public class SystemParameterTests
{
    [Theory]
    [InlineData(ParameterDataType.Int, "12", true)]
    [InlineData(ParameterDataType.Int, "12.5", false)]
    [InlineData(ParameterDataType.Bool, "true", true)]
    [InlineData(ParameterDataType.Bool, "yes", false)]
    [InlineData(ParameterDataType.Cron, "*/5 * * * *", true)]
    [InlineData(ParameterDataType.Cron, "every minute", false)]
    [InlineData(ParameterDataType.Json, "{\"a\":1}", true)]
    [InlineData(ParameterDataType.Json, "{a:1", false)]
    [InlineData(ParameterDataType.String, "bất kỳ", true)]
    public void Validate_checks_value_against_data_type(ParameterDataType type, string value, bool valid)
    {
        (SystemParameter.Validate(type, value) is null).Should().Be(valid);
    }

    [Fact]
    public void SetValue_rejects_wrong_type_with_vietnamese_message()
    {
        var p = new SystemParameter("X.Y", "1", ParameterDataType.Int, "X", "Tên", "Mô tả");

        var act = () => p.SetValue("abc");

        act.Should().Throw<BusinessRuleException>().WithMessage("Giá trị phải là số nguyên.");
        p.Value.Should().Be("1");
    }
}
