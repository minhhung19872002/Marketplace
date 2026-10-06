namespace ShopHub.Application.Abstractions;

public enum ReportCellKind
{
    Text,
    Integer,  // counts
    Money,    // VND
    Percent,  // basis points (1 = 0.01%)
}

public record ReportColumn(string Title, ReportCellKind Kind);

/// <summary>
/// One report as a table (spec VI.10, III.8): the same rows feed the screen, the Excel file and the PDF, so the three
/// always agree. Cells are string (Text) or long (Integer / Money / Percent in basis points).
/// </summary>
public record ReportTable(string Title, string Subtitle, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object>> Rows,
    IReadOnlyList<object>? Totals = null);

public interface IReportDocuments
{
    byte[] Excel(ReportTable table);

    byte[] Pdf(ReportTable table);
}
