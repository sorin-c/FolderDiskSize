using System.Diagnostics;

namespace FolderDiskSize.Core.Scanning;

/// <summary>
/// Portable recursive scanner built on .NET directory enumeration. Top-level subfolders are scanned
/// in parallel. Junctions and symlinks are listed but not followed.
/// A faster NTFS MFT reader can later be added as another <see cref="IDiskScanner"/>.
/// </summary>
public sealed class FileSystemScanner(IAllocationSizeProvider allocation) : IDiskScanner
{
    private const int ParallelDepth = 2;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public FileSystemScanner() : this(AllocationSizeProvider.CreateDefault()) { }

    /// <summary>
    /// Also scan inside junctions and symbolic links. Only links that point outside the folder being
    /// scanned are followed, and each target once, so nothing is counted twice and loops can't happen.
    /// </summary>
    public bool FollowReparsePoints { get; set; }

    public Task<FsNode> ScanAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(path, progress, cancellationToken), cancellationToken);

    private FsNode Scan(string path, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var info = new DirectoryInfo(PathUtil.Normalize(path));
        if (!info.Exists) throw new DirectoryNotFoundException(path);

        var state = new ScanState(ct, PathUtil.Normalize(info.FullName));
        var root = new FsNode(DisplayName(info), info.FullName, isDirectory: true, parent: null)
        {
            LastWriteTime = info.LastWriteTime,
            Attributes = info.Attributes,
        };

        using var timer = progress is null ? null : new Timer(_ => progress.Report(state.Snapshot()), null, 0, 200);
        ScanDirectory(root, state, depth: 0);
        progress?.Report(state.Snapshot());
        return root;
    }

    private void ScanDirectory(FsNode node, ScanState state, int depth)
    {
        state.Token.ThrowIfCancellationRequested();
        state.CurrentPath = node.FullPath;

        var children = new List<FsNode>();
        var subdirs = new List<FsNode>();
        try
        {
            foreach (var entry in new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", Options))
            {
                if (entry is DirectoryInfo dir)
                {
                    var child = new FsNode(dir.Name, dir.FullName, isDirectory: true, node)
                    {
                        LastWriteTime = dir.LastWriteTime,
                        Attributes = dir.Attributes,
                    };
                    children.Add(child);
                    if (!child.IsReparsePoint || ShouldFollow(dir, state)) subdirs.Add(child);
                    Interlocked.Increment(ref state.Folders);
                }
                else if (entry is FileInfo file)
                {
                    var child = new FsNode(file.Name, file.FullName, isDirectory: false, node)
                    {
                        Size = file.Length,
                        Allocated = SafeAllocated(file),
                        LastWriteTime = file.LastWriteTime,
                        Attributes = file.Attributes,
                    };
                    children.Add(child);
                    Interlocked.Increment(ref state.Files);
                    Interlocked.Add(ref state.Bytes, child.Size);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.AccessDenied = true;
        }

        if (depth < ParallelDepth && subdirs.Count > 1)
        {
            Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = state.Token },
                d => ScanDirectory(d, state, depth + 1));
        }
        else
        {
            foreach (var d in subdirs) ScanDirectory(d, state, depth + 1);
        }

        long size = 0, allocated = 0, files = 0, folders = 0;
        foreach (var c in children)
        {
            size += c.Size;
            allocated += c.Allocated;
            if (c.IsDirectory)
            {
                files += c.FileCount;
                folders += c.FolderCount + 1;
            }
            else
            {
                files++;
            }
        }
        node.Size = size;
        node.Allocated = allocated;
        node.FileCount = files;
        node.FolderCount = folders;
        node.SetChildren(children);
    }

    private bool ShouldFollow(DirectoryInfo link, ScanState state)
    {
        if (!FollowReparsePoints) return false;
        try
        {
            if (link.ResolveLinkTarget(returnFinalTarget: true) is not DirectoryInfo { Exists: true } target) return false;
            var path = PathUtil.Normalize(target.FullName);
            var insideScan = string.Equals(path, state.Root, StringComparison.OrdinalIgnoreCase) || PathUtil.IsUnder(path, state.Root)
                             || PathUtil.IsUnder(state.Root, path);
            return !insideScan && state.FollowedTargets.TryAdd(path, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private long SafeAllocated(FileInfo file)
    {
        try { return allocation.GetAllocatedSize(file); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return file.Length; }
    }

    private static string DisplayName(DirectoryInfo info) =>
        info.Parent is null ? info.FullName : info.Name;

    private sealed class ScanState(CancellationToken token, string root)
    {
        public readonly string Root = root;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> FollowedTargets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public readonly CancellationToken Token = token;
        public long Files;
        public long Folders;
        public long Bytes;
        public volatile string CurrentPath = "";

        public ScanProgress Snapshot() =>
            new(Interlocked.Read(ref Files), Interlocked.Read(ref Folders), Interlocked.Read(ref Bytes), CurrentPath, _clock.Elapsed);
    }
}
