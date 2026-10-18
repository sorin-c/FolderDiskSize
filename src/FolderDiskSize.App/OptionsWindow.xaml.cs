using System.Windows;
using FolderDiskSize.App.Services;

namespace FolderDiskSize.App;

/// <summary>The Options dialog. Edits <see cref="UserSettings"/> in place only when OK is pressed.</summary>
public partial class OptionsWindow : Window
{
    private readonly UserSettings _settings;

    public OptionsWindow(UserSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ShowHiddenFiles.IsChecked = settings.ShowHiddenFiles;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _settings.ShowHiddenFiles = ShowHiddenFiles.IsChecked == true;
        DialogResult = true;
    }
}
