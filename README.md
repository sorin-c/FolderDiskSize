# Folder Disk Size

A Windows disk usage explorer modeled on FolderSizes. This first cut is the **explorer-like Details view**.

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

Needs the .NET 10 SDK. Open `FolderDiskSize.slnx` in Visual Studio 2026 or Rider to debug.

## How the view works

- **Folder Browser**: "This PC" lists the ready drives. Folders load from disk as you expand them. Selecting one scans it (the first time) and fills the grid.
- **Details grid**: Name, Size, Allocated, Files, Folders, Modified, % Parent (bar), Attribs, Type. Click a header to sort; the sort sticks as you navigate. Double-click a folder to open it. Folders that couldn't be read show in red.
- **Navigation**: back, forward, up, an editable address bar (press Enter), rescan and stop.
- **Results are cached**: scanning `C:\` once lets you browse every folder under it without rescanning. A rescan of a subfolder replaces just that part.
- **Info panel**: the selected row (or the current folder): counts, size and allocated, created/modified/accessed, attributes, % of parent.
- **Status bar**: current activity, folders and files scanned, elapsed time, memory.

## Scanner

`FileSystemScanner` walks folders with `DirectoryInfo.EnumerateFileSystemInfos`, scanning the top two levels in parallel. It lists junctions and symlinks but doesn't follow them, so nothing is counted twice. On Windows, `WindowsAllocationSizeProvider` computes allocated size from the volume cluster size, and uses `GetCompressedFileSizeW` for compressed or sparse files.

A faster NTFS MFT reader (`FSCTL_ENUM_USN_DATA`, admin only) can be added later as another `IDiskScanner` without touching the UI.

## Not done yet

- Ribbon, and the Computer / Bar Graph / Pie Graph / Folder Map tabs.
- Real shell icons and type names (`SHGetFileInfo`); the grid uses font glyphs and "EXT File" for now.
- Showing results while a scan is still running; the grid fills when the scan finishes.
- Keeping the tree selection in sync when you navigate from the grid or address bar.
- Owner column, filters, exports.
