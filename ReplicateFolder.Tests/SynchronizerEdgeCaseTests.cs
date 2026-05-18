using System.Text;
using Xunit;

namespace ReplicateFolder.Tests;

/// <summary>
/// Tricky filenames and structure shapes that real-world filesystems throw at us.
/// </summary>
public class SynchronizerEdgeCaseTests
{
    [Theory]
    [InlineData("file with spaces.txt")]
    [InlineData("file.with.many.dots.txt")]
    [InlineData("UPPER_lower-MIXED.TXT")]
    [InlineData("dash-and_underscore.txt")]
    [InlineData("parens (1) [2] {3}.txt")]
    [InlineData("plus+and=equals&amp.txt")]
    [InlineData("hash#and%percent.txt")]
    [InlineData(".hiddenStyle")]
    [InlineData("trailing.dot.")]
    public void Special_character_filenames_roundtrip(string name)
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile(name, "payload-" + name);

        ws.SyncOnce();

        Assert.Equal("payload-" + name, File.ReadAllText(ws.ReplicaPath(name)));
    }

    [Theory]
    [InlineData("ümlaut_ñoño_漢字_🚀.txt")]
    [InlineData("Привет-мир.txt")]
    [InlineData("日本語ファイル.txt")]
    [InlineData("emoji-🐉🔥.bin")]
    public void Unicode_filenames_roundtrip(string name)
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile(name, "u");

        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath(name)), $"missing: {name}");
    }

    [Fact]
    public void Long_filename_close_to_limit()
    {
        using var ws = new TempWorkspace();
        // 200 chars + extension – safely under MAX_PATH-260 for the full path.
        var name = new string('a', 200) + ".txt";
        ws.WriteSourceFile(name, "x");

        ws.SyncOnce();

        Assert.True(File.Exists(ws.ReplicaPath(name)));
    }

    [Fact]
    public void Deeply_nested_directory_tree()
    {
        using var ws = new TempWorkspace();
        // 20 levels deep, each segment short to stay within typical path limits.
        var rel = string.Join(Path.DirectorySeparatorChar,
            Enumerable.Range(0, 20).Select(i => "lvl" + i));
        ws.WriteSourceFile(Path.Combine(rel, "leaf.txt"), "deep");

        ws.SyncOnce();

        Assert.Equal("deep", File.ReadAllText(ws.ReplicaPath(Path.Combine(rel, "leaf.txt"))));
    }

    [Fact]
    public void Many_files_in_single_directory()
    {
        using var ws = new TempWorkspace();
        const int count = 500;
        for (var i = 0; i < count; i++)
            ws.WriteSourceFile($"f_{i:D4}.txt", i.ToString());

        ws.SyncOnce();

        Assert.Equal(count, Directory.GetFiles(ws.Replica).Length);
    }

    [Fact]
    public void Binary_file_with_all_byte_values()
    {
        using var ws = new TempWorkspace();
        var bytes = new byte[256];
        for (var i = 0; i < 256; i++) bytes[i] = (byte)i;
        ws.WriteSourceFile("all-bytes.bin", bytes);

        ws.SyncOnce();

        Assert.Equal(bytes, File.ReadAllBytes(ws.ReplicaPath("all-bytes.bin")));
    }

    [Fact]
    public void Case_change_only_is_handled_on_case_insensitive_fs()
    {
        // On Windows/macOS-default the names collide and the existing file is updated.
        // On case-sensitive Linux fs we'd end up with two files. Just assert the
        // source content is reachable in the replica under at least one casing.
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("Readme.MD", "case");
        ws.SyncOnce();

        var matches = Directory.GetFiles(ws.Replica)
            .Where(f => string.Equals(Path.GetFileName(f), "Readme.MD", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Single(matches);
        Assert.Equal("case", File.ReadAllText(matches[0]));
    }

    [Fact]
    public void Repeated_sync_is_idempotent()
    {
        using var ws = new TempWorkspace();
        ws.WriteSourceFile("a.txt", "a");
        ws.WriteSourceFile(Path.Combine("d", "b.txt"), "b");

        ws.SyncOnce();
        var snapshot1 = SnapshotReplica(ws);

        ws.SyncOnce();
        ws.SyncOnce();
        var snapshot3 = SnapshotReplica(ws);

        Assert.Equal(snapshot1, snapshot3);
    }

    private static string SnapshotReplica(TempWorkspace ws)
    {
        var sb = new StringBuilder();
        foreach (var entry in Directory.EnumerateFileSystemEntries(ws.Replica, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(ws.Replica, entry);
            if (File.Exists(entry))
                sb.Append("F ").Append(rel).Append(' ').AppendLine(new FileInfo(entry).Length.ToString());
            else
                sb.Append("D ").AppendLine(rel);
        }
        return sb.ToString();
    }
}

