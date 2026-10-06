using ShopHub.Domain.Common;

namespace ShopHub.Application.Features.Returns;

/// <summary>
/// What returning some units of an order line gives back (spec 3.8): the money the buyer paid for exactly those
/// units after every allocated discount, plus the xu used on them. The line's amounts are split over its units with
/// the largest remainder method and returns take units in order, so successive partial returns of the same line
/// always add up to the line's total — never a đồng more or less.
/// </summary>
public static class ReturnPricing
{
    public static (long Money, long Coins) ForUnits(long paidMoney, long paidCoins, int lineQuantity, int alreadyReturned, int returning)
    {
        if (lineQuantity < 1) throw new ArgumentOutOfRangeException(nameof(lineQuantity));
        if (alreadyReturned < 0 || returning < 1 || alreadyReturned + returning > lineQuantity)
            throw new BusinessRuleException($"Chỉ còn {lineQuantity - alreadyReturned} sản phẩm có thể trả.");
        if (paidMoney < 0 || paidCoins < 0) throw new ArgumentOutOfRangeException(nameof(paidMoney));
        return (Take(paidMoney, lineQuantity, alreadyReturned, returning), Take(paidCoins, lineQuantity, alreadyReturned, returning));
    }

    private static long Take(long total, int units, int skip, int take)
    {
        if (total == 0) return 0;
        var parts = Money.Vnd(total).Allocate(Enumerable.Repeat(1L, units).ToList());
        return parts.Skip(skip).Take(take).Sum(p => p.Value);
    }
}
