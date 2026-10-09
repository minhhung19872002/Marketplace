using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopHub.Application.Abstractions;

namespace ShopHub.Reporting;

/// <summary>
/// Đối soát theo kỳ (Excel + PDF) and hoá đơn phí sàn (PDF) for a shop (spec 3.9, III.7). Every row is a released
/// order; the totals are the sums of the rows, so the file ties out to the đồng with the shop's "khả dụng" credits.
/// </summary>
public sealed class FinanceDocuments : IFinanceDocuments
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public FinanceDocuments() => ShippingDocuments.EnsureFonts();

    private static string Vnd(long amount) => $"₫{amount.ToString("N0", Vi)}";

    private static string Day(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Vn).ToString("dd/MM/yyyy", Vi);

    // The period is [from, to): the last day shown is the day before "to"
    private static string Period(DateTimeOffset from, DateTimeOffset to) => $"{Day(from)} – {Day(to.AddTicks(-1))}";

    private static readonly string[] Headers =
        ["Mã đơn", "Hoàn thành", "Giải ngân", "Tiền hàng", "Giảm giá shop", "Hoàn tiền shop chịu", "Phí cố định", "Phí thanh toán", "Phí dịch vụ", "Thực nhận"];

    public byte[] SettlementReportExcel(SettlementReport report)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Đối soát");
        sheet.Cell(1, 1).Value = $"Báo cáo đối soát {report.ShopName} — {report.PlatformName}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = $"Kỳ: {Period(report.From, report.To)}";
        const int head = 4;
        for (var c = 0; c < Headers.Length; c++) sheet.Cell(head, c + 1).Value = Headers[c];
        sheet.Row(head).Style.Font.Bold = true;
        var row = head + 1;
        foreach (var r in report.Rows)
        {
            sheet.Cell(row, 1).Value = r.OrderCode;
            sheet.Cell(row, 2).Value = TimeZoneInfo.ConvertTime(r.CompletedAt, Vn).DateTime;
            sheet.Cell(row, 3).Value = TimeZoneInfo.ConvertTime(r.ReleasedAt, Vn).DateTime;
            sheet.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            sheet.Cell(row, 3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            long[] values = [r.Goods, r.ShopDiscount, r.RefundsBorne, r.FixedFee, r.PaymentFee, r.ServiceFee, r.Net];
            for (var k = 0; k < values.Length; k++) sheet.Cell(row, 4 + k).Value = values[k];
            row++;
        }
        sheet.Cell(row, 1).Value = $"Tổng ({report.Rows.Count} đơn)";
        long[] totals = [report.Goods, report.ShopDiscount, report.RefundsBorne, report.FixedFee, report.PaymentFee, report.ServiceFee, report.Net];
        for (var k = 0; k < totals.Length; k++) sheet.Cell(row, 4 + k).Value = totals[k];
        sheet.Row(row).Style.Font.Bold = true;
        sheet.Range(head + 1, 4, row, 10).Style.NumberFormat.Format = "#,##0";
        sheet.Columns().AdjustToContents(head, Math.Min(row, 300));
        sheet.SheetView.FreezeRows(head);
        using var ms = new MemoryStream();
        book.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] SettlementReportPdf(SettlementReport report) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(ShippingDocuments.BaseStyle(8.5f));
            page.Header().Column(col =>
            {
                col.Item().Text($"BÁO CÁO ĐỐI SOÁT — {report.ShopName}").Bold().FontSize(14);
                col.Item().Text($"Kỳ: {Period(report.From, report.To)} · Lập lúc {Day(report.GeneratedAt)} · {report.PlatformName}");
            });
            page.Content().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(1.1f);
                    for (var i = 0; i < 7; i++) c.RelativeColumn(1.2f);
                });
                table.Header(h =>
                {
                    foreach (var title in Headers) h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).Bold();
                });
                foreach (var r in report.Rows)
                {
                    table.Cell().Padding(3).Text(r.OrderCode);
                    table.Cell().Padding(3).Text(Day(r.CompletedAt));
                    table.Cell().Padding(3).Text(Day(r.ReleasedAt));
                    foreach (var v in new[] { r.Goods, r.ShopDiscount, r.RefundsBorne, r.FixedFee, r.PaymentFee, r.ServiceFee, r.Net })
                        table.Cell().Padding(3).AlignRight().Text(Vnd(v));
                }
                table.Cell().ColumnSpan(3).Padding(3).Text($"Tổng ({report.Rows.Count} đơn)").Bold();
                foreach (var v in new[] { report.Goods, report.ShopDiscount, report.RefundsBorne, report.FixedFee, report.PaymentFee, report.ServiceFee, report.Net })
                    table.Cell().Padding(3).AlignRight().Text(Vnd(v)).Bold();
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Trang ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();

    public byte[] FeeInvoicePdf(FeeInvoice invoice) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(ShippingDocuments.BaseStyle(10));
            page.Content().Column(col =>
            {
                col.Spacing(6);
                col.Item().Text(invoice.PlatformLegalName).Bold().FontSize(12);
                col.Item().Text(invoice.PlatformAddress);
                col.Item().Text($"Mã số thuế: {invoice.PlatformTaxCode}");
                col.Item().PaddingTop(12).AlignCenter().Text("HOÁ ĐƠN PHÍ DỊCH VỤ SÀN").Bold().FontSize(16);
                col.Item().AlignCenter().Text($"Số: {invoice.InvoiceNo} · Ngày lập: {Day(invoice.IssuedAt)}");
                col.Item().PaddingTop(12).Text(t =>
                {
                    t.Span("Đơn vị sử dụng dịch vụ: ").Bold();
                    t.Span(invoice.ShopName);
                });
                if (invoice.ShopTaxCode is { } tax) col.Item().Text($"Mã số thuế: {tax}");
                col.Item().Text($"Kỳ tính phí: {Period(invoice.From, invoice.To)} · {invoice.OrderCount} đơn hàng đã giải ngân");
                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(1.4f);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Khoản phí").Bold();
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Số tiền").Bold();
                    });
                    foreach (var (name, amount) in new[]
                             {
                                 ("Phí cố định (theo ngành hàng)", invoice.FixedFee), ("Phí thanh toán", invoice.PaymentFee),
                                 ("Phí dịch vụ (Freeship+ / Voucher Plus)", invoice.ServiceFee),
                             })
                    {
                        table.Cell().Padding(4).Text(name);
                        table.Cell().Padding(4).AlignRight().Text(Vnd(amount));
                    }
                    table.Cell().Padding(4).Text("Tổng cộng").Bold();
                    table.Cell().Padding(4).AlignRight().Text(Vnd(invoice.Total)).Bold();
                });
                col.Item().PaddingTop(8).Text("Các khoản phí đã được trừ vào tiền giải ngân của từng đơn hàng (xem báo cáo đối soát cùng kỳ).").Italic();
            });
        })).GeneratePdf();
}
