using System.Text;
using Xunit;

namespace ReplicateFolder.Tests;

public class SynchronizerTests
{
    [Fact]
    public void Copies_new_file_from_source_to_replica()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("hello.txt", "hello world");

        ws.SyncOnce();

        Assert.Equal("hello world", File.ReadAllText(ws.ReplicaPath("hello.txt")));
    }

    [Fact]
    public void Recreates_nested_directory_structure()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile(Path.Combine("a", "b", "c", "file.txt"), "x");

        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath(Path.Combine("a", "b", "c", "file.txt"))));
    }

    [Fact]
    public void Updates_file_whose_content_changed()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("a.txt", "v1");
        ws.SyncOnce();
        Assert.Equal("v1", File.ReadAllText(ws.ReplicaPath("a.txt")));

        ws.WriteSourceFile("a.txt", "v2-longer");
        ws.SyncOnce();

        Assert.Equal("v2-longer", File.ReadAllText(ws.ReplicaPath("a.txt")));
    }

    [Fact]
    public void Updates_file_when_size_unchanged_but_content_differs()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("a.bin", new byte[] { 1, 2, 3, 4 });
        ws.SyncOnce();

        ws.WriteSourceFile("a.bin", new byte[] { 9, 9, 9, 9 });
        ws.SyncOnce();

        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(ws.ReplicaPath("a.bin")));
    }

    [Fact]
    public void Does_not_rewrite_identical_file()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("same.txt", "stable");
        ws.SyncOnce();

        var beforeWrite = File.GetLastWriteTimeUtc(ws.ReplicaPath("same.txt"));
        // Sleep to ensure timestamp would change if the file were rewritten.
        Thread.Sleep(50);

        ws.SyncOnce();

        var afterWrite = File.GetLastWriteTimeUtc(ws.ReplicaPath("same.txt"));
        Assert.Equal(beforeWrite, afterWrite);
    }

    [Fact]
    public void Removes_file_that_no_longer_exists_in_source()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("keep.txt", "k");
        ws.WriteSourceFile("delete.txt", "d");
        ws.SyncOnce();

        File.Delete(ws.SourcePath("delete.txt"));
        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath("keep.txt")));
        Assert.False(File.Exists(ws.ReplicaPath("delete.txt")));
    }

    [Fact]
    public void Removes_directory_tree_that_no_longer_exists_in_source()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile(Path.Combine("gone", "inner", "x.txt"), "x");
        ws.SyncOnce();
        Assert.True(File.Exists(ws.ReplicaPath(Path.Combine("gone", "inner", "x.txt"))));

        Directory.Delete(ws.SourcePath("gone"), recursive: true);
        ws.SyncOnce();

        Assert.False(Directory.Exists(ws.ReplicaPath("gone")));
    }

    [Fact]
    public void Removes_unexpected_files_added_to_replica()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("a.txt", "a");
        ws.SyncOnce();

        // User puts something extra in the replica – it must be removed.
        File.WriteAllText(ws.ReplicaPath("extra.txt"), "extra");
        Directory.CreateDirectory(ws.ReplicaPath("extraDir"));
        File.WriteAllText(Path.Combine(ws.ReplicaPath("extraDir"), "x.txt"), "x");

        ws.SyncOnce();

        Assert.False(File.Exists(ws.ReplicaPath("extra.txt")));
        Assert.False(Directory.Exists(ws.ReplicaPath("extraDir")));
        Assert.True(File.Exists(ws.ReplicaPath("a.txt")));
    }

    [Fact]
    public void Replaces_replica_directory_when_source_has_file_with_same_name()
    {
        using var ws = new TempWorkspace();
        Directory.CreateDirectory(ws.ReplicaPath("conflict"));
        File.WriteAllText(Path.Combine(ws.ReplicaPath("conflict"), "old.txt"), "old");

        ws.WriteSourceFile("conflict", "now I'm a file");
        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath("conflict")));
        Assert.False(Directory.Exists(Path.Combine(ws.ReplicaPath("conflict"), "old.txt")));
        Assert.Equal("now I'm a file", File.ReadAllText(ws.ReplicaPath("conflict")));
    }

    [Fact]
    public void Replaces_replica_file_when_source_has_directory_with_same_name()
    {
        using var ws = new TempWorkspace();
        File.WriteAllText(ws.ReplicaPath("conflict"), "I was a file");

        ws.WriteSourceFile(Path.Combine("conflict", "inside.txt"), "inside");
        ws.SyncOnce();

        Assert.True(Directory.Exists(ws.ReplicaPath("conflict")));
        Assert.Equal("inside", File.ReadAllText(Path.Combine(ws.ReplicaPath("conflict"), "inside.txt")));
    }

    [Fact]
    public void Creates_replica_root_if_missing()
    {
        using var ws = new TempWorkspace();
        Directory.Delete(ws.Replica);
        ws.WriteSourceFile("x.txt", "x");

        ws.SyncOnce();

        Assert.True(Directory.Exists(ws.Replica));
        Assert.True(File.Exists(ws.ReplicaPath("x.txt")));
    }

    [Fact]
    public void Empty_source_produces_empty_replica()
    {
        using var ws = new TempWorkspace();
        File.WriteAllText(ws.ReplicaPath("leftover.txt"), "x");

        ws.SyncOnce();

        Assert.Empty(Directory.GetFileSystemEntries(ws.Replica));
    }

    [Fact]
    public void Handles_zero_byte_files()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("empty.bin", Array.Empty<byte>());

        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath("empty.bin")));
        Assert.Equal(0, new FileInfo(ws.ReplicaPath("empty.bin")).Length);
    }

    [Fact]
    public void Preserves_empty_directories()
    {
        using var ws = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(ws.Source, "emptyDir"));

        ws.SyncOnce();

        Assert.True(Directory.Exists(ws.ReplicaPath("emptyDir")));
    }

    [Fact]
    public void Missing_source_logs_error_and_does_not_throw()
    {
        using var ws = new TempWorkspace();
        Directory.Delete(ws.Source);

        var ex = Record.Exception(() => ws.SyncOnce());
        Assert.Null(ex);

        ws.Logger.Dispose(); // release the file handle before reading
        var log = File.ReadAllText(ws.LogFile);
        Assert.Contains("Source folder no longer exists", log);
    }

    [Fact]
    public void Honors_cancellation_token()
    {
        using var ws = new TempWorkspace();
        for (var i = 0; i < 50; i++)
            ws.WriteSourceFile($"f{i}.txt", "x");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ws.Sync.SyncOnce(cts.Token));
    }
}


