namespace FolderDiskSize.Core.Formatting;

/// <summary>
/// Extension-based type names ("PNG File"). The Windows app can swap in shell names from SHGetFileInfo later.
/// </summary>
public static class FileTypeNames
{
    public const string Folder = "File folder";

    public static string For(FsNode node)
    {
        if (node.IsDirectory) return Folder;
        var ext = Path.GetExtension(node.Name);
        return string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " File";
    }
}
