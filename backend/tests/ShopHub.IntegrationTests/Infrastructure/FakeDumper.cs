using ShopHub.Infrastructure.Ops;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>Writes a small file with pg_dump's signature ("PGDMP"); <see cref="FailWith"/> makes the next dumps fail like pg_dump would.</summary>
public sealed class FakeDumper : IDatabaseDumper
{
    public string? FailWith { get; set; }

    public async Task DumpAsync(string path, CancellationToken ct)
    {
        if (FailWith is { } error) throw new InvalidOperationException($"pg_dump lỗi (1): {error}");
        await File.WriteAllBytesAsync(path, [.. "PGDMP"u8, .. Enumerable.Repeat((byte)7, 2048)], ct);
    }

    public async Task VerifyAsync(string path, CancellationToken ct)
    {
        var head = (await File.ReadAllBytesAsync(path, ct)).Take(5).ToArray();
        if (!head.SequenceEqual("PGDMP"u8.ToArray())) throw new InvalidOperationException("pg_restore lỗi (1): not a dump");
    }
}
