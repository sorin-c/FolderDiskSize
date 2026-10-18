using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace FolderDiskSize.App.Services;

/// <summary>Explorer actions for a file or folder: show it in Explorer, copy its path, open its Properties dialog.</summary>
public static class ShellActions
{
    /// <summary>Opens an Explorer window on the item's folder with the item selected.</summary>
    public static string? ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    /// <summary>Puts the paths on the clipboard, one per line.</summary>
    public static string? CopyPaths(IEnumerable<string> paths)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            return null;
        }
        catch (COMException ex)
        {
            // Another app has the clipboard open.
            return ex.Message;
        }
    }

    /// <summary>Shows the Windows Properties dialog for the item, the same one Explorer opens.</summary>
    public static bool ShowProperties(Window owner, string path)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        return SHObjectProperties(hwnd, SHOP_FILEPATH, path, null);
    }

    /// <summary>
    /// Deletes the items the way Explorer does: to the Recycle Bin unless <paramref name="permanently"/>,
    /// with Windows' own confirmation and progress dialogs. Returns false if it failed or was cancelled.
    /// </summary>
    public static bool Delete(Window owner, IReadOnlyCollection<string> paths, bool permanently)
    {
        if (paths.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle,
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', paths) + "\0\0", // a list ended by two nulls
            // Without FOF_NOCONFIRMATION, Windows decides whether to ask, following the Recycle Bin's
            // "Display delete confirmation dialog" setting; a permanent delete always asks.
            fFlags = permanently ? (ushort)0 : (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING),
        };
        return SHFileOperationW(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    private const uint FO_DELETE = 0x3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_WANTNUKEWARNING = 0x4000; // warn if an item is too big for the Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    private const uint SHOP_FILEPATH = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);
}
