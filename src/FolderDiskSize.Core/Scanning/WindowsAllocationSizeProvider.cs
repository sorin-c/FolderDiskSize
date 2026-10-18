using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FolderDiskSize.Core.Scanning;

/// <summary>
/// Uses the volume's cluster size, and GetCompressedFileSizeW for compressed or sparse files,
/// to match what Explorer shows as "Size on disk".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAllocationSizeProvider : IAllocationSizeProvider
{
    private const uint InvalidFileSize = 0xFFFFFFFF;
    private readonly ConcurrentDictionary<string, long> _clusterSizes = new(StringComparer.OrdinalIgnoreCase);

    public long GetAllocatedSize(FileInfo file)
    {
        var cluster = GetClusterSize(file.FullName);
        if ((file.Attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) != 0)
        {
            var low = GetCompressedFileSizeW(file.FullName, out var high);
            if (low != InvalidFileSize || Marshal.GetLastWin32Error() == 0)
                return ClusterRoundingAllocationProvider.RoundUp(((long)high << 32) | low, cluster);
        }
        return ClusterRoundingAllocationProvider.RoundUp(file.Length, cluster);
    }

    private long GetClusterSize(string path)
    {
        var root = Path.GetPathRoot(path) ?? path;
        return _clusterSizes.GetOrAdd(root, static r =>
            GetDiskFreeSpaceW(r, out var sectorsPerCluster, out var bytesPerSector, out _, out _)
                ? (long)sectorsPerCluster * bytesPerSector
                : 4096);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(string lpRootPathName, out uint lpSectorsPerCluster,
        out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);
}
