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

    /// <summary>
    /// Swaps a child for a fresh scan of it and updates the totals of this folder and every folder above it.
    /// Returns false if <paramref name="old"/> is not one of this folder's children.
    /// </summary>
    public bool ReplaceChild(FsNode old, FsNode replacement)
    {
        var index = _children?.IndexOf(old) ?? -1;
        if (index < 0) return false;
        _children![index] = replacement;
        replacement.Parent = this;
        old.Parent = null;

        var size = replacement.Size - old.Size;
        var allocated = replacement.Allocated - old.Allocated;
        var files = replacement.FileCount - old.FileCount;
        var folders = replacement.FolderCount - old.FolderCount;
        for (var node = this; node is not null; node = node.Parent)
        {
            node.Size += size;
            node.Allocated += allocated;
            node.FileCount += files;
            node.FolderCount += folders;
        }
        return true;
    }

    /// <summary>
    /// Takes a deleted child out of this folder and subtracts it from the totals of this folder and every folder above it.
    /// Returns false if <paramref name="child"/> is not one of this folder's children.
    /// </summary>
    public bool RemoveChild(FsNode child)
    {
        if (_children is null || !_children.Remove(child)) return false;
        child.Parent = null;

        var files = child.IsDirectory ? child.FileCount : 1;
        var folders = child.IsDirectory ? child.FolderCount + 1 : 0;
        for (var node = this; node is not null; node = node.Parent)
        {
            node.Size -= child.Size;
            node.Allocated -= child.Allocated;
            node.FileCount -= files;
            node.FolderCount -= folders;
        }
        return true;
    }

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
