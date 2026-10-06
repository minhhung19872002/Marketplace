using System.Globalization;
using ClosedXML.Excel;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Common;

namespace ShopHub.Reporting;

/// <summary>Excel files of the seller's bulk tools: import template per category, price / stock sheet, and their reader.</summary>
public sealed class ProductSheets : IProductSheets
{
    private const string DataSheet = "Dữ liệu";
    private const string GuideSheet = "Hướng dẫn";
    private const string MetaSheet = "_shophub";
    private const string ListsSheet = "_lists";

    public byte[] ImportTemplate(string title, IReadOnlyList<TemplateColumn> columns, IReadOnlyDictionary<string, string> meta, IReadOnlyList<string> guide)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(DataSheet);
        var lists = book.Worksheets.Add(ListsSheet);
        lists.Visibility = XLWorksheetVisibility.VeryHidden;
        for (var c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            var head = sheet.Cell(1, c + 1);
            head.Value = col.Header;
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = col.Required ? XLColor.FromHtml("#FDE7E1") : XLColor.FromHtml("#F2F2F2");
            var hint = sheet.Cell(2, c + 1);
            hint.Value = col.Hint ?? (col.Options is { Count: > 0 } o ? string.Join(" | ", o.Take(6)) + (o.Count > 6 ? " …" : "") : "");
            hint.Style.Font.Italic = true;
            hint.Style.Font.FontColor = XLColor.FromHtml("#6B6B6B");
            sheet.Column(c + 1).Width = Math.Clamp(col.Header.Length + 6, 14, 40);
            if (col.Options is { Count: > 0 } options)
            {
                // A dropdown backed by a hidden list (Excel limits inline lists to 255 characters)
                for (var i = 0; i < options.Count; i++) lists.Cell(i + 1, c + 1).Value = options[i];
                var range = lists.Range(1, c + 1, options.Count, c + 1);
                sheet.Range(3, c + 1, 1002, c + 1).CreateDataValidation().List(range, true);
            }
        }
        sheet.SheetView.FreezeRows(2);

        var guideSheet = book.Worksheets.Add(GuideSheet);
        guideSheet.Cell(1, 1).Value = title;
        guideSheet.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < guide.Count; i++) guideSheet.Cell(i + 3, 1).Value = guide[i];
        guideSheet.Column(1).Width = 120;

        WriteMeta(book, meta);
        sheet.SetTabActive();
        return Save(book);
    }

    public byte[] PriceStockSheet(IReadOnlyList<PriceStockRow> rows, IReadOnlyDictionary<string, string> meta)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(DataSheet);
        string[] headers = ["ID SKU (không sửa)", "Mã SKU", "Sản phẩm", "Phân loại", "Giá*", "Giá gốc", "Tồn kho*"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = r.SkuId.ToString();
            sheet.Cell(row, 2).Value = r.SellerSku ?? "";
            sheet.Cell(row, 3).Value = r.Product;
            sheet.Cell(row, 4).Value = r.Variant ?? "";
            sheet.Cell(row, 5).Value = r.Price;
            sheet.Cell(row, 6).Value = r.OriginalPrice;
            sheet.Cell(row, 7).Value = r.Stock;
        }
        sheet.Column(1).Width = 38;
        sheet.Column(3).Width = 50;
        sheet.Columns(4, 7).Width = 16;
        sheet.Range(2, 1, Math.Max(2, rows.Count + 1), 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        sheet.SheetView.FreezeRows(1);
        WriteMeta(book, meta);
        return Save(book);
    }

    public SheetContent Read(byte[] file, int firstDataRow)
    {
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(new MemoryStream(file));
        }
        catch (Exception)
        {
            throw new BusinessRuleException("Không đọc được tệp. Vui lòng dùng tệp .xlsx tải từ Kênh Người Bán.");
        }
        using (book)
        {
            var meta = new Dictionary<string, string>();
            if (book.Worksheets.TryGetWorksheet(MetaSheet, out var metaSheet))
                foreach (var row in metaSheet.RowsUsed())
                    meta[row.Cell(1).GetString()] = row.Cell(2).GetString();
            if (!book.Worksheets.TryGetWorksheet(DataSheet, out var sheet)) throw new BusinessRuleException($"Tệp không có trang \"{DataSheet}\".");

            var headers = new Dictionary<int, string>();
            var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastColumn; c++)
                if (sheet.Cell(1, c).GetString().Trim() is { Length: > 0 } h) headers[c] = h;

            var rows = new List<SheetRow>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var r = firstDataRow; r <= lastRow; r++)
            {
                var cells = new Dictionary<string, string>();
                foreach (var (c, h) in headers)
                {
                    var cell = sheet.Cell(r, c);
                    var text = cell.DataType == XLDataType.Number
                        ? cell.GetDouble().ToString("0.##########", CultureInfo.InvariantCulture)
                        : cell.GetFormattedString().Trim();
                    if (text.Length > 0) cells[h] = text;
                }
                if (cells.Count > 0) rows.Add(new SheetRow(r, cells));
            }
            return new SheetContent(meta, rows);
        }
    }

    private static void WriteMeta(XLWorkbook book, IReadOnlyDictionary<string, string> meta)
    {
        var sheet = book.Worksheets.Add(MetaSheet);
        sheet.Visibility = XLWorksheetVisibility.VeryHidden;
        var i = 1;
        foreach (var (key, value) in meta)
        {
            sheet.Cell(i, 1).Value = key;
            sheet.Cell(i, 2).Value = value;
            i++;
        }
    }

    private static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
