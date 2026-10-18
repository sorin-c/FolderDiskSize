using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDiskSize.App.Services;

namespace FolderDiskSize.App.ViewModels;

/// <summary>A node in the Folder Browser. Children are listed from disk the first time it expands.</summary>
public sealed partial class FolderTreeItemViewModel : ObservableObject
{
    private static readonly FolderTreeItemViewModel Placeholder = new("…", null, _ => { }, hasChildren: false);
    private readonly Action<FolderTreeItemViewModel> _onSelected;
    private readonly bool _isLink;
    private bool _loaded;
    private ImageSource? _icon;

    public FolderTreeItemViewModel(string name, string? fullPath, Action<FolderTreeItemViewModel> onSelected, bool hasChildren = true, bool isLink = false)
    {
        Name = name;
        FullPath = fullPath;
        _isLink = isLink;
        _onSelected = onSelected;
        if (hasChildren) Children.Add(Placeholder);
    }

    public string Name { get; }

    /// <summary>Null for virtual nodes such as "This PC".</summary>
    public string? FullPath { get; }

    /// <summary>"This PC", the drive's own icon, or the folder icon (with a shortcut arrow for links).</summary>
    public ImageSource? Icon => _icon ??= FullPath switch
    {
        null when Name == "…" => null,
        null => ShellIcons.ForThisPc(),
        var p when Path.GetPathRoot(p) == p => ShellIcons.ForDrive(p),
        _ => ShellIcons.ForFolder(_isLink),
    };

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

    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    /// <summary>Whether the folder has at least one subfolder, so only those get an expand arrow, like Explorer.</summary>
    private static bool HasSubfolders(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories("*", Options).Any();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void LoadChildren()
    {
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in new DirectoryInfo(FullPath!).EnumerateDirectories("*", Options).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var isLink = (dir.Attributes & FileAttributes.ReparsePoint) != 0;
                Children.Add(new FolderTreeItemViewModel(dir.Name, dir.FullName, _onSelected, hasChildren: !isLink && HasSubfolders(dir), isLink: isLink));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Leave it empty; the Details grid shows the folder in red once scanned.
        }
    }
}
