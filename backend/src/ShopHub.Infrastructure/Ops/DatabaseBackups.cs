using System.Diagnostics;
using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Security;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Configuration;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Ops;

/// <summary>Writes a restorable dump of the database to a file and proves it can be read back.</summary>
public interface IDatabaseDumper
{
    Task DumpAsync(string path, CancellationToken ct);

    /// <summary>Throws when the file is not a readable dump.</summary>
    Task VerifyAsync(string path, CancellationToken ct);
}

/// <summary><c>pg_dump --format=custom</c> then <c>pg_restore --list</c> (password via PGPASSWORD, never on the command line); deploy/scripts/restore.sh reads the result.</summary>
public sealed class PgDumpDumper(ShopHubSettings settings) : IDatabaseDumper
{
    public Task DumpAsync(string path, CancellationToken ct) =>
        RunAsync("pg_dump", ["--format=custom", "--compress=6", "--no-owner", $"--file={path}"], ct);

    public Task VerifyAsync(string path, CancellationToken ct) => RunAsync("pg_restore", ["--list", path], ct);

    private async Task RunAsync(string tool, IReadOnlyList<string> args, CancellationToken ct)
    {
        var cs = new NpgsqlConnectionStringBuilder(settings.DbConnectionString);
        var start = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["PGHOST"] = cs.Host;
        start.Environment["PGPORT"] = cs.Port.ToString(CultureInfo.InvariantCulture);
        start.Environment["PGUSER"] = cs.Username;
        start.Environment["PGPASSWORD"] = cs.Password;
        start.Environment["PGDATABASE"] = cs.Database;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Không chạy được {tool}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        _ = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} lỗi ({process.ExitCode}): {(await stderr).Trim()}");
    }
}

public record BackupFileDto(string Name, long SizeBytes, DateTimeOffset WrittenAt);

public record BackupRunDto(Guid Id, string FileName, long? SizeBytes, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, BackupStatus Status, string? Error);

public record BackupsDto(string Directory, int KeepCount, IReadOnlyList<BackupFileDto> Files, IReadOnlyList<BackupRunDto> Runs);

/// <summary>
/// Sao lưu CSDL như một việc nền (spec 6.4, L085): lịch JOB.BACKUP_CRON (giờ VN), giữ BACKUP.KEEP_COUNT bản mới nhất
/// trong SH_BACKUP_DIR, mỗi lần chạy một dòng <c>sys.backup_runs</c>; hỏng thì báo quản trị xem được việc nền.
/// </summary>
public sealed class BackupService(
    ShopHubDbContext db,
    IDatabaseDumper dumper,
    ShopHubSettings settings,
    ISystemParameters parameters,
    AdminAlerts alerts,
    IClock clock,
    ILogger<BackupService> logger)
{
    public const string Prefix = "shophub-";

    public async Task<BackupRunDto> RunAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(settings.BackupDirectory);
        var now = clock.UtcNow;
        // Vietnam time; milliseconds so two "Sao lưu ngay" in one second never share a file
        var name = $"{Prefix}{VietnamTime.ToLocal(now):yyyyMMdd-HHmmss-fff}.dump";
        var run = new BackupRun(name, now);
        db.BackupRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var path = Path.Combine(settings.BackupDirectory, name);
        try
        {
            await dumper.DumpAsync(path + ".partial", ct);
            // A dump that cannot be listed is not a backup
            await dumper.VerifyAsync(path + ".partial", ct);
            File.Move(path + ".partial", path, overwrite: true);
            run.Succeeded(new FileInfo(path).Length, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            Prune((int)Math.Max(1, await parameters.GetIntAsync(ParameterKeys.BackupKeepCount, ct)));
            logger.LogInformation("Backup {File} written ({Bytes} bytes)", name, run.SizeBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (File.Exists(path + ".partial")) File.Delete(path + ".partial");
            run.Failed(ex.Message, clock.UtcNow);
            await alerts.RaiseAsync(Permissions.JobDashboardView, "Sao lưu CSDL thất bại", $"Bản {name}: {run.Error}", "/admin/viec-nen",
                $"backup-failed:{run.Id:N}", ct);
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Backup {File} failed", name);
        }
        return ToDto(run);
    }

    /// <summary>Keep the newest <paramref name="keep"/> dumps, delete the older ones.</summary>
    private void Prune(int keep)
    {
        foreach (var old in Files().Skip(keep))
        {
            File.Delete(Path.Combine(settings.BackupDirectory, old.Name));
            logger.LogInformation("Backup {File} removed (keeping {Keep})", old.Name, keep);
        }
    }

    public IReadOnlyList<BackupFileDto> Files() =>
        Directory.Exists(settings.BackupDirectory)
            ? new DirectoryInfo(settings.BackupDirectory).GetFiles($"{Prefix}*.dump").OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => new BackupFileDto(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero))).ToList()
            : [];

    public async Task<BackupsDto> OverviewAsync(CancellationToken ct)
    {
        var runs = await db.BackupRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id).Take(30).ToListAsync(ct);
        return new BackupsDto(settings.BackupDirectory, (int)await parameters.GetIntAsync(ParameterKeys.BackupKeepCount, ct), Files(),
            runs.Select(ToDto).ToList());
    }

    private static BackupRunDto ToDto(BackupRun r) => new(r.Id, r.FileName, r.SizeBytes, r.StartedAt, r.FinishedAt, r.Status, r.Error);
}

/// <summary>Hangfire entry for <see cref="BackupService"/>: a failed backup is recorded and alerted, not retried in a loop.</summary>
public sealed class BackupJob(BackupService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunJobAsync() => service.RunAsync(CancellationToken.None);
}
