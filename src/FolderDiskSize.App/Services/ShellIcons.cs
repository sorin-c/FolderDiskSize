using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FolderDiskSize.App.Services;

/// <summary>
/// Small (16px) icons and type names from the Windows shell, the same ones Explorer shows.
/// Lookups are cached by extension, so a folder of 100k ".dll" files costs one shell call.
/// Call from the UI thread (the shell needs an STA thread).
/// </summary>
public static class ShellIcons
{
    // Files whose icon is stored in the file itself, so the extension alone isn't enough.
    private static readonly HashSet<string> PerFileIconExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico", ".lnk", ".url", ".cur", ".ani", ".msc", ".cpl", ".scr", ".appref-ms" };

    private static readonly ConcurrentDictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (PerFileIconExtensions.Contains(ext))
            return Icons.GetOrAdd("path:" + path, _ => Load(path, FileAttributeNormal, useAttributes: false, Shgfi.LinkOverlay)
                                                      ?? ForExtension(ext));
        return ForExtension(ext);
    }

    public static ImageSource? ForFolder(bool isLink = false) =>
        Icons.GetOrAdd(isLink ? "folder:link" : "folder", _ =>
            Load("folder", FileAttributeDirectory, useAttributes: true, isLink ? Shgfi.LinkOverlay : 0));

    /// <summary>A drive or other special location, looked up by its real path (e.g. "C:\").</summary>
    public static ImageSource? ForDrive(string root) =>
        Icons.GetOrAdd("drive:" + root, _ => Load(root, FileAttributeDirectory, useAttributes: false, 0) ?? ForFolder());

    /// <summary>The 32x32 version of <see cref="ForDrive"/>, for the Computer tab.</summary>
    public static ImageSource? ForDriveLarge(string root) =>
        Icons.GetOrAdd("drive-large:" + root, _ => Load(root, FileAttributeDirectory, useAttributes: false, 0, large: true));

    public static ImageSource? ForThisPc() =>
        Icons.GetOrAdd("thispc", _ =>
        {
            if (SHGetSpecialFolderLocation(IntPtr.Zero, CsidlDrives, out var pidl) != 0) return null;
            try { return LoadPidl(pidl); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        });

    /// <summary>Explorer's type name, e.g. "Text Document" or "Application". Null if the shell has none.</summary>
    public static string? TypeName(string path, bool isDirectory)
    {
        var key = isDirectory ? "<dir>" : Path.GetExtension(path);
        var name = TypeNames.GetOrAdd(key, _ =>
        {
            var info = new SHFILEINFO();
            var attrs = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
            var probe = isDirectory ? "folder" : "file" + key;
            return SHGetFileInfoW(probe, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), Shgfi.TypeName | Shgfi.UseFileAttributes) != IntPtr.Zero
                ? info.szTypeName
                : "";
        });
        return name.Length > 0 ? name : null;
    }

    private static ImageSource? ForExtension(string ext) =>
        Icons.GetOrAdd("ext:" + ext, _ => Load("file" + ext, FileAttributeNormal, useAttributes: true, 0));

    private static ImageSource? Load(string path, uint attributes, bool useAttributes, uint extraFlags, bool large = false)
    {
        var info = new SHFILEINFO();
        var flags = Shgfi.Icon | (large ? 0 : Shgfi.SmallIcon) | extraFlags | (useAttributes ? Shgfi.UseFileAttributes : 0);
        if (SHGetFileInfoW(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero)
            return null;
        return ToImage(info.hIcon);
    }

    private static ImageSource? LoadPidl(IntPtr pidl)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), Shgfi.Icon | Shgfi.SmallIcon | Shgfi.Pidl) == IntPtr.Zero)
            return null;
        return ToImage(info.hIcon);
    }

    private static ImageSource? ToImage(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const int CsidlDrives = 0x11;

    private static class Shgfi
    {
        public const uint SmallIcon = 0x1;
        public const uint Pidl = 0x8;
        public const uint UseFileAttributes = 0x10;
        public const uint Icon = 0x100;
        public const uint TypeName = 0x400;
        public const uint LinkOverlay = 0x8000;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetSpecialFolderLocation(IntPtr hwndOwner, int nFolder, out IntPtr ppidl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
