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
