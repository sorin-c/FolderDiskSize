using System.Diagnostics;
using FolderDiskSize.Core;
using FolderDiskSize.Core.Scanning;

namespace FolderDiskSize.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fds-test-").FullName;
    private readonly FileSystemScanner _scanner = new(new ClusterRoundingAllocationProvider(4096));

    public void Dispose() => DeleteTree(_root);

    /// <summary>
    /// Removes links before the recursive delete: on Windows, Directory.Delete(recursive) fails with
    /// "Access denied" on a junction, so each link is deleted on its own first.
    /// </summary>
    private static void DeleteTree(string path)
    {
        foreach (var dir in new DirectoryInfo(path).EnumerateDirectories())
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) dir.Delete();
            else DeleteTree(dir.FullName);
        }
        Directory.Delete(path, recursive: true);
    }

    private void WriteFile(string relative, int length)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }

    /// <summary>
    /// Symlinks need admin or Developer Mode on Windows, so fall back to a junction there,
    /// which any user can create and the scanner treats the same way.
    /// </summary>
    private static void CreateFolderLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException)
        {
            var mklink = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) mklink.ArgumentList.Add(arg);
            using var process = Process.Start(mklink)!;
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException($"mklink /J failed for {link}", e);
        }
    }

    [Fact]
    public async Task Aggregates_sizes_and_counts_up_the_tree()
    {
        WriteFile("a.bin", 100);
        WriteFile("big/b.bin", 5000);
        WriteFile("big/deep/c.bin", 10);
        WriteFile("big/deep/d.bin", 20);
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var root = await _scanner.ScanAsync(_root);

        Assert.Equal(5130, root.Size);
        Assert.Equal(4096 + 8192 + 4096 + 4096, root.Allocated);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.FolderCount);

        var big = root.Children.Single(c => c.Name == "big");
        Assert.Equal(5030, big.Size);
        Assert.Equal(3, big.FileCount);
        Assert.Equal(1, big.FolderCount);
        Assert.Same(root, big.Parent);
        Assert.Equal(5030 * 100.0 / 5130, big.PercentOfParent, 6);
    }

    [Fact]
    public async Task Find_locates_nested_nodes_by_path()
    {
        WriteFile("x/y/z.txt", 1);
        var root = await _scanner.ScanAsync(_root);

        var y = root.Find(Path.Combine(_root, "x", "y"));
        Assert.NotNull(y);
        Assert.Equal("y", y.Name);
        Assert.Same(root, root.Find(_root + Path.DirectorySeparatorChar));
        Assert.Null(root.Find(Path.Combine(_root, "nope")));
        Assert.Null(root.Find(Path.GetTempPath()));
    }

    [Fact]
    public async Task ReplaceChild_updates_totals_up_the_tree()
    {
        WriteFile("a.bin", 100);
        WriteFile("sub/deep/b.bin", 1000);
        var root = await _scanner.ScanAsync(_root);
        var sub = root.Children.Single(c => c.Name == "sub");

        WriteFile("sub/deep/c.bin", 500);
        WriteFile("sub/new/d.bin", 50);
        var fresh = await _scanner.ScanAsync(Path.Combine(_root, "sub"));

        Assert.True(sub.Parent!.ReplaceChild(sub, fresh));
        Assert.Same(root, fresh.Parent);
        Assert.Contains(fresh, root.Children);
        Assert.Equal(1650, root.Size);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.FolderCount);
        Assert.False(root.ReplaceChild(sub, fresh));
    }

    [Fact]
    public async Task RemoveChild_subtracts_totals_up_the_tree()
    {
        WriteFile("a.bin", 100);
        WriteFile("sub/deep/b.bin", 1000);
        WriteFile("sub/c.bin", 10);
        var root = await _scanner.ScanAsync(_root);
        var sub = root.Children.Single(c => c.Name == "sub");
        var deep = sub.Children.Single(c => c.Name == "deep");
        var c = sub.Children.Single(n => n.Name == "c.bin");

        Assert.True(sub.RemoveChild(c));
        Assert.Equal(1100, root.Size);
        Assert.Equal(2, root.FileCount);

        Assert.True(sub.RemoveChild(deep));
        Assert.Equal(100, root.Size);
        Assert.Equal(1, root.FileCount);
        Assert.Equal(1, root.FolderCount);
        Assert.Equal(0, sub.Size);
        Assert.Null(deep.Parent);
        Assert.False(sub.RemoveChild(deep));
    }

    [Fact]
    public async Task Does_not_follow_symlinked_folders()
    {
        WriteFile("real/file.bin", 1000);
        CreateFolderLink(Path.Combine(_root, "link"), Path.Combine(_root, "real"));

        var root = await _scanner.ScanAsync(_root);

        Assert.Equal(1000, root.Size);
        var link = root.Children.Single(c => c.Name == "link");
        Assert.True(link.IsReparsePoint);
        Assert.Empty(link.Children);
    }

    [Fact]
    public async Task Reports_final_progress()
    {
        WriteFile("one.bin", 10);
        WriteFile("sub/two.bin", 10);
        ScanProgress? last = null;
        var progress = new SynchronousProgress(p => last = p);

        await _scanner.ScanAsync(_root, progress);

        Assert.NotNull(last);
        Assert.Equal(2, last.Value.Files);
        Assert.Equal(1, last.Value.Folders);
        Assert.Equal(20, last.Value.Bytes);
    }

    [Fact]
    public async Task Honors_cancellation()
    {
        WriteFile("a/b.bin", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.ScanAsync(_root, null, cts.Token));
    }

    private sealed class SynchronousProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        private readonly object _gate = new();
        public void Report(ScanProgress value) { lock (_gate) report(value); }
    }

    [Fact]
    public async Task Follows_links_to_outside_folders_when_enabled()
    {
        var outside = Directory.CreateTempSubdirectory("fds-target-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(outside, "data.bin"), new byte[700]);
            WriteFile("local.bin", 100);
            CreateFolderLink(Path.Combine(_root, "out"), outside);
            CreateFolderLink(Path.Combine(_root, "self"), _root); // would loop if followed

            var scanner = new FileSystemScanner(new ClusterRoundingAllocationProvider(4096)) { FollowReparsePoints = true };
            var root = await scanner.ScanAsync(_root);

            Assert.Equal(800, root.Size);
            Assert.Equal(700, root.Children.Single(c => c.Name == "out").Size);
            Assert.Empty(root.Children.Single(c => c.Name == "self").Children);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
