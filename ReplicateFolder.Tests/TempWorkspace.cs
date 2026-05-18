using System.Text;
using ReplicateFolder;

namespace ReplicateFolder.Tests;

/// <summary>
/// Per-test temporary workspace with a Source dir, Replica dir and a Logger
/// pointed at a temp log file. Cleans up everything on Dispose.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public string Source { get; }
    public string Replica { get; }
    public string LogFile { get; }
    public Logger Logger { get; }
    public Synchronizer Sync { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ReplicateFolderTests_" + Guid.NewGuid().ToString("N"));
        Source = Path.Combine(Root, "source");
        Replica = Path.Combine(Root, "replica");
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Replica);

        LogFile = Path.Combine(Root, "sync.log");
        Logger = new Logger(LogFile);
        Sync = new Synchronizer(Source, Replica, Logger);
    }

    public void SyncOnce() => Sync.SyncOnce(CancellationToken.None);

    public string WriteSourceFile(string relativePath, string content)
        => WriteSourceFile(relativePath, Encoding.UTF8.GetBytes(content));

    public string WriteSourceFile(string relativePath, byte[] content)
    {
        var full = Path.Combine(Source, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string ReplicaPath(string relativePath) => Path.Combine(Replica, relativePath);
    public string SourcePath(string relativePath) => Path.Combine(Source, relativePath);

    public void Dispose()
    {
        try { Logger.Dispose(); } catch { /* ignore */ }
        TryDeleteTree(Root);
    }

    private static void TryDeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        // Clear read-only flags that can appear on Windows for some test artefacts.
        foreach (var f in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* ignore */ }
        }
        try { Directory.Delete(path, recursive: true); } catch { /* ignore */ }
    }
}

