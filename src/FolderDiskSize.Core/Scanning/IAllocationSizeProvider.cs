namespace FolderDiskSize.Core.Scanning;

/// <summary>Works out how many bytes a file occupies on disk, as opposed to its logical length.</summary>
public interface IAllocationSizeProvider
{
    long GetAllocatedSize(FileInfo file);
}

/// <summary>Rounds every file up to a fixed cluster size. Used off Windows and in tests.</summary>
public sealed class ClusterRoundingAllocationProvider(long clusterSize = 4096) : IAllocationSizeProvider
{
    public long GetAllocatedSize(FileInfo file) => RoundUp(file.Length, clusterSize);

    internal static long RoundUp(long length, long cluster) =>
        cluster <= 0 ? length : (length + cluster - 1) / cluster * cluster;
}

public static class AllocationSizeProvider
{
    public static IAllocationSizeProvider CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsAllocationSizeProvider() : new ClusterRoundingAllocationProvider();
}
