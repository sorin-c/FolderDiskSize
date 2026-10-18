# Folder Disk Size

A Windows disk usage explorer modeled on FolderSizes. This first cut is the **explorer-like Details view**.

## Download

Each release on GitHub has two zips for 64-bit Windows. Unzip either one and run `FolderDiskSize.exe`; there is no installer.

| Zip | Needs .NET? | Size | Pick it if |
|---|---|---|---|
| `FolderDiskSize-<version>-win-x64-standalone.zip` | No. .NET is built in. | About 55 MB | You just want it to run on any 64-bit Windows 10 or 11 PC. |
| `FolderDiskSize-<version>-win-x64-requires-dotnet.zip` | Yes: the **.NET 10 Desktop Runtime (x64)** | About 0.5 MB | You already have .NET 10 installed, or don't mind installing it once. |

To get the runtime for the smaller zip, open the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0) and, under **.NET Desktop Runtime 10.0**, download the **Windows x64** installer. The plain ".NET Runtime" and "ASP.NET Core Runtime" don't include WPF, so the app won't start with only those. If the runtime is missing, Windows shows a message with a download link when you start the app.

Each zip has a `.sha256` file next to it with its SHA-256 checksum.

## What's here

| Project | Target | What it does |
|---|---|---|
| `src/FolderDiskSize.Core` | `net10.0` | Scanner behind `IDiskScanner`, the `FsNode` result tree, size/attribute/type formatting. No UI, runs anywhere. |
| `src/FolderDiskSize.App` | `net10.0-windows` (WPF) | Main window: Folder Browser tree, Drive Space panel, sortable Details grid, info panel, status bar. Panels dock via AvalonDock. |
| `tests/FolderDiskSize.Core.Tests` | `net10.0` | xUnit tests for the scanner and formatters. |

## Run it (Windows)

```powershell
dotnet run --project src/FolderDiskSize.App
```

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), which includes the runtime. Open `FolderDiskSize.slnx` in Visual Studio 2026 or Rider to debug.

## How the view works

- **Folder Browser**: "This PC" lists the ready drives. Folders load from disk as you expand them. Selecting one scans it (the first time) and fills the grid.
- **This PC** (shown at start, or by selecting "This PC"): the main view shows a tile per drive with its used-space bar, free/total, file system, bus and whether it's NVMe, SSD or HDD, plus the computer's name, domain, processors, memory and user. Click a tile (or select it and press Enter) to open the drive in the details grid, which replaces the tiles in the same view (there are no tabs). "This PC" is part of the history, so Back from a drive returns to it, and Up from a drive's root goes there too.
- **Details grid**: Name, Size, Allocated, Files, Folders, Modified, % Parent (bar), Attribs, Type. Click a header to sort; the sort sticks as you navigate. Double-click a folder to open it. Icons and type names come from Windows, as in Explorer. Folders that couldn't be read show in red. Right-click any column header to choose which columns to show (Name always stays); the choice is saved in `%LocalAppData%\FolderDiskSize\settings.json`.
- **Navigation**: back, forward, up, an editable address bar (press Enter), rescan and stop.
- **Results are cached**: scanning `C:\` once lets you browse every folder under it without rescanning. A rescan of a subfolder replaces just that part.
- **Info panel**: the selected row (or the current folder): counts, size and allocated, created/modified/accessed, attributes, % of parent.
- **Status bar**: current activity, folders and files scanned, elapsed time, memory.

## Scanner

`FileSystemScanner` walks folders with `DirectoryInfo.EnumerateFileSystemInfos`, scanning the top two levels in parallel. It lists junctions and symlinks but doesn't follow them, so nothing is counted twice. On Windows, `WindowsAllocationSizeProvider` computes allocated size from the volume cluster size, and uses `GetCompressedFileSizeW` for compressed or sparse files.

A faster NTFS MFT reader (`FSCTL_ENUM_USN_DATA`, admin only) can be added later as another `IDiskScanner` without touching the UI.

## Not done yet

- Ribbon, and the Bar Graph / Pie Graph / Folder Map tabs.
- Showing results while a scan is still running; the grid fills when the scan finishes.
- Keeping the tree selection in sync when you navigate from the grid or address bar.
- Owner column, filters, exports.
