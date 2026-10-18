using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FolderDiskSize.App.ViewModels;

/// <summary>A node in the Folder Browser. Children are listed from disk the first time it expands.</summary>
public sealed partial class FolderTreeItemViewModel : ObservableObject
{
    private static readonly FolderTreeItemViewModel Placeholder = new("…", null, "", _ => { });
    private readonly Action<FolderTreeItemViewModel> _onSelected;
    private bool _loaded;

    public FolderTreeItemViewModel(string name, string? fullPath, string glyph, Action<FolderTreeItemViewModel> onSelected, bool hasChildren = true)
    {
        Name = name;
        FullPath = fullPath;
        Glyph = glyph;
        _onSelected = onSelected;
        if (hasChildren) Children.Add(Placeholder);
    }

    public string Name { get; }

    /// <summary>Null for virtual nodes such as "This PC".</summary>
    public string? FullPath { get; }

    public string Glyph { get; }
    public ObservableCollection<FolderTreeItemViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && FullPath is not null) LoadChildren();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }

    private void LoadChildren()
    {
        _loaded = true;
        Children.Clear();
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var dir in new DirectoryInfo(FullPath!).EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var isLink = (dir.Attributes & FileAttributes.ReparsePoint) != 0;
                Children.Add(new FolderTreeItemViewModel(dir.Name, dir.FullName, isLink ? "" : "", _onSelected, hasChildren: !isLink));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Leave it empty; the Details grid shows the folder in red once scanned.
        }
    }
}
