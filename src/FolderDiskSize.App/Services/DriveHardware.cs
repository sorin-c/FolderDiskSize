using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FolderDiskSize.App.Services;

/// <summary>
/// Asks Windows what kind of disk a drive letter sits on: its bus (NVMe, SATA, USB…) and whether it
/// is a spinning disk. Works without admin rights; anything it can't find out comes back null.
/// </summary>
public static class DriveHardware
{
    /// <summary>"NVMe", "SSD" or "HDD", or null if Windows won't say.</summary>
    public static string? Kind(string? bus, bool? seekPenalty) =>
        bus == "NVMe" ? "NVMe" : seekPenalty switch { false => "SSD", true => "HDD", null => null };

    /// <summary>Reads the bus name and seek penalty for a drive root such as "C:\".</summary>
    public static (string? Bus, bool? SeekPenalty) Query(string root)
    {
        if (!OperatingSystem.IsWindows() || root.Length < 2 || root[1] != ':') return (null, null);
        using var handle = CreateFileW($@"\\.\{root[..2]}", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return (null, null);
        return (QueryBus(handle), QuerySeekPenalty(handle));
    }

    private static string? QueryBus(SafeFileHandle handle)
    {
        // STORAGE_DEVICE_DESCRIPTOR: BusType is the 4-byte field at offset 28.
        var buffer = new byte[1024];
        if (!Query(handle, StorageDeviceProperty, buffer)) return null;
        return BitConverter.ToInt32(buffer, 28) switch
        {
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "FireWire",
            6 => "Fibre Channel",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 or 15 => "Virtual",
            16 => "Storage Spaces",
            17 => "NVMe",
            19 => "UFS",
            _ => null,
        };
    }

    private static bool? QuerySeekPenalty(SafeFileHandle handle)
    {
        // DEVICE_SEEK_PENALTY_DESCRIPTOR: IncursSeekPenalty is the byte at offset 8.
        var buffer = new byte[12];
        return Query(handle, StorageDeviceSeekPenaltyProperty, buffer) ? buffer[8] != 0 : null;
    }

    private static bool Query(SafeFileHandle handle, int propertyId, byte[] output)
    {
        // STORAGE_PROPERTY_QUERY { PropertyId, QueryType = PropertyStandardQuery, AdditionalParameters[1] }
        var query = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        return DeviceIoControl(handle, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out var returned, IntPtr.Zero)
               && returned > 0;
    }

    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize,
        out int returned, IntPtr overlapped);
}
