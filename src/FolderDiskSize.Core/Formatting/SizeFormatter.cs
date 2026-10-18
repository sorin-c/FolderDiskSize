using System.Globalization;

namespace FolderDiskSize.Core.Formatting;

public static class SizeFormatter
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats with three significant digits like FolderSizes and Explorer: "1.07 TB", "816 GB", "4.74 MB".</summary>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024) return bytes.ToString("N0", culture) + " bytes";

        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, culture) + " " + Units[unit];
    }
}
