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

## Make a release

1. Set the version in `Directory.Build.props` (`<Version>`), for example `0.2.0`.
2. Run `.\publish.ps1` from the repo root in PowerShell. It runs the tests, then publishes with the `win-x64` profile (`src/FolderDiskSize.App/Properties/PublishProfiles/win-x64.pubxml`): one self-contained `FolderDiskSize.exe` that runs without .NET installed.
3. Output goes to `artifacts\` (ignored by git): the `FolderDiskSize-<version>-win-x64` folder with the exe, a zip of it, and the zip's SHA-256.
4. Start the exe from that folder once to check it, then commit, tag (`git tag v0.2.0`) and attach the zip to a GitHub release.

`.\publish.ps1 -Version 0.2.0` overrides the version for one build; `-SkipTests` skips the tests. The exe isn't code-signed yet, so SmartScreen will warn on other PCs.

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
