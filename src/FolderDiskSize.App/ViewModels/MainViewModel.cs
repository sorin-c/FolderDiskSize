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
    private FolderTreeItemViewModel? _selectedTreeItem;
    private bool _syncingTree;
    private string _sortMember = nameof(EntryViewModel.Size);
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    public MainViewModel(IDiskScanner scanner)
    {
        _scanner = scanner;
        LoadDrives();
    }

    public ObservableCollection<FolderTreeItemViewModel> TreeRoots { get; } = [];
    public ObservableCollection<DriveViewModel> Drives { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<EntryViewModel> _entries = [];

    [ObservableProperty]
    private string _address = "";

    [ObservableProperty]
    private FsNode? _currentNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsNode))]
    private EntryViewModel? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isScanning;

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
    public FsNode? DetailsNode => SelectedEntry?.Node ?? CurrentNode;

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

    private void RefreshDetails()
    {
        foreach (var name in new[] { nameof(DetailsNode), nameof(DetailsName), nameof(DetailsType), nameof(DetailsCounts), nameof(DetailsSize),
                     nameof(DetailsModified), nameof(DetailsCreated), nameof(DetailsAccessed), nameof(DetailsAttributes), nameof(DetailsPercent) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private Task NavigateAsync(string? path) => string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : GoToAsync(path, recordHistory: true);

    [RelayCommand]
    private Task OpenEntryAsync(EntryViewModel? entry) =>
        entry is { IsDirectory: true, Node.IsReparsePoint: false } ? GoToAsync(entry.Node.FullPath, recordHistory: true) : Task.CompletedTask;

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
        if (CurrentNode is null) return Task.CompletedTask;
        var target = _back.Count > 0 ? _back.Pop() : ParentPath();
        if (target is null) return Task.CompletedTask;
        _forward.Push(CurrentNode.FullPath);
        return GoToAsync(target, recordHistory: false);
    }

    [RelayCommand]
    private Task GoForwardAsync()
    {
        if (_forward.Count == 0 || CurrentNode is null) return Task.CompletedTask;
        _back.Push(CurrentNode.FullPath);
        return GoToAsync(_forward.Pop(), recordHistory: false);
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (CurrentNode is null) return;
        var path = CurrentNode.FullPath;
        var result = await ScanAsync(path);
        if (result is not null) Show(result);
    }

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan() => _scanCts?.Cancel();

    /// <summary>Called by the view when a column header is clicked; sorting is done here so it survives navigation.</summary>
    public void SortBy(string member, ListSortDirection direction)
    {
        _sortMember = member;
        _sortDirection = direction;
        Entries = Sort(Entries);
    }

    public (string Member, ListSortDirection Direction) CurrentSort => (_sortMember, _sortDirection);

    private async Task GoToAsync(string path, bool recordHistory)
    {
        string normalized;
        try { normalized = PathUtil.Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusText = $"Invalid path: {path}";
            return;
        }

        var node = FindScanned(normalized);
        if (node is null)
        {
            if (!Directory.Exists(normalized))
            {
                StatusText = $"Folder not found: {normalized}";
                return;
            }
            node = await ScanAsync(normalized);
            if (node is null) return;
        }

        if (recordHistory && CurrentNode is not null && !string.Equals(CurrentNode.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(CurrentNode.FullPath);
            _forward.Clear();
        }
        Show(node);
    }

    private void Show(FsNode node)
    {
        CurrentNode = node;
        Address = node.FullPath;
        SelectedEntry = null;
        Entries = Sort(node.Children.Select(c => new EntryViewModel(c)));
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
        if (match is null || match == _selectedTreeItem) return;
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

    private string? ParentPath() =>
        CurrentNode?.Parent?.FullPath ?? (CurrentNode is null ? null : Path.GetDirectoryName(CurrentNode.FullPath));

    private void LoadDrives()
    {
        var thisPc = new FolderTreeItemViewModel("This PC", null, "", OnTreeSelected, hasChildren: false) { IsExpanded = true };
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var vm = new DriveViewModel(drive);
                Drives.Add(vm);
                thisPc.Children.Add(new FolderTreeItemViewModel(vm.TreeName, vm.RootPath, "", OnTreeSelected));
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
        if (!_syncingTree && item.FullPath is not null) NavigateCommand.Execute(item.FullPath);
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
