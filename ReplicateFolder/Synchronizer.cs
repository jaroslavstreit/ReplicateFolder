using System.Diagnostics;
using System.Security.Cryptography;

namespace ReplicateFolder;

public sealed class Synchronizer
{
    private readonly string _source;
    private readonly string _replica;
    private readonly Logger _logger;

    public Synchronizer(string source, string replica, Logger logger)
    {
        _source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        _replica = Path.TrimEndingDirectorySeparator(Path.GetFullPath(replica));
        _logger = logger;
    }

    public void SyncOnce(CancellationToken ct)
    {
        _logger.Log("Sync started.");
        var sw = Stopwatch.StartNew();

        if (!Directory.Exists(_source))
        {
            _logger.LogError($"Source folder no longer exists: {_source}");
            return;
        }

        if (!Directory.Exists(_replica))
        {
            Directory.CreateDirectory(_replica);
            _logger.Log($"Created replica root: {_replica}");
        }

        // Pass 1 — replicate source content into replica (create/update).
        ReplicateInto(new DirectoryInfo(_source), _replica, ct);

        // Pass 2 — remove anything from replica that no longer exists in source.
        RemoveExtras(new DirectoryInfo(_replica), _source, ct);

        sw.Stop();
        _logger.Log($"Sync finished in {sw.Elapsed.TotalSeconds:F2}s.");
    }

    private void ReplicateInto(DirectoryInfo srcDir, string dstDir, CancellationToken ct)
    {
        foreach (var entry in srcDir.EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            var dstPath = Path.Combine(dstDir, entry.Name);

            try
            {
                if (IsSymlink(entry))
                {
                    // Replicate the link itself; never follow it.
                    ReplicateSymlink(entry, dstPath);
                }
                else if (entry is DirectoryInfo subDir)
                {
                    EnsureRealDirectory(dstPath);
                    ReplicateInto(subDir, dstPath, ct);
                }
                else if (entry is FileInfo srcFile)
                {
                    ReplicateFile(srcFile, dstPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to replicate '{entry.FullName}' -> '{dstPath}': {ex.Message}");
            }
        }
    }

    private void ReplicateSymlink(FileSystemInfo srcLink, string dstPath)
    {
        var target = srcLink.LinkTarget!;
        var existing = TryGetInfo(dstPath);

        if (existing is not null)
        {
            // Already a matching link of the same kind and target → nothing to do.
            if (IsSymlink(existing) && SameKind(existing, srcLink) && existing.LinkTarget == target)
                return;

            DeleteAny(existing);
            _logger.Log($"Removed (replacing with symlink): {dstPath}");
        }

        EnsureParentDirectory(dstPath);
        if (srcLink is DirectoryInfo)
            Directory.CreateSymbolicLink(dstPath, target);
        else
            File.CreateSymbolicLink(dstPath, target);

        _logger.Log($"Created symlink: {dstPath} -> {target}");
    }

    private void ReplicateFile(FileInfo srcFile, string dstPath)
    {
        var existing = TryGetInfo(dstPath);

        // If a directory or a symlink sits where a regular file should be, replace it.
        if (existing is DirectoryInfo || (existing is not null && IsSymlink(existing)))
        {
            DeleteAny(existing);
            _logger.Log($"Removed (type mismatch): {dstPath}");
            existing = null;
        }

        if (existing is null)
        {
            EnsureParentDirectory(dstPath);
            File.Copy(srcFile.FullName, dstPath, overwrite: false);
            _logger.Log($"Copied (new): {srcFile.FullName} -> {dstPath}");
        }
        else if (!FilesAreEqual(srcFile.FullName, dstPath))
        {
            File.Copy(srcFile.FullName, dstPath, overwrite: true);
            _logger.Log($"Copied (updated): {srcFile.FullName} -> {dstPath}");
        }
    }

    private void EnsureRealDirectory(string dstPath)
    {
        var existing = TryGetInfo(dstPath);

        // If a file or symlink sits where a real directory should be, replace it.
        if (existing is FileInfo || (existing is not null && IsSymlink(existing)))
        {
            DeleteAny(existing);
            _logger.Log($"Removed (type mismatch): {dstPath}");
            existing = null;
        }

        if (existing is null)
        {
            Directory.CreateDirectory(dstPath);
            _logger.Log($"Created directory: {dstPath}");
        }
    }

    private void RemoveExtras(DirectoryInfo replicaDir, string srcDirPath, CancellationToken ct)
    {
        foreach (var entry in replicaDir.EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            var srcPath = Path.Combine(srcDirPath, entry.Name);

            try
            {
                if (IsSymlink(entry))
                {
                    // Keep only if source has a symlink of the same kind at the same path.
                    // (Target equality was already enforced by the replicate pass.)
                    var srcInfo = TryGetInfo(srcPath);
                    if (srcInfo is null || !IsSymlink(srcInfo) || !SameKind(srcInfo, entry))
                    {
                        DeleteAny(entry); // removes the link only, never the target
                        _logger.Log($"Removed symlink: {entry.FullName}");
                    }
                    // Never recurse into a symlinked directory.
                }
                else if (entry is DirectoryInfo subDir)
                {
                    var srcInfo = TryGetInfo(srcPath);
                    if (srcInfo is null || srcInfo is FileInfo || IsSymlink(srcInfo))
                    {
                        Directory.Delete(subDir.FullName, recursive: true);
                        _logger.Log($"Removed directory: {subDir.FullName}");
                    }
                    else
                    {
                        RemoveExtras(subDir, srcPath, ct);
                    }
                }
                else if (entry is FileInfo file)
                {
                    var srcInfo = TryGetInfo(srcPath);
                    if (srcInfo is null || srcInfo is DirectoryInfo || IsSymlink(srcInfo))
                    {
                        file.Delete();
                        _logger.Log($"Removed file: {file.FullName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to remove '{entry.FullName}': {ex.Message}");
            }
        }
    }

    // --- helpers --------------------------------------------------------

    private static bool IsSymlink(FileSystemInfo info) => info.LinkTarget is not null;

    private static bool SameKind(FileSystemInfo a, FileSystemInfo b) =>
        (a is DirectoryInfo) == (b is DirectoryInfo);

    /// <summary>
    /// Returns a FileInfo or DirectoryInfo describing the entry without following links,
    /// or null if nothing exists at the path.
    /// </summary>
    private static FileSystemInfo? TryGetInfo(string path)
    {
        FileAttributes attrs;
        try { attrs = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        return (attrs & FileAttributes.Directory) != 0
            ? new DirectoryInfo(path)
            : new FileInfo(path);
    }

    private static void DeleteAny(FileSystemInfo info)
    {
        // For a real directory we need recursive delete; for everything else
        // (files, file symlinks, directory symlinks) a plain Delete just unlinks.
        if (info is DirectoryInfo di && !IsSymlink(di))
            di.Delete(recursive: true);
        else
            info.Delete();
    }

    private static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static bool FilesAreEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;

        // Built-in MD5 is sufficient for change detection (not a security boundary).
        var ha = HashFile(a);
        var hb = HashFile(b);
        // Faster than LINQ comparison: ReadOnlySpan<byte> picks MemoryExtensions.SequenceEqual
        // (vectorized) instead of System.Linq.Enumerable.SequenceEqual.
        return ((ReadOnlySpan<byte>)ha).SequenceEqual(hb);
    }

    private static byte[] HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return MD5.HashData(stream);
    }
}
