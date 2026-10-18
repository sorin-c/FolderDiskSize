namespace FolderDiskSize.Core;

public static class PathUtil
{
    /// <summary>Full path without a trailing separator, except for roots like "C:\" or "/".</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (full.Length > (root?.Length ?? 0))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="folder"/>. Both must be normalized.</summary>
    public static bool IsUnder(string path, string folder)
    {
        if (path.Length <= folder.Length) return false;
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;
        var endsWithSep = folder[^1] == Path.DirectorySeparatorChar || folder[^1] == Path.AltDirectorySeparatorChar;
        return endsWithSep || path[folder.Length] == Path.DirectorySeparatorChar || path[folder.Length] == Path.AltDirectorySeparatorChar;
    }
}
