namespace FolderDiskSize.Core.Scanning;

public interface IDiskScanner
{
    /// <summary>Scans <paramref name="path"/> recursively and returns the root of the result tree.</summary>
    Task<FsNode> ScanAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

public readonly record struct ScanProgress(long Files, long Folders, long Bytes, string CurrentPath, TimeSpan Elapsed);
