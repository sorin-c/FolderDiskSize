using System.IO;
using System.Text.Json;

namespace FolderDiskSize.App.Services;

/// <summary>
/// Preferences kept between runs, stored as JSON in %LocalAppData%\FolderDiskSize\settings.json.
/// A missing or unreadable file just gives the defaults.
/// </summary>
public sealed class UserSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderDiskSize", "settings.json");

    /// <summary>Headers of the Details grid columns the user has hidden.</summary>
    public List<string> HiddenColumns { get; set; } = [];

    /// <summary>List hidden and system files and folders in the Details grid.</summary>
    public bool ShowHiddenFiles { get; set; } = true;

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save a preference shouldn't interrupt the user.
        }
    }
}
