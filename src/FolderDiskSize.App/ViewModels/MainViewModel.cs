using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDiskSize.Core;
using FolderDiskSize.Core.Formatting;
using FolderDiskSize.Core.Scanning;

namespace FolderDiskSize.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDiskScanner _scanner;

    // Newest first, so a rescan of a subfolder wins over an older scan of its drive.
    private readonly List<FsNode> _results = [];
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _scanCts;
    private int _navigation; // bumped on every navigation, so a slow scan can tell it has been superseded
    private FolderTreeItemViewModel? _selectedTreeItem;
    private bool _syncingTree;
    private string _sortMember = nameof(EntryViewModel.Size);
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    /// <summary>The Computer tab's place in the back/forward history and the address bar.</summary>
    public const string ThisPcPath = "This PC";

    public MainViewModel(IDiskScanner scanner)
    {
        _scanner = scanner;
        LoadDrives();
        Address = ThisPcPath;
    }

    public ObservableCollection<FolderTreeItemViewModel> TreeRoots { get; } = [];
    public ObservableCollection<DriveViewModel> Drives { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<EntryViewModel> _entries = [];

    [ObservableProperty]
    private string _address = "";

    [ObservableProperty]
    private FsNode? _currentNode;

    /// <summary>True while "This PC" (the Computer tab) is the current location rather than a folder.</summary>
    [ObservableProperty]
    private bool _isComputerView = true;

    /// <summary>Where back/forward/up start from: <see cref="ThisPcPath"/> or the current folder.</summary>
    private string? CurrentLocation => IsComputerView ? ThisPcPath : CurrentNode?.FullPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsNode))]
    private EntryViewModel? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isScanning;

    /// <summary>True while the grid is empty waiting for a folder to be scanned; the view shows a spinner.</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private long _scannedFolders;

    [ObservableProperty]
    private long _scannedFiles;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _memoryText = "";

    /// <summary>The item described in the info panel under the grid: the selected row, else the current folder.</summary>
    public FsNode? DetailsNode => IsLoading ? null : SelectedEntry?.Node ?? CurrentNode;

    public string DetailsName => DetailsNode?.Name ?? "";
    public string DetailsType => DetailsNode is { } n ? FileTypeNames.For(n) : "";
    public string DetailsCounts => DetailsNode is { IsDirectory: true } n ? $"{n.FolderCount:N0} Folders ({n.FileCount:N0} Files)" : "";
    public string DetailsSize => DetailsNode is { } n ? $"{SizeFormatter.Format(n.Size)} ({SizeFormatter.Format(n.Allocated)} allocated)" : "";
    public string DetailsModified => DetailsNode is { } n ? $"Modified: {n.LastWriteTime:g}" : "";
    public string DetailsCreated => DetailsNode is { } n ? $"Created: {SafeTime(n, i => i.CreationTime):g}" : "";
    public string DetailsAccessed => DetailsNode is { } n ? $"Accessed: {SafeTime(n, i => i.LastAccessTime):g}" : "";
    public string DetailsAttributes => DetailsNode is { } n ? $"Attributes: {AttributeFormatter.Format(n.Attributes)}" : "";
    public string DetailsPercent => DetailsNode is { } n ? $"Percentage: {n.PercentOfParent:0.00}%" : "";

    partial void OnCurrentNodeChanged(FsNode? value) => RefreshDetails();
    partial void OnSelectedEntryChanged(EntryViewModel? value) => RefreshDetails();
    partial void OnIsLoadingChanged(bool value) => RefreshDetails();

    private void RefreshDetails()
    {
        foreach (var name in new[] { nameof(DetailsNode), nameof(DetailsName), nameof(DetailsType), nameof(DetailsCounts), nameof(DetailsSize),
                     nameof(DetailsModified), nameof(DetailsCreated), nameof(DetailsAccessed), nameof(DetailsAttributes), nameof(DetailsPercent) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private Task NavigateAsync(string? path) => string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : GoToAsync(path, recordHistory: true);

    [RelayCommand]
    private Task OpenEntryAsync(EntryViewModel? entry)
    {
        switch (entry)
        {
            case { IsDirectory: true, Node.IsReparsePoint: false }:
                return GoToAsync(entry.Node.FullPath, recordHistory: true);
            case { IsDirectory: false }:
                OpenWithDefaultApp(entry.Node.FullPath);
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>Opens a file the way Explorer does, with whatever app Windows has associated with it.</summary>
    private void OpenWithDefaultApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            StatusText = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task GoUpAsync()
    {
        var parent = ParentPath();
        return parent is null ? Task.CompletedTask : GoToAsync(parent, recordHistory: true);
    }

    /// <summary>
    /// Returns to the previous folder, like Explorer's back button. With no history yet
    /// (for example right after picking a folder in the tree), goes to the parent folder instead.
    /// </summary>
    [RelayCommand]
    private Task GoBackAsync()
    {
        if (CurrentLocation is not { } current) return Task.CompletedTask;
        var target = _back.Count > 0 ? _back.Pop() : ParentPath();
        if (target is null) return Task.CompletedTask;
        _forward.Push(current);
        return GoToAsync(target, recordHistory: false);
    }

    [RelayCommand]
    private Task GoForwardAsync()
    {
        if (_forward.Count == 0 || CurrentLocation is not { } current) return Task.CompletedTask;
        _back.Push(current);
        return GoToAsync(_forward.Pop(), recordHistory: false);
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (CurrentNode is null || IsComputerView) return;
        var nav = ++_navigation;
        var current = CurrentNode;
        var result = await LoadAsync(current.FullPath);
        if (nav != _navigation) return;
        Show(result ?? current);
    }

    /// <summary>
    /// Rescans one folder from the grid and puts the fresh result in place, so the current
    /// folder's totals update without rescanning everything else.
    /// </summary>
    [RelayCommand]
    private async Task RescanEntryAsync(EntryViewModel? entry)
    {
        if (entry is not { IsDirectory: true, Node.IsReparsePoint: false } || entry.Node.Parent is not { } parent) return;
        var nav = _navigation;
        var fresh = await ScanAsync(entry.Node.FullPath);
        if (fresh is null || !parent.ReplaceChild(entry.Node, fresh)) return;
        if (nav != _navigation || CurrentNode != parent) return; // the user has moved on; the tree is still up to date
        Show(parent);
        SelectedEntry = Entries.FirstOrDefault(e => e.Node == fresh);
    }

    /// <summary>
    /// After the view has deleted items through Windows, takes the ones that are really gone out of the
    /// scan results, the grid and the folder tree, and selects the row that took the first one's place.
    /// </summary>
    public void RemoveDeleted(IReadOnlyList<EntryViewModel> entries)
    {
        var gone = entries.Where(e => !File.Exists(e.Node.FullPath) && !Directory.Exists(e.Node.FullPath)).ToList();
        if (gone.Count == 0) return;
        var rows = Entries.ToList();
        var firstIndex = gone.Select(e => rows.IndexOf(e)).Where(i => i >= 0).DefaultIfEmpty(0).Min();

        foreach (var entry in gone)
        {
            var path = PathUtil.Normalize(entry.Node.FullPath);
            entry.Node.Parent?.RemoveChild(entry.Node);
            _results.RemoveAll(r =>
            {
                var p = PathUtil.Normalize(r.FullPath);
                return string.Equals(p, path, StringComparison.OrdinalIgnoreCase) || PathUtil.IsUnder(p, path);
            });
            if (entry.IsDirectory) RemoveFromTree(TreeRoots, path);
        }

        if (CurrentNode is null || IsComputerView) return;
        Show(CurrentNode);
        SelectedEntry = Entries.Count == 0 ? null : Entries[Math.Min(firstIndex, Entries.Count - 1)];
        StatusText = gone.Count == 1 ? $"Deleted {gone[0].Name}" : $"Deleted {gone.Count:N0} items";
    }

    /// <summary>Removes a deleted folder from the Folder Browser, looking only at folders that have been expanded.</summary>
    private static bool RemoveFromTree(ObservableCollection<FolderTreeItemViewModel> items, string path)
    {
        foreach (var item in items)
        {
            if (item.FullPath is not null)
            {
                var p = PathUtil.Normalize(item.FullPath);
                if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                {
                    items.Remove(item);
                    return true;
                }
                if (!PathUtil.IsUnder(path, p)) continue;
            }
            if (RemoveFromTree(item.Children, path)) return true;
        }
        return false;
    }

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan() => _scanCts?.Cancel();

    /// <summary>Called by the view when a column header is clicked; sorting is done here so it survives navigation.</summary>
    public void SortBy(string member, ListSortDirection direction)
    {
        _sortMember = member;
        _sortDirection = direction;
        var selected = SelectedEntry;
        Entries = Sort(Entries);
        SelectedEntry = selected;
    }

    public (string Member, ListSortDirection Direction) CurrentSort => (_sortMember, _sortDirection);

    private async Task GoToAsync(string path, bool recordHistory)
    {
        if (string.Equals(path.Trim(), ThisPcPath, StringComparison.OrdinalIgnoreCase))
        {
            ShowComputer(recordHistory);
            return;
        }

        string normalized;
        try { normalized = PathUtil.Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusText = $"Invalid path: {path}";
            return;
        }

        var from = CurrentLocation;
        var nav = ++_navigation;
        var node = FindScanned(normalized);
        if (node is null)
        {
            if (!Directory.Exists(normalized))
            {
                StatusText = $"Folder not found: {normalized}";
                return;
            }
            Address = normalized;
            IsComputerView = false; // show the spinner in Details while it scans
            node = await LoadAsync(normalized);
            if (nav != _navigation) return; // the user went somewhere else while this was scanning
            if (node is null)
            {
                // Cancelled or failed: put the previous folder (or This PC) back.
                if (from == ThisPcPath) ShowComputer(recordHistory: false);
                else if (CurrentNode is not null) Show(CurrentNode);
                return;
            }
        }
        else
        {
            _scanCts?.Cancel(); // navigating away from a folder that was still loading
            IsLoading = false;
        }

        if (recordHistory && from is not null && !string.Equals(from, node.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(from);
            _forward.Clear();
        }
        Show(node);
    }

    /// <summary>Makes "This PC" the current location; the view shows the Computer tab.</summary>
    private void ShowComputer(bool recordHistory)
    {
        ++_navigation;
        _scanCts?.Cancel();
        IsLoading = false;
        if (recordHistory && CurrentLocation is { } current && current != ThisPcPath)
        {
            _back.Push(current);
            _forward.Clear();
        }
        IsComputerView = true;
        CurrentNode = null;
        SelectedEntry = null;
        Address = ThisPcPath;
        if (TreeRoots.FirstOrDefault() is { } thisPc) SelectTreeItem(thisPc);
    }

    /// <summary>Empties the grid and shows the spinner while <paramref name="path"/> is scanned.</summary>
    private async Task<FsNode?> LoadAsync(string path)
    {
        SelectedEntry = null;
        Entries = [];
        IsLoading = true;
        try
        {
            return await ScanAsync(path);
        }
        finally
        {
            if (_scanCts is null) IsLoading = false;
        }
    }

    /// <summary>List hidden and system items in the grid. They always count towards folder sizes.</summary>
    public bool ShowHiddenFiles { get; set; } = true;

    private bool IsVisible(FsNode node) =>
        ShowHiddenFiles || (node.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    /// <summary>Redraws the current folder after a display option changed, keeping the selection.</summary>
    public void RefreshCurrentView()
    {
        if (IsComputerView || CurrentNode is null) return;
        var selected = SelectedEntry?.Node;
        Show(CurrentNode);
        if (selected is not null)
            SelectedEntry = Entries.FirstOrDefault(e => ReferenceEquals(e.Node, selected));
    }

    private void Show(FsNode node)
    {
        var previous = CurrentNode is null || IsComputerView ? null : PathUtil.Normalize(CurrentNode.FullPath);
        IsComputerView = false;
        CurrentNode = node;
        Address = node.FullPath;
        SelectedEntry = null;
        Entries = Sort(node.Children.Where(IsVisible).Select(c => new EntryViewModel(c)));

        // Coming back out of a folder (back, up), select the folder we came from, like Explorer.
        if (previous is not null)
        {
            SelectedEntry = Entries.FirstOrDefault(e =>
            {
                var p = PathUtil.Normalize(e.Node.FullPath);
                return string.Equals(p, previous, StringComparison.OrdinalIgnoreCase) || PathUtil.IsUnder(previous, p);
            });
        }
        SelectInTree(node.FullPath);
    }

    /// <summary>
    /// Expands the Folder Browser down to <paramref name="path"/> and selects it, so the tree follows
    /// navigation from back/forward, the grid and the address bar.
    /// </summary>
    private void SelectInTree(string path)
    {
        var target = PathUtil.Normalize(path);
        FolderTreeItemViewModel? match = null;
        IEnumerable<FolderTreeItemViewModel> level = TreeRoots.SelectMany(r => r.Children);
        while (true)
        {
            FolderTreeItemViewModel? next = null;
            foreach (var item in level)
            {
                if (item.FullPath is null) continue;
                var p = PathUtil.Normalize(item.FullPath);
                if (string.Equals(p, target, StringComparison.OrdinalIgnoreCase) || PathUtil.IsUnder(target, p))
                {
                    next = item;
                    break;
                }
            }
            if (next is null) break;
            match = next;
            if (string.Equals(PathUtil.Normalize(next.FullPath!), target, StringComparison.OrdinalIgnoreCase)) break;
            next.IsExpanded = true; // loads its children
            level = next.Children;
        }

        // If the exact folder isn't in the tree (e.g. created after its parent was expanded), select the closest ancestor.
        if (match is not null) SelectTreeItem(match);
    }

    private void SelectTreeItem(FolderTreeItemViewModel match)
    {
        if (match == _selectedTreeItem) return;
        _syncingTree = true;
        try
        {
            if (_selectedTreeItem is not null) _selectedTreeItem.IsSelected = false;
            match.IsSelected = true;
            _selectedTreeItem = match;
        }
        finally
        {
            _syncingTree = false;
        }
    }

    private FsNode? FindScanned(string path)
    {
        foreach (var root in _results)
            if (root.Find(path) is { } hit) return hit;
        return null;
    }

    private async Task<FsNode?> ScanAsync(string path)
    {
        _scanCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _scanCts = cts;
        IsScanning = true;
        StatusText = $"Scanning {path}";
        var progress = new Progress<ScanProgress>(p =>
        {
            if (_scanCts != cts) return;
            ScannedFiles = p.Files;
            ScannedFolders = p.Folders;
            ElapsedText = FormatElapsed(p.Elapsed);
            StatusText = $"Scanning {p.CurrentPath}";
        });
        var clock = Stopwatch.StartNew();

        try
        {
            var result = await _scanner.ScanAsync(path, progress, cts.Token);
            _results.RemoveAll(r => PathUtil.IsUnder(PathUtil.Normalize(r.FullPath), PathUtil.Normalize(result.FullPath))
                                    || string.Equals(r.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase));
            _results.Insert(0, result);
            ScannedFiles = result.FileCount;
            ScannedFolders = result.FolderCount;
            ElapsedText = FormatElapsed(clock.Elapsed);
            StatusText = "File system analysis engine currently idle";
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
            return null;
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            return null;
        }
        finally
        {
            if (_scanCts == cts)
            {
                _scanCts = null;
                IsScanning = false;
            }
            MemoryText = SizeFormatter.Format(Environment.WorkingSet);
        }
    }

    private IReadOnlyList<EntryViewModel> Sort(IEnumerable<EntryViewModel> items)
    {
        Func<EntryViewModel, object> key = _sortMember switch
        {
            nameof(EntryViewModel.Name) => e => e.Name,
            nameof(EntryViewModel.Allocated) => e => e.Allocated,
            nameof(EntryViewModel.FileCount) => e => e.FileCount,
            nameof(EntryViewModel.FolderCount) => e => e.FolderCount,
            nameof(EntryViewModel.Modified) => e => e.Modified,
            nameof(EntryViewModel.Percent) => e => e.Percent,
            nameof(EntryViewModel.AttributesText) => e => e.AttributesText,
            nameof(EntryViewModel.TypeName) => e => e.TypeName,
            _ => e => e.Size,
        };
        IComparer<object> comparer = _sortMember is nameof(EntryViewModel.Name) or nameof(EntryViewModel.AttributesText) or nameof(EntryViewModel.TypeName)
            ? Comparer<object>.Create((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare((string)a, (string)b))
            : Comparer<object>.Default;
        var sorted = _sortDirection == ListSortDirection.Ascending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer);
        return sorted.ToList();
    }

    // Up from a drive's root goes to "This PC", like Explorer.
    private string? ParentPath() =>
        IsComputerView || CurrentNode is null
            ? null
            : CurrentNode.Parent?.FullPath ?? Path.GetDirectoryName(CurrentNode.FullPath) ?? ThisPcPath;

    private void LoadDrives()
    {
        var thisPc = new FolderTreeItemViewModel("This PC", null, OnTreeSelected, hasChildren: false) { IsExpanded = true };
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var vm = new DriveViewModel(drive);
                Drives.Add(vm);
                thisPc.Children.Add(new FolderTreeItemViewModel(vm.TreeName, vm.RootPath, OnTreeSelected));
            }
            catch (IOException)
            {
                // Drive went away between IsReady and reading its size.
            }
        }
        TreeRoots.Add(thisPc);
    }

    private void OnTreeSelected(FolderTreeItemViewModel item)
    {
        _selectedTreeItem = item;
        if (!_syncingTree) NavigateCommand.Execute(item.FullPath ?? ThisPcPath);
    }

    private static DateTime? SafeTime(FsNode node, Func<FileSystemInfo, DateTime> pick)
    {
        try
        {
            FileSystemInfo info = node.IsDirectory ? new DirectoryInfo(node.FullPath) : new FileInfo(node.FullPath);
            return info.Exists ? pick(info) : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} sec" : $"{t.TotalSeconds:0} sec";
}
