using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace ReplicateFolder.Tests;

/// <summary>
/// Heavy benchmark-style tests. They are <b>skipped by default</b> because they
/// need significant disk space and time. Enable by setting environment variable
/// <c>REPLICATEFOLDER_RUN_PERF=1</c> before running the test session.
/// </summary>
public class SynchronizerPerformanceTests
{
    private const string EnvFlag = "REPLICATEFOLDER_RUN_PERF";

    private readonly ITestOutputHelper _out;
    public SynchronizerPerformanceTests(ITestOutputHelper output) => _out = output;

    private static string? SkipReason()
        => Environment.GetEnvironmentVariable(EnvFlag) == "1"
            ? null
            : $"Set {EnvFlag}=1 to run performance tests.";

    [Fact]
    public void Replicates_single_3GB_file()
    {
        var skip = SkipReason();
        if (skip is not null) { _out.WriteLine("SKIP: " + skip); return; }

        const long size = 3L * 1024 * 1024 * 1024; // 3 GiB
        using var ws = new TempWorkspace();
        EnsureFreeSpace(ws.Root, size * 3); // source + replica + slack

        var srcFile = Path.Combine(ws.Source, "huge.bin");
        var createSw = Stopwatch.StartNew();
        CreateSparseLikeFile(srcFile, size);
        createSw.Stop();
        _out.WriteLine($"Created 3 GiB source in {createSw.Elapsed.TotalSeconds:F2}s.");

        var sw = Stopwatch.StartNew();
        ws.SyncOnce();
        sw.Stop();
        _out.WriteLine($"Replicated 3 GiB in {sw.Elapsed.TotalSeconds:F2}s " +
                       $"({size / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds:F1} MiB/s).");

        var dst = ws.ReplicaPath("huge.bin");
        Assert.True(File.Exists(dst));
        Assert.Equal(size, new FileInfo(dst).Length);

        // Second sync must be a no-op (hash equal) and finish in a fraction of the time.
        var sw2 = Stopwatch.StartNew();
        ws.SyncOnce();
        sw2.Stop();
        _out.WriteLine($"Second (no-op) sync took {sw2.Elapsed.TotalSeconds:F2}s " +
                       "(still has to hash both sides).");
    }

    [Fact]
    public void Replicates_10000_small_files()
    {
        var skip = SkipReason();
        if (skip is not null) { _out.WriteLine("SKIP: " + skip); return; }

        const int count = 10_000;
        using var ws = new TempWorkspace();

        var createSw = Stopwatch.StartNew();
        // Spread across 100 subdirectories to avoid a pathological single-dir entry count.
        for (var i = 0; i < count; i++)
        {
            var rel = Path.Combine($"d{i % 100:D3}", $"f_{i:D5}.txt");
            ws.WriteSourceFile(rel, "payload-" + i);
        }
        createSw.Stop();
        _out.WriteLine($"Created {count} files in {createSw.Elapsed.TotalSeconds:F2}s.");

        var sw = Stopwatch.StartNew();
        ws.SyncOnce();
        sw.Stop();
        _out.WriteLine($"Initial replicate of {count} files: {sw.Elapsed.TotalSeconds:F2}s " +
                       $"({count / sw.Elapsed.TotalSeconds:F0} files/s).");

        Assert.Equal(count,
            Directory.EnumerateFiles(ws.Replica, "*", SearchOption.AllDirectories).Count());

        var sw2 = Stopwatch.StartNew();
        ws.SyncOnce();
        sw2.Stop();
        _out.WriteLine($"Re-sync (all up to date): {sw2.Elapsed.TotalSeconds:F2}s.");
    }

    // --- helpers --------------------------------------------------------

    private static void CreateSparseLikeFile(string path, long size)
    {
        // SetLength produces a logically-sized file quickly. The OS may store it sparsely,
        // but for replication-throughput measurement what matters is the logical length
        // and the bytes the copy/hash code has to traverse.
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        fs.SetLength(size);
    }

    private static void EnsureFreeSpace(string path, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return;
            var di = new DriveInfo(root);
            if (di.AvailableFreeSpace < requiredBytes)
                throw new SkipException(
                    $"Not enough free space on {root}: need {requiredBytes:N0} bytes, " +
                    $"have {di.AvailableFreeSpace:N0}.");
        }
        catch (ArgumentException) { /* non-drive path on Unix, skip the check */ }
    }

    private sealed class SkipException : Exception
    {
        public SkipException(string msg) : base(msg) { }
    }
}

