using FolderDiskSize.Core;
using FolderDiskSize.Core.Formatting;

namespace FolderDiskSize.App.ViewModels;

/// <summary>One row of the Details grid. Text columns are precomputed so scrolling stays cheap.</summary>
public sealed class EntryViewModel
{
    public EntryViewModel(FsNode node)
    {
        Node = node;
        SizeText = SizeFormatter.Format(node.Size);
        AllocatedText = SizeFormatter.Format(node.Allocated);
        FileCountText = node.IsDirectory ? node.FileCount.ToString("N0") : "";
        FolderCountText = node.IsDirectory ? node.FolderCount.ToString("N0") : "";
        PercentText = Percent.ToString("0.00") + "%";
        AttributesText = AttributeFormatter.Format(node.Attributes);
        TypeName = FileTypeNames.For(node);
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
    public string TypeName { get; }

    /// <summary>Segoe Fluent / MDL2 glyph: folder, link, or page.</summary>
    public string Glyph => Node.IsReparsePoint ? "" : Node.IsDirectory ? "" : "";
}
