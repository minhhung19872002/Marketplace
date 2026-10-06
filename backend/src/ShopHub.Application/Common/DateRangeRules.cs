using FluentValidation;

namespace ShopHub.Application.Common;

public interface IDateRangeRequest
{
    DateTimeOffset? From { get; }
    DateTimeOffset? To { get; }
}

public static class DateRangeRules
{
    /// <summary>
    /// The single place where an inverted range (from &gt; to) is rejected with a clear message —
    /// never answered with a silently empty table.
    /// </summary>
    public static void ApplyDateRangeRules<T>(this AbstractValidator<T> validator) where T : IDateRangeRequest
    {
        validator.RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithName("from")
            .WithMessage("Khoảng thời gian không hợp lệ: \"Từ ngày\" phải trước hoặc bằng \"Đến ngày\".");
    }
}
