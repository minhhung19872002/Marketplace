using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

public enum BackupStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>One database backup attempt (spec 6.4): the file it wrote, its size, how it ended.</summary>
public class BackupRun : Entity
{
    private BackupRun() { }

    public BackupRun(string fileName, DateTimeOffset now)
    {
        FileName = fileName;
        StartedAt = now;
        Status = BackupStatus.Running;
    }

    public string FileName { get; private set; } = string.Empty;
    public long? SizeBytes { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public BackupStatus Status { get; private set; }
    public string? Error { get; private set; }

    public void Succeeded(long size, DateTimeOffset now)
    {
        SizeBytes = size;
        FinishedAt = now;
        Status = BackupStatus.Succeeded;
    }

    public void Failed(string error, DateTimeOffset now)
    {
        Error = error.Length > 1000 ? error[..1000] : error;
        FinishedAt = now;
        Status = BackupStatus.Failed;
    }
}
