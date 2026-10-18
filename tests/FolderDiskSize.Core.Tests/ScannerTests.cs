using FolderDiskSize.Core;
using FolderDiskSize.Core.Scanning;

namespace FolderDiskSize.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fds-test-").FullName;
    private readonly FileSystemScanner _scanner = new(new ClusterRoundingAllocationProvider(4096));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteFile(string relative, int length)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
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
    public async Task Does_not_follow_symlinked_folders()
    {
        WriteFile("real/file.bin", 1000);
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), Path.Combine(_root, "real"));

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
}
