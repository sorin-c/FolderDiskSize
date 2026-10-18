using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FolderDiskSize.App.ViewModels;
using FolderDiskSize.Core.Scanning;

namespace FolderDiskSize.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> TextColumns = ["Name", "AttributesText", "TypeName"];

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel(new FileSystemScanner());
        DataContext = vm;

        // AvalonDock panels can float into their own windows, which breaks DataContext inheritance,
        // so each panel's content gets the view model directly.
        FolderTree.DataContext = vm;
        DriveList.DataContext = vm;
        DetailsRoot.DataContext = vm;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

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

    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        // Sort in the view model rather than through the collection view: it is faster on
        // large folders and keeps the chosen order when navigating to another folder.
        e.Handled = true;
        var member = e.Column.SortMemberPath;
        var next = e.Column.SortDirection switch
        {
            ListSortDirection.Ascending => ListSortDirection.Descending,
            ListSortDirection.Descending => ListSortDirection.Ascending,
            _ => TextColumns.Contains(member) ? ListSortDirection.Ascending : ListSortDirection.Descending,
        };
        foreach (var column in EntriesGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = next;
        ViewModel.SortBy(member, next);
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
}
