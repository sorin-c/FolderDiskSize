using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderDiskSize.App.Services;
using FolderDiskSize.App.ViewModels;
using FolderDiskSize.Core.Scanning;

namespace FolderDiskSize.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> TextColumns = ["Name", "AttributesText", "TypeName"];

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel(new FileSystemScanner()) { ShowHiddenFiles = _settings.ShowHiddenFiles };
        DataContext = vm;

        // AvalonDock panels can float into their own windows, which breaks DataContext inheritance,
        // so each panel's content gets the view model directly.
        FolderTree.DataContext = vm;
        DriveList.DataContext = vm;
        Loaded += (_, _) => FitDriveSpacePane(vm.Drives.Count);
        DetailsRoot.DataContext = vm;
        ComputerRoot.DataContext = vm;
        ComputerInfoPanel.DataContext = new ComputerInfoViewModel();

        SetUpColumnChooser();

        // Lets the cell style keep the selection highlighted while a header or row menu has the focus.
        EntriesGrid.ContextMenuOpening += (_, _) => EntriesGrid.Tag = "MenuOpen";
        EntriesGrid.ContextMenuClosing += (_, _) => EntriesGrid.Tag = null;

        vm.PropertyChanged += (_, e) =>
        {
            // "This PC" shows the drive tiles; a folder (from a tile, the tree, the address bar, back/forward) shows the grid.
            if (e.PropertyName == nameof(MainViewModel.IsComputerView))
            {
                ComputerRoot.Visibility = vm.IsComputerView ? Visibility.Visible : Visibility.Collapsed;
                DetailsRoot.Visibility = vm.IsComputerView ? Visibility.Collapsed : Visibility.Visible;
            }
            if (e.PropertyName == nameof(MainViewModel.Entries))
            {
                Dispatcher.BeginInvoke(ShowSortArrow, DispatcherPriority.Loaded);
                Dispatcher.BeginInvoke(FocusGridAfterNavigation, DispatcherPriority.Loaded);
            }
        };
    }

    private readonly UserSettings _settings = UserSettings.Load();
    private string _typeAhead = "";
    private DateTime _lastTypeAhead;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnOptionsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OptionsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _settings.Save();
        ViewModel.ShowHiddenFiles = _settings.ShowHiddenFiles;
        ViewModel.RefreshCurrentView();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Backspace goes back like in Explorer, except while typing in a text box.
        if (e.Key == Key.Back && Keyboard.FocusedElement is not TextBox)
        {
            ViewModel.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Height of a row (and the header) in the Drive Space list; fixed so the pane can be sized to fit.</summary>
    public const double DriveRowHeight = 24;

    /// <summary>
    /// Makes the Drive Space pane tall enough for the header and every drive, so it opens without a
    /// scroll bar; the Folder Browser above gets the rest. The user can still drag the splitter.
    /// AvalonDock turns pixel heights in a pane group into 1*, so this splits the height as star ratios
    /// once the window has its real size.
    /// </summary>
    private void FitDriveSpacePane(int driveCount)
    {
        const double paneChrome = 34; // AvalonDock's title bar plus borders
        const double slack = 12;
        var needed = paneChrome + DriveRowHeight * (driveCount + 1) + slack;
        var total = Docking.ActualHeight;
        if (total < needed + 150) return; // window too short; keep the default split
        FolderBrowserPane.DockHeight = new GridLength(total - needed, GridUnitType.Star);
        DriveSpacePane.DockHeight = new GridLength(needed, GridUnitType.Star);
    }

    private void OnDriveDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Open the drive in the Details view, ignoring double-clicks on the column headers.
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is GridViewColumnHeader) return;
            if (source is ListViewItem { Content: DriveViewModel drive })
            {
                ViewModel.NavigateCommand.Execute(drive.RootPath);
                return;
            }
        }
    }

    private void OnDriveTileClick(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is ListBoxItem { Content: DriveViewModel drive })
            {
                ViewModel.NavigateCommand.Execute(drive.RootPath);
                return;
            }
        }
    }

    private void OnDriveTileKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DriveTiles.SelectedItem is DriveViewModel drive)
        {
            ViewModel.NavigateCommand.Execute(drive.RootPath);
            e.Handled = true;
        }
    }

    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        // Scroll the tree so a folder selected by navigation (back, grid, address bar) is visible.
        if (e.OriginalSource is TreeViewItem item) item.BringIntoView();
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        // The side buttons on most mice.
        if (e.ChangedButton == MouseButton.XButton1)
        {
            ViewModel.GoBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            ViewModel.GoForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Right-clicking a column header in the Details grid shows a menu with a checkbox per column
    /// to show or hide it. Name always stays visible. Hidden columns are remembered between runs.
    /// </summary>
    private void SetUpColumnChooser()
    {
        foreach (var column in EntriesGrid.Columns)
            if (!IsNameColumn(column) && _settings.HiddenColumns.Contains(HeaderOf(column)))
                column.Visibility = Visibility.Collapsed;

        var menu = new ContextMenu();
        EntriesGrid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader), EntriesGrid.ColumnHeaderStyle)
        {
            Setters = { new Setter(ContextMenuProperty, menu) },
        };
        // Rebuilt each time so the list follows the columns' current order and visibility.
        EntriesGrid.ContextMenuOpening += (_, _) => FillColumnMenu(menu);
    }

    private void FillColumnMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        foreach (var column in EntriesGrid.Columns.OrderBy(c => c.DisplayIndex))
        {
            var item = new MenuItem
            {
                Header = HeaderOf(column),
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                IsEnabled = !IsNameColumn(column),
            };
            item.Click += (_, _) => SetColumnVisible(column, item.IsChecked);
            menu.Items.Add(item);
        }
    }

    private void SetColumnVisible(DataGridColumn column, bool visible)
    {
        column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var header = HeaderOf(column);
        _settings.HiddenColumns.Remove(header);
        if (!visible) _settings.HiddenColumns.Add(header);
        _settings.Save();
    }

    private static string HeaderOf(DataGridColumn column) => column.Header as string ?? "";

    private static bool IsNameColumn(DataGridColumn column) => HeaderOf(column) == "Name";

    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        // Sort in the view model rather than through the collection view: it is faster on
        // large folders and keeps the chosen order when navigating to another folder.
        e.Handled = true;
        var member = e.Column.SortMemberPath;
        var (current, direction) = ViewModel.CurrentSort;
        var next = member != current
            ? TextColumns.Contains(member) ? ListSortDirection.Ascending : ListSortDirection.Descending
            : direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ViewModel.SortBy(member, next);
        ShowSortArrow();
    }

    /// <summary>
    /// Marks the sorted column so its header shows the sort arrow. The grid clears every column's
    /// SortDirection whenever it gets a new list of entries, so this runs after each one.
    /// </summary>
    private void ShowSortArrow()
    {
        var (member, direction) = ViewModel.CurrentSort;
        foreach (var column in EntriesGrid.Columns)
            column.SortDirection = column.SortMemberPath == member ? direction : null;
    }

    /// <summary>
    /// After the grid shows a new folder, put keyboard focus on the selected row (or the first one)
    /// so the arrow keys work straight away. Left alone while the user is working in the tree or drive list.
    /// </summary>
    private void FocusGridAfterNavigation()
    {
        if (FolderTree.IsKeyboardFocusWithin || DriveList.IsKeyboardFocusWithin)
            return;
        var item = ViewModel.SelectedEntry ?? ViewModel.Entries.FirstOrDefault();
        if (item is null)
        {
            EntriesGrid.Focus();
            return;
        }
        ViewModel.SelectedEntry = item;
        FocusRow(item);
    }

    private void FocusRow(EntryViewModel item)
    {
        EntriesGrid.ScrollIntoView(item);
        EntriesGrid.UpdateLayout();
        EntriesGrid.CurrentCell = new DataGridCellInfo(item, EntriesGrid.Columns[0]);
        if (EntriesGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row
            && FindChild<DataGridCellsPresenter>(row) is { } presenter
            && presenter.ItemContainerGenerator.ContainerFromIndex(0) is DataGridCell cell)
        {
            cell.Focus();
        }
        else
        {
            EntriesGrid.Focus();
        }
    }

    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter opens the selected folder or file instead of moving down a row.
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel.OpenEntryCommand.Execute(ViewModel.SelectedEntry);
            e.Handled = true;
        }
        // Home and End go to the first and last row like Explorer, instead of the first and last column.
        else if (e.Key is Key.Home or Key.End && Keyboard.Modifiers == ModifierKeys.None && ViewModel.Entries.Count > 0)
        {
            var target = e.Key == Key.Home ? ViewModel.Entries[0] : ViewModel.Entries[^1];
            ViewModel.SelectedEntry = target;
            FocusRow(target);
            e.Handled = true;
        }
        // Delete sends the selected items to the Recycle Bin; Shift+Delete deletes them permanently, like Explorer.
        else if (e.Key == Key.Delete && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            DeleteEntries(EntriesGrid.SelectedItems.OfType<EntryViewModel>().ToList(), Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
        }
    }

    private void DeleteEntries(IReadOnlyList<EntryViewModel> entries, bool permanently)
    {
        if (entries.Count == 0) return;
        ShellActions.Delete(this, entries.Select(x => x.Node.FullPath).ToList(), permanently);
        // Even after a cancel or an error part of the selection may be gone, so always check what is left.
        ViewModel.RemoveDeleted(entries);
        if (ViewModel.SelectedEntry is { } next) FocusRow(next);
    }

    private void OnGridPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Type-ahead like Explorer: typing the start of a name jumps to it. Pressing the same
        // letter again cycles through the names that start with it.
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        var now = DateTime.UtcNow;
        _typeAhead = now - _lastTypeAhead > TimeSpan.FromSeconds(1) ? e.Text : _typeAhead + e.Text;
        _lastTypeAhead = now;

        var entries = ViewModel.Entries;
        if (entries.Count == 0) return;
        var cycling = _typeAhead.Length > 1 && _typeAhead.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(_typeAhead[0]));
        var prefix = cycling ? _typeAhead[..1] : _typeAhead;
        var current = ViewModel.SelectedEntry is { } sel ? IndexOf(entries, sel) : -1;
        var start = cycling || _typeAhead.Length == 1 ? current + 1 : Math.Max(current, 0);

        for (var i = 0; i < entries.Count; i++)
        {
            var candidate = entries[(start + i) % entries.Count];
            if (candidate.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                ViewModel.SelectedEntry = candidate;
                FocusRow(candidate);
                break;
            }
        }
        e.Handled = true;
    }

    private static int IndexOf(IReadOnlyList<EntryViewModel> items, EntryViewModel item)
    {
        for (var i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only react to double-clicks on a row, not on headers or the scrollbar.
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is DataGridColumnHeader) return;
            if (source is DataGridRow { Item: EntryViewModel entry })
            {
                ViewModel.OpenEntryCommand.Execute(entry);
                return;
            }
        }
    }

    // Row right-click menu. Each menu item's DataContext is the row it was opened on.
    private static EntryViewModel? MenuEntry(object sender) => (sender as FrameworkElement)?.DataContext as EntryViewModel;

    private void OnRowMenuOpen(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } entry) ViewModel.OpenEntryCommand.Execute(entry);
    }

    private void OnRowMenuShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } entry && ShellActions.ShowInExplorer(entry.Node.FullPath) is { } error)
            ViewModel.StatusText = $"Couldn't open Explorer: {error}";
    }

    private void OnRowMenuCopyPath(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } entry) return;
        // With several rows selected, copy all of them, like Explorer's "Copy as path".
        var selected = EntriesGrid.SelectedItems.OfType<EntryViewModel>().ToList();
        IEnumerable<string> paths = selected.Contains(entry) ? selected.Select(s => s.Node.FullPath) : [entry.Node.FullPath];
        if (ShellActions.CopyPaths(paths) is { } error)
            ViewModel.StatusText = $"Couldn't copy to the clipboard: {error}";
    }

    private void OnRowMenuRescan(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } entry) ViewModel.RescanEntryCommand.Execute(entry);
    }

    private void OnRowMenuDelete(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } entry) return;
        var selected = EntriesGrid.SelectedItems.OfType<EntryViewModel>().ToList();
        DeleteEntries(selected.Contains(entry) ? selected : [entry], permanently: Keyboard.Modifiers == ModifierKeys.Shift);
    }

    private void OnRowMenuProperties(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } entry && !ShellActions.ShowProperties(this, entry.Node.FullPath))
            ViewModel.StatusText = $"Couldn't show properties for {entry.Name}";
    }
}
