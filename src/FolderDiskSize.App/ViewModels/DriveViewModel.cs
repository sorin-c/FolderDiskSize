using System.IO;
using System.Windows.Media;
using FolderDiskSize.App.Services;
using FolderDiskSize.Core.Formatting;

namespace FolderDiskSize.App.ViewModels;

/// <summary>One row of the Drive Space panel.</summary>
public sealed class DriveViewModel
{
    public DriveViewModel(DriveInfo drive)
    {
        Name = drive.Name.TrimEnd('\\');
        RootPath = drive.Name;
        Total = drive.TotalSize;
        Free = drive.TotalFreeSpace;
        UsedPercent = Total > 0 ? (Total - Free) * 100.0 / Total : 0;
        Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveTypeName(drive.DriveType) : drive.VolumeLabel;
        Format = drive.DriveFormat;
        (Bus, var seekPenalty) = DriveHardware.Query(RootPath);
        Kind = DriveHardware.Kind(Bus, seekPenalty);
    }

    public string Name { get; }
    public string RootPath { get; }
    public string Label { get; }
    public string Format { get; }
    public long Total { get; }
    public long Free { get; }
    public double UsedPercent { get; }
    /// <summary>The disk's bus, e.g. "NVMe", "SATA" or "USB"; null if Windows didn't say.</summary>
    public string? Bus { get; }
    /// <summary>"NVMe", "SSD" or "HDD"; null if unknown.</summary>
    public string? Kind { get; }

    public ImageSource? Icon => ShellIcons.ForDrive(RootPath);
    public string TotalText => SizeFormatter.Format(Total);
    public string FreeText => SizeFormatter.Format(Free);
    public string UsedPercentText => UsedPercent.ToString("0") + "%";
    public string TreeName => $"{Label} ({Name})";

    // Computer tab tiles.
    public ImageSource? LargeIcon => ShellIcons.ForDriveLarge(RootPath);
    public string TileTitle => Kind is null ? TreeName : $"{Label} ({Name}) · {Kind}";
    public string FreeOfTotalText => $"{FreeText} free of {TotalText}";
    public string FileSystemText => Bus is null ? $"File system: {Format}" : $"File system: {Format} ({Bus} bus)";
    public bool IsAlmostFull => UsedPercent >= 90;

    private static string DriveTypeName(DriveType type) => type switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "Removable Disk",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "CD Drive",
        _ => "Drive",
    };
}
