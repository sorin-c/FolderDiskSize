namespace FolderDiskSize.Core.Formatting;

public static class AttributeFormatter
{
    private static readonly (FileAttributes Flag, char Letter)[] Letters =
    [
        (FileAttributes.ReadOnly, 'R'),
        (FileAttributes.Hidden, 'H'),
        (FileAttributes.System, 'S'),
        (FileAttributes.Directory, 'D'),
        (FileAttributes.Archive, 'A'),
        (FileAttributes.Compressed, 'C'),
        (FileAttributes.Encrypted, 'E'),
        (FileAttributes.ReparsePoint, 'L'),
        (FileAttributes.SparseFile, 'P'),
        (FileAttributes.Offline, 'O'),
    ];

    /// <summary>Short attribute string such as "HSD" or "DA".</summary>
    public static string Format(FileAttributes attributes)
    {
        Span<char> buffer = stackalloc char[Letters.Length];
        var n = 0;
        foreach (var (flag, letter) in Letters)
            if ((attributes & flag) != 0) buffer[n++] = letter;
        return new string(buffer[..n]);
    }
}
