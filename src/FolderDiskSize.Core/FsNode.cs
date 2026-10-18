namespace FolderDiskSize.Core;

/// <summary>
/// One file or folder in a scan result. Folder totals (sizes and counts) include everything beneath them.
/// </summary>
public sealed class FsNode
{
    private static readonly IReadOnlyList<FsNode> NoChildren = [];
    private List<FsNode>? _children;

    public FsNode(string name, string fullPath, bool isDirectory, FsNode? parent)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Parent = parent;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public FsNode? Parent { get; internal set; }

    /// <summary>Logical size in bytes (sum of file lengths for folders).</summary>
    public long Size { get; internal set; }

    /// <summary>Bytes actually allocated on disk (cluster rounding, compression, sparse files).</summary>
    public long Allocated { get; internal set; }

    /// <summary>Files beneath this folder, recursively. Zero for files.</summary>
    public long FileCount { get; internal set; }

    /// <summary>Folders beneath this folder, recursively, not counting itself. Zero for files.</summary>
    public long FolderCount { get; internal set; }

    public DateTime LastWriteTime { get; internal set; }
    public FileAttributes Attributes { get; internal set; }

    /// <summary>The folder could not be listed (permissions, locked, removed during scan).</summary>
    public bool AccessDenied { get; internal set; }

    /// <summary>A junction or symbolic link. Not followed, so its target is not counted twice.</summary>
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    public IReadOnlyList<FsNode> Children => _children ?? NoChildren;

    /// <summary>Share of the parent folder's size, 0–100.</summary>
    public double PercentOfParent =>
        Parent is { Size: > 0 } p ? Size * 100.0 / p.Size : 0;

    internal void SetChildren(List<FsNode> children) => _children = children;

    /// <summary>Finds a descendant (or this node) by full path, case-insensitively.</summary>
    public FsNode? Find(string path)
    {
        var target = PathUtil.Normalize(path);
        var current = this;
        while (true)
        {
            var here = PathUtil.Normalize(current.FullPath);
            if (string.Equals(here, target, StringComparison.OrdinalIgnoreCase))
                return current;
            if (!PathUtil.IsUnder(target, here))
                return null;

            FsNode? next = null;
            foreach (var child in current.Children)
            {
                if (!child.IsDirectory) continue;
                var c = PathUtil.Normalize(child.FullPath);
                if (string.Equals(c, target, StringComparison.OrdinalIgnoreCase) || PathUtil.IsUnder(target, c))
                {
                    next = child;
                    break;
                }
            }
            if (next is null) return null;
            current = next;
        }
    }
}
