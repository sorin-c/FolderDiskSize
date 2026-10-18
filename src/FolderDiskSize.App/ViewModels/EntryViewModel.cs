using System.Windows.Media;
using FolderDiskSize.App.Services;
using FolderDiskSize.Core;
using FolderDiskSize.Core.Formatting;

namespace FolderDiskSize.App.ViewModels;

/// <summary>One row of the Details grid. Text columns are precomputed so scrolling stays cheap.</summary>
public sealed class EntryViewModel
{
    private string? _typeName;
    private ImageSource? _icon;

    public EntryViewModel(FsNode node)
    {
        Node = node;
        SizeText = SizeFormatter.Format(node.Size);
        AllocatedText = SizeFormatter.Format(node.Allocated);
        FileCountText = node.IsDirectory ? node.FileCount.ToString("N0") : "";
        FolderCountText = node.IsDirectory ? node.FolderCount.ToString("N0") : "";
        PercentText = Percent.ToString("0.00") + "%";
        AttributesText = AttributeFormatter.Format(node.Attributes);
    }

    public FsNode Node { get; }
    public string Name => Node.Name;
    public bool IsDirectory => Node.IsDirectory;
    public bool AccessDenied => Node.AccessDenied;
    public long Size => Node.Size;
    public long Allocated => Node.Allocated;
    public long FileCount => Node.FileCount;
    public long FolderCount => Node.FolderCount;
    public DateTime Modified => Node.LastWriteTime;
    public double Percent => Node.PercentOfParent;

    public string SizeText { get; }
    public string AllocatedText { get; }
    public string FileCountText { get; }
    public string FolderCountText { get; }
    public string PercentText { get; }
    public string AttributesText { get; }

    /// <summary>Explorer's type name ("Text Document"), falling back to "TXT File" if the shell has none.</summary>
    public string TypeName => _typeName ??= ShellIcons.TypeName(Node.Name, Node.IsDirectory) ?? FileTypeNames.For(Node);

    /// <summary>The 16px Windows shell icon, loaded when the row first scrolls into view.</summary>
    public ImageSource? Icon => _icon ??= Node.IsDirectory ? ShellIcons.ForFolder(Node.IsReparsePoint) : ShellIcons.ForFile(Node.FullPath);
}
