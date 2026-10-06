using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopHub.Application.Abstractions;

namespace ShopHub.Reporting;

/// <summary>
/// Shipping labels and picking lists (QuestPDF, Community licence) and the order Excel export (ClosedXML).
/// Text uses the embedded Noto Sans with ligatures off, so the text read back from the PDF is exactly the text written
/// (spec section 8, rule 12).
/// </summary>
public sealed class ShippingDocuments : IShippingDocuments
{
    private const string Font = "Noto Sans";
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly object FontLock = new();
    private static bool _fontsReady;

    public ShippingDocuments() => EnsureFonts();

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady) return;
            QuestPDF.Settings.License = LicenseType.Community;
            // Never depend on whatever fonts the container happens to have
            QuestPDF.Settings.UseSystemFonts = false;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
            }
            _fontsReady = true;
        }
    }

    private static string Vnd(long amount) => $"₫{amount.ToString("N0", Vi)}";

    private static string Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Vn).ToString("HH:mm dd/MM/yyyy", Vi);

    private static TextStyle BaseStyle(float size) =>
        TextStyle.Default.FontFamily(Font).FontSize(size).DisableFontFeature(FontFeatures.StandardLigatures);

    public byte[] RenderLabels(IReadOnlyList<ShippingLabel> labels, LabelSize size) =>
        Document.Create(doc =>
        {
            foreach (var l in labels)
            {
                doc.Page(page =>
                {
                    page.Size(size == LabelSize.A5 ? PageSizes.A5 : PageSizes.A6);
                    page.Margin(size == LabelSize.A5 ? 18 : 10);
                    page.DefaultTextStyle(BaseStyle(size == LabelSize.A5 ? 10 : 7.5f));
                    page.Content().Column(col =>
                    {
                        col.Spacing(4);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(l.CarrierName).Bold().FontSize(size == LabelSize.A5 ? 14 : 10);
                            r.AutoItem().AlignRight().Text($"Đơn {l.OrderCode}").Bold();
                        });
                        col.Item().Height(size == LabelSize.A5 ? 60 : 38).Svg(Code39.Svg(l.TrackingNo));
                        col.Item().AlignCenter().Text(l.TrackingNo).Bold().FontSize(size == LabelSize.A5 ? 13 : 9).LetterSpacing(0.05f);
                        col.Item().LineHorizontal(0.5f);
                        col.Item().Text(t =>
                        {
                            t.Span("Từ: ").Bold();
                            t.Span($"{l.SenderName} · {l.SenderPhone}");
                        });
                        col.Item().Text(l.SenderAddress);
                        col.Item().LineHorizontal(0.5f);
                        col.Item().Text(t =>
                        {
                            t.Span("Đến: ").Bold();
                            t.Span($"{l.ReceiverName} · {l.ReceiverPhone}").Bold();
                        });
                        col.Item().Text(l.ReceiverAddress);
                        col.Item().LineHorizontal(0.5f);
                        col.Item().Text("Nội dung hàng").Bold();
                        foreach (var item in l.Items.Take(size == LabelSize.A5 ? 12 : 6))
                            col.Item().Text($"{item.Quantity} × {item.Name}{(item.Variant is null ? "" : $" ({item.Variant})")}");
                        if (l.Items.Count > (size == LabelSize.A5 ? 12 : 6)) col.Item().Text($"… và {l.Items.Count - (size == LabelSize.A5 ? 12 : 6)} dòng khác");
                        if (!string.IsNullOrWhiteSpace(l.BuyerNote)) col.Item().Text($"Ghi chú: {l.BuyerNote}").Italic();
                        col.Item().LineHorizontal(0.5f);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Thu hộ (COD): ").Bold();
                                t.Span(l.CodAmount > 0 ? Vnd(l.CodAmount) : "Không thu tiền").Bold().FontSize(size == LabelSize.A5 ? 13 : 9);
                            });
                            r.AutoItem().Text($"{l.WeightG:N0} g".Replace(',', '.'));
                        });
                        col.Item().Text($"Ngày tạo: {Local(l.CreatedAt)}").FontSize(size == LabelSize.A5 ? 8 : 6);
                    });
                });
            }
        }).GeneratePdf();

    public byte[] RenderPickingList(string shopName, IReadOnlyList<PickingLine> lines, DateTimeOffset printedAt) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(BaseStyle(10));
            page.Header().Column(c =>
            {
                c.Item().Text("PHIẾU SOẠN HÀNG").Bold().FontSize(16);
                c.Item().Text($"{shopName} · In lúc {Local(printedAt)} · {lines.Sum(l => l.Quantity)} sản phẩm");
            });
            page.Content().PaddingTop(10).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(2);
                    c.RelativeColumn(5);
                    c.ConstantColumn(48);
                    c.RelativeColumn(4);
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "#", "Mã SKU", "Sản phẩm", "SL", "Đơn hàng" })
                        h.Cell().BorderBottom(1).PaddingVertical(4).Text(title).Bold();
                });
                var i = 0;
                foreach (var l in lines)
                {
                    i++;
                    t.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(i.ToString(Vi));
                    t.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(l.SellerSku ?? "—");
                    t.Cell().BorderBottom(0.5f).PaddingVertical(3).Text($"{l.Name}{(l.Variant is null ? "" : $" ({l.Variant})")}");
                    t.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(l.Quantity.ToString(Vi)).Bold();
                    t.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(string.Join(", ", l.OrderCodes)).FontSize(8);
                }
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.Span("Trang ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();

    public byte[] ExportOrders(IReadOnlyList<OrderExportRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Đơn hàng");
        string[] headers = ["Mã đơn", "Ngày đặt", "Trạng thái", "Người mua", "SĐT nhận", "Địa chỉ nhận", "Sản phẩm", "Tiền hàng", "Giảm giá shop",
            "Phí vận chuyển", "Tổng thanh toán", "Thanh toán", "Mã vận đơn", "Đơn vị vận chuyển"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Row(1).Style.Font.Bold = true;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = rows[r];
            var row = r + 2;
            sheet.Cell(row, 1).Value = x.Code;
            sheet.Cell(row, 2).Value = TimeZoneInfo.ConvertTime(x.CreatedAt, Vn).DateTime;
            sheet.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            sheet.Cell(row, 3).Value = x.Status;
            sheet.Cell(row, 4).Value = x.BuyerName;
            sheet.Cell(row, 5).Value = x.ReceiverPhone;
            sheet.Cell(row, 6).Value = x.Address;
            sheet.Cell(row, 7).Value = x.Items;
            sheet.Cell(row, 8).Value = x.Subtotal;
            sheet.Cell(row, 9).Value = x.ShopDiscount;
            sheet.Cell(row, 10).Value = x.ShippingFee;
            sheet.Cell(row, 11).Value = x.GrandTotal;
            sheet.Cell(row, 12).Value = x.PaymentMethod;
            sheet.Cell(row, 13).Value = x.TrackingNo ?? "";
            sheet.Cell(row, 14).Value = x.Carrier ?? "";
            for (var c = 8; c <= 11; c++) sheet.Cell(row, c).Style.NumberFormat.Format = "#,##0";
        }
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        sheet.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        book.SaveAs(ms);
        return ms.ToArray();
    }
}
