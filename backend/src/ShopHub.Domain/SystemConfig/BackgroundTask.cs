using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

public enum BackgroundTaskKind
{
    ProductImport,     // Đăng sản phẩm hàng loạt bằng Excel
    PriceStockUpdate,  // Cập nhật giá / tồn kho hàng loạt bằng Excel
    OrdersExport,      // Xuất danh sách đơn của shop ra Excel (6.4: không chạy trong lượt HTTP)
    AuditExport,       // Xuất nhật ký thao tác ra Excel
}

public enum BackgroundTaskStatus
{
    Queued,
    Running,
    Done,
    Failed,
}

/// <summary>One problem found while processing a row of an uploaded sheet (shown in the error table).</summary>
public record TaskRowError(int Row, string? Column, string Message);

/// <summary>
/// A long job started from a screen (spec 6.4: "việc dài không chạy trong lượt HTTP — xếp hàng, trả mã việc, có tiến độ").
/// The uploaded file waits in <see cref="Input"/> and is dropped once processed.
/// </summary>
public class BackgroundTask : Entity
{
    public const int MaxErrors = 500;

    private BackgroundTask() { }

    public BackgroundTask(BackgroundTaskKind kind, Guid ownerUserId, Guid? shopId, string fileName, byte[] input, DateTimeOffset now)
    {
        Kind = kind;
        OwnerUserId = ownerUserId;
        ShopId = shopId;
        FileName = fileName;
        Input = input;
        CreatedAt = now;
    }

    public BackgroundTaskKind Kind { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? ShopId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public byte[]? Input { get; private set; }
    public BackgroundTaskStatus Status { get; private set; } = BackgroundTaskStatus.Queued;
    public int Total { get; private set; }
    public int Processed { get; private set; }
    public int Succeeded { get; private set; }
    public int FailedCount { get; private set; }
    public List<TaskRowError> Errors { get; private set; } = [];
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public void Begin(int total)
    {
        Total = total;
        Processed = 0;
    }

    /// <summary>Absolute progress so far (the runner keeps the counters while it works and writes them every few rows).</summary>
    public void SetProgress(int processed, int succeeded, int failed, IReadOnlyList<TaskRowError> errors)
    {
        Processed = processed;
        Succeeded = succeeded;
        FailedCount = failed;
        Errors = errors.Take(MaxErrors).ToList();
    }

    // The file an export produced (downloaded by its owner)
    public string? OutputName { get; private set; }
    public string? OutputType { get; private set; }
    public byte[]? Output { get; private set; }

    public void Deliver(string fileName, string contentType, byte[] content, string message, DateTimeOffset now)
    {
        OutputName = fileName;
        OutputType = contentType;
        Output = content;
        Total = Processed = Succeeded = 1;
        Finish(message, now);
    }

    public void Finish(string message, DateTimeOffset now)
    {
        Status = BackgroundTaskStatus.Done;
        Message = message;
        Input = null;
        FinishedAt = now;
    }

    public void Fail(string message, DateTimeOffset now)
    {
        Status = BackgroundTaskStatus.Failed;
        Message = message;
        Input = null;
        FinishedAt = now;
    }
}
