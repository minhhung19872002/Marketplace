using System.Globalization;

namespace ShopHub.Domain.Common;

/// <summary>
/// Integer VND amount. Never use decimal/double for money in Domain/Application.
/// </summary>
public readonly record struct Money(long Value) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    public static Money Vnd(long value) => new(value);

    public static Money operator +(Money a, Money b) => new(checked(a.Value + b.Value));
    public static Money operator -(Money a, Money b) => new(checked(a.Value - b.Value));
    public static Money operator *(Money a, int quantity) => new(checked(a.Value * quantity));
    public static bool operator <(Money a, Money b) => a.Value < b.Value;
    public static bool operator >(Money a, Money b) => a.Value > b.Value;
    public static bool operator <=(Money a, Money b) => a.Value <= b.Value;
    public static bool operator >=(Money a, Money b) => a.Value >= b.Value;

    public static Money Min(Money a, Money b) => a <= b ? a : b;
    public static Money Max(Money a, Money b) => a >= b ? a : b;

    public int CompareTo(Money other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Share of this amount in basis points (1% = 100 bp), rounded half away from zero.
    /// </summary>
    public Money PercentBp(int basisPoints)
    {
        var product = (Int128)Value * basisPoints;
        var quotient = (long)(product / 10_000);
        var remainder = (long)(product % 10_000);
        if (Math.Abs(remainder) * 2 >= 10_000) quotient += Math.Sign(Value) * Math.Sign(basisPoints);
        return new Money(quotient);
    }

    /// <summary>
    /// Split this amount proportionally to <paramref name="weights"/> using the largest remainder method:
    /// the parts always sum exactly to the original amount. Ties go to the earlier index (stable).
    /// </summary>
    public Money[] Allocate(IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0) throw new ArgumentException("Cần ít nhất một trọng số.", nameof(weights));
        if (weights.Any(w => w < 0)) throw new ArgumentException("Trọng số không được âm.", nameof(weights));
        if (Value < 0) throw new InvalidOperationException("Không phân bổ số tiền âm.");

        var totalWeight = weights.Aggregate(Int128.Zero, (acc, w) => acc + w);
        var parts = new long[weights.Count];
        if (totalWeight == 0)
        {
            // No weights: everything goes to the first part so nothing is lost
            parts[0] = Value;
            return parts.Select(p => new Money(p)).ToArray();
        }

        var remainders = new Int128[weights.Count];
        long allocated = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = (Int128)Value * weights[i];
            parts[i] = (long)(exact / totalWeight);
            remainders[i] = exact % totalWeight;
            allocated += parts[i];
        }

        var leftover = Value - allocated;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < leftover; k++) parts[order[k]] += 1;

        return parts.Select(p => new Money(p)).ToArray();
    }

    public override string ToString() =>
        $"₫{Value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))}";
}
