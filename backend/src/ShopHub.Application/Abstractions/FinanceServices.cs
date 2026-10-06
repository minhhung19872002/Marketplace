namespace ShopHub.Application.Abstractions;

public record SettlementReportRow(string OrderCode, DateTimeOffset CompletedAt, DateTimeOffset ReleasedAt, long Goods, long ShopDiscount,
    long RefundsBorne, long FixedFee, long PaymentFee, long ServiceFee, long Net);

/// <summary>Đối soát theo kỳ of one shop: every released order of the period and the totals (they add up to the đồng).</summary>
public record SettlementReport(string ShopName, string PlatformName, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<SettlementReportRow> Rows,
    DateTimeOffset GeneratedAt)
{
    public long Goods => Rows.Sum(r => r.Goods);
    public long ShopDiscount => Rows.Sum(r => r.ShopDiscount);
    public long RefundsBorne => Rows.Sum(r => r.RefundsBorne);
    public long FixedFee => Rows.Sum(r => r.FixedFee);
    public long PaymentFee => Rows.Sum(r => r.PaymentFee);
    public long ServiceFee => Rows.Sum(r => r.ServiceFee);
    public long Net => Rows.Sum(r => r.Net);
}

/// <summary>Hoá đơn phí sàn: the fees the platform charged a shop over a period (from the same released orders).</summary>
public record FeeInvoice(string InvoiceNo, string PlatformLegalName, string PlatformAddress, string PlatformTaxCode, string ShopName, string? ShopTaxCode,
    DateTimeOffset From, DateTimeOffset To, long FixedFee, long PaymentFee, long ServiceFee, int OrderCount, DateTimeOffset IssuedAt)
{
    public long Total => FixedFee + PaymentFee + ServiceFee;
}

public interface IFinanceDocuments
{
    byte[] SettlementReportPdf(SettlementReport report);

    byte[] SettlementReportExcel(SettlementReport report);

    byte[] FeeInvoicePdf(FeeInvoice invoice);
}
