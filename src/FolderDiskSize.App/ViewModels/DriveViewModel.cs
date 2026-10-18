using System.IO;
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
    }

    public string Name { get; }
    public string RootPath { get; }
    public string Label { get; }
    public string Format { get; }
    public long Total { get; }
    public long Free { get; }
    public double UsedPercent { get; }

    public string TotalText => SizeFormatter.Format(Total);
    public string FreeText => SizeFormatter.Format(Free);
    public string UsedPercentText => UsedPercent.ToString("0") + "%";
    public string FormatLetter => Format.Length > 0 ? Format[..1] : "";
    public string TreeName => $"{Label} ({Name})";

    private static string DriveTypeName(DriveType type) => type switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "Removable Disk",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "CD Drive",
        _ => "Drive",
    };
}
