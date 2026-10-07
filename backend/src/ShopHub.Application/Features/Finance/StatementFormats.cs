using System.Globalization;
using ShopHub.Application.Common;
using ShopHub.Domain.Common;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Finance;

/// <summary>One transaction of a gateway statement: what the gateway says was paid, refunded and charged as fee.</summary>
public record GatewayStatementRow(string TxnId, int Line, long Amount, long Refunded, long Fee);

public record ParsedGatewayStatement(IReadOnlyList<GatewayStatementRow> Payments, IReadOnlyList<(int Line, string Note)> BadLines,
    IReadOnlyList<(int Line, string TxnId)> Duplicates, int Lines);

/// <summary>
/// Readers for each gateway's reconciliation file (spec VI.7). The simulated gateway writes
/// <c>txn_id,payment_id,amount,refunded_amount,paid_at</c>; the merchant portals of VNPay, MoMo and ZaloPay export a
/// table with Vietnamese headers — columns are found by header name (accents, case and spacing ignored), so an export
/// with extra or reordered columns still reads. A refund is either its own row (type "Hoàn …", pointing at the original
/// transaction) or a "đã hoàn" column on the payment row. The header names follow the portals' transaction exports;
/// the exact contract files are confirmed with real merchant accounts (Phase 14 G).
/// </summary>
public static class GatewayStatementFormats
{
    private sealed record Format(string[] Txn, string[] Amount, string[] Refunded, string[] Fee, string[] Type, string[] Original);

    private static readonly string[] AmountNames = ["so tien", "so tien giao dich", "so tien thanh toan", "amount", "gia tri"];
    private static readonly string[] RefundedNames = ["so tien da hoan", "da hoan", "refunded amount", "so tien hoan"];
    private static readonly string[] FeeNames = ["phi", "phi giao dich", "phi dich vu", "fee"];
    private static readonly string[] TypeNames = ["loai giao dich", "loai gd", "loai", "transaction type", "type"];
    private static readonly string[] OriginalNames = ["ma gd goc", "ma giao dich goc", "giao dich goc", "original transaction"];

    private static Format Of(PaymentMethod method) => method switch
    {
        PaymentMethod.Simulated => new(["txn id"], ["amount"], ["refunded amount"], [], [], []),
        PaymentMethod.VnPay => new(["ma gd vnpay", "ma giao dich vnpay", "vnp transactionno", "ma giao dich"], AmountNames, RefundedNames, FeeNames,
            TypeNames, OriginalNames),
        PaymentMethod.MoMo => new(["ma giao dich momo", "ma gd momo", "trans id", "transid", "ma giao dich"], AmountNames, RefundedNames, FeeNames,
            TypeNames, OriginalNames),
        PaymentMethod.ZaloPay => new(["ma giao dich zalopay", "ma gd zalopay", "zp trans id", "ma giao dich"], AmountNames, RefundedNames,
            FeeNames, TypeNames, OriginalNames),
        _ => throw new BusinessRuleException("Phương thức này không có tệp đối soát của cổng."),
    };

    public static ParsedGatewayStatement Parse(PaymentMethod method, string text)
    {
        var format = Of(method);
        var rows = Csv.Parse(text);
        if (rows.Count == 0) return new([], [], [], 0);
        var header = rows[0].Select(Normalise).ToList();
        int Col(string[] names) => names.Select(n => header.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
        var (txn, amount, refunded, fee, type, original) =
            (Col(format.Txn), Col(format.Amount), Col(format.Refunded), Col(format.Fee), Col(format.Type), Col(format.Original));
        if (txn < 0 || amount < 0)
            throw new BusinessRuleException($"Tệp không đúng định dạng đối soát của {Label(method)}: thiếu cột mã giao dịch hoặc số tiền.");

        var payments = new Dictionary<string, GatewayStatementRow>();
        var order = new List<string>();
        var bad = new List<(int, string)>();
        var duplicates = new List<(int, string)>();
        var refunds = new List<(int Line, string Txn, long Amount)>();
        foreach (var (row, line) in rows.Skip(1).Select((r, i) => (r, i + 2)))
        {
            string Cell(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";
            var id = Cell(txn);
            if (id.Length == 0 || ParseAmount(Cell(amount)) is not { } value)
            {
                bad.Add((line, "Không đọc được mã giao dịch hoặc số tiền."));
                continue;
            }
            var isRefund = type >= 0 && Normalise(Cell(type)) is var t && (t.Contains("hoan") || t.Contains("refund"));
            if (isRefund)
            {
                refunds.Add((line, original >= 0 && Cell(original).Length > 0 ? Cell(original) : id, Math.Abs(value)));
                continue;
            }
            if (payments.ContainsKey(id))
            {
                duplicates.Add((line, id));
                continue;
            }
            payments[id] = new GatewayStatementRow(id, line, value, refunded >= 0 ? ParseAmount(Cell(refunded)) ?? 0 : 0,
                fee >= 0 ? ParseAmount(Cell(fee)) ?? 0 : 0);
            order.Add(id);
        }
        foreach (var r in refunds)
        {
            if (payments.TryGetValue(r.Txn, out var p)) payments[r.Txn] = p with { Refunded = p.Refunded + r.Amount };
            else bad.Add((r.Line, $"Dòng hoàn tiền trỏ tới giao dịch {r.Txn} không có trong tệp."));
        }
        return new(order.Select(id => payments[id]).ToList(), bad, duplicates, rows.Count - 1);
    }

    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.VnPay => "VNPay",
        PaymentMethod.MoMo => "MoMo",
        PaymentMethod.ZaloPay => "ZaloPay",
        _ => "cổng giả lập",
    };

    /// <summary>"1.250.000", "1,250,000 ₫", "-50000" → long; anything else → null.</summary>
    public static long? ParseAmount(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return null;
        return raw.TrimStart().StartsWith('-') ? -v : v;
    }

    private static string Normalise(string header) =>
        // Slug.Fold drops accents, case, punctuation (BOM, "_", "/") and collapses spaces
        Slug.Fold(header);
}
