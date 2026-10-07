using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShopHub.Application.Abstractions;

namespace ShopHub.Reporting;

/// <summary>Any <see cref="ReportTable"/> as Excel (typed numbers) or PDF (A4 landscape, Vietnamese font, ligatures off).</summary>
public sealed class ReportDocuments : IReportDocuments
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public ReportDocuments() => ShippingDocuments.EnsureFonts();

    public static string Format(object cell, ReportCellKind kind) => kind switch
    {
        ReportCellKind.Money => $"₫{Convert.ToInt64(cell, Vi).ToString("N0", Vi)}",
        ReportCellKind.Integer => Convert.ToInt64(cell, Vi).ToString("N0", Vi),
        ReportCellKind.Percent => $"{(Convert.ToInt64(cell, Vi) / 100m).ToString("0.##", Vi)}%",
        _ => Convert.ToString(cell, Vi) ?? "",
    };

    public byte[] Excel(ReportTable table)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Báo cáo");
        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = table.Subtitle;
        const int head = 4;
        for (var c = 0; c < table.Columns.Count; c++) sheet.Cell(head, c + 1).Value = table.Columns[c].Title;
        sheet.Row(head).Style.Font.Bold = true;
        var row = head + 1;
        void Write(IReadOnlyList<object> cells, int r)
        {
            for (var c = 0; c < table.Columns.Count && c < cells.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (table.Columns[c].Kind)
                {
                    case ReportCellKind.Text:
                        cell.Value = Convert.ToString(cells[c], Vi) ?? "";
                        break;
                    case ReportCellKind.Percent:
                        cell.Value = Convert.ToInt64(cells[c], Vi) / 10_000d;
                        cell.Style.NumberFormat.Format = "0.00%";
                        break;
                    default:
                        cell.Value = Convert.ToInt64(cells[c], Vi);
                        cell.Style.NumberFormat.Format = "#,##0";
                        break;
                }
            }
        }
        foreach (var r in table.Rows) Write(r, row++);
        if (table.Totals is { } totals)
        {
            Write(totals, row);
            sheet.Row(row).Style.Font.Bold = true;
        }
        sheet.Columns().AdjustToContents(head, Math.Min(row, 300));
        sheet.SheetView.FreezeRows(head);
        using var ms = new MemoryStream();
        book.SaveAs(ms);
        return ms.ToArray();
    }

    public const int MaxChartPoints = 62;

    public byte[] Pdf(ReportTable table, ReportChart? chart = null) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(ShippingDocuments.BaseStyle(9));
            page.Header().Column(h =>
            {
                h.Item().Text(table.Title).FontSize(14).Bold();
                h.Item().Text(table.Subtitle).FontSize(9);
            });
            page.Content().PaddingTop(8).Column(content =>
            {
                if (chart is { Points.Count: > 0 }) content.Item().PaddingBottom(10).Element(e => Chart(e, chart));
                content.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    foreach (var col in table.Columns)
                        if (col.Kind == ReportCellKind.Text) c.RelativeColumn(2);
                        else c.RelativeColumn();
                });
                t.Header(h =>
                {
                    foreach (var col in table.Columns)
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(col.Title).Bold();
                });
                void Row(IReadOnlyList<object> cells, bool bold)
                {
                    for (var c = 0; c < table.Columns.Count; c++)
                    {
                        var text = c < cells.Count ? Format(cells[c], table.Columns[c].Kind) : "";
                        var cell = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
                        var span = table.Columns[c].Kind == ReportCellKind.Text ? cell.Text(text) : cell.AlignRight().Text(text);
                        if (bold) span.Bold();
                    }
                }
                foreach (var r in table.Rows) Row(r, false);
                if (table.Totals is { } totals) Row(totals, true);
            });
            });
            page.Footer().AlignRight().Text(x =>
            {
                x.Span("Trang ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        })).GeneratePdf();

    /// <summary>Horizontal bars (one per point, a second thinner bar for the second series), scaled to the largest value.</summary>
    private static void Chart(IContainer container, ReportChart chart)
    {
        var points = chart.Points.Take(MaxChartPoints).ToList();
        var max = Math.Max(1, points.Max(p => Math.Max(p.Value, p.Value2 ?? 0)));
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(col =>
        {
            col.Item().Text(chart.Series2 is null ? $"Biểu đồ: {chart.Series}" : $"Biểu đồ: {chart.Series} / {chart.Series2}").Bold();
            foreach (var p in points)
                col.Item().PaddingTop(2).Row(row =>
                {
                    row.ConstantItem(120).Text(p.Label).FontSize(7);
                    row.RelativeItem().Column(bars =>
                    {
                        Bar(bars, p.Value, max, Colors.Orange.Medium, 7);
                        if (chart.Series2 is not null) Bar(bars, p.Value2 ?? 0, max, Colors.Blue.Medium, 4);
                    });
                    row.ConstantItem(110).AlignRight().Text(chart.Series2 is null
                        ? p.Value.ToString("N0", Vi)
                        : $"{p.Value.ToString("N0", Vi)} / {(p.Value2 ?? 0).ToString("N0", Vi)}").FontSize(7);
                });
        });
    }

    private static void Bar(ColumnDescriptor bars, long value, long max, string color, float height)
    {
        var share = (float)Math.Clamp((double)value / max, 0, 1);
        bars.Item().Height(height).Row(r =>
        {
            if (share > 0) r.RelativeItem(share).Background(color);
            if (share < 1) r.RelativeItem(1 - share);
        });
    }
}
