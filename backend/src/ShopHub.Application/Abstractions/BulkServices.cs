namespace ShopHub.Application.Abstractions;

/// <summary>Starts a <see cref="Domain.SystemConfig.BackgroundTask"/> outside the HTTP request (Hangfire); a sweep picks up any it missed.</summary>
public interface IBackgroundTasks
{
    void Enqueue(Guid taskId);
}

/// <summary>
/// The user a background job acts for (the person who started it): set by the job before it sends commands, read by
/// <see cref="ICurrentUser"/> when there is no HTTP request, so every permission check still applies to that person.
/// </summary>
public sealed class ActingUser
{
    public Guid? UserId { get; set; }
}

/// <summary>
/// Downloads a product image named by URL in an import sheet. Only https, only public addresses (checked when
/// connecting, so DNS tricks cannot reach the internal network), size and time capped. Throws BusinessRuleException.
/// </summary>
public interface IRemoteImageFetcher
{
    Task<byte[]> FetchAsync(string url, CancellationToken ct);
}

public record TemplateColumn(string Header, bool Required, string? Hint, IReadOnlyList<string>? Options);

/// <summary>One data row of an uploaded sheet: its row number in Excel and the cells by header (trimmed, empty omitted).</summary>
public record SheetRow(int Row, IReadOnlyDictionary<string, string> Cells);

public record SheetContent(IReadOnlyDictionary<string, string> Meta, IReadOnlyList<SheetRow> Rows);

public record PriceStockRow(Guid SkuId, string? SellerSku, string Product, string? Variant, long Price, long OriginalPrice, int Stock);

/// <summary>Excel files of the seller's bulk tools (ClosedXML in ShopHub.Reporting).</summary>
public interface IProductSheets
{
    /// <summary>Import template of one leaf category: header row, hint row, dropdowns, guide sheet; meta travels in a hidden sheet.</summary>
    byte[] ImportTemplate(string title, IReadOnlyList<TemplateColumn> columns, IReadOnlyDictionary<string, string> meta, IReadOnlyList<string> guide);

    byte[] PriceStockSheet(IReadOnlyList<PriceStockRow> rows, IReadOnlyDictionary<string, string> meta);

    /// <summary>Header in row 1; data from <paramref name="firstDataRow"/>. Throws BusinessRuleException for a file that is not a readable .xlsx.</summary>
    SheetContent Read(byte[] file, int firstDataRow);
}
