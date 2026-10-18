using System.Runtime.InteropServices;
using FolderDiskSize.Core.Formatting;

namespace FolderDiskSize.App.ViewModels;

/// <summary>The strip under the drive tiles on the Computer tab: machine name, domain, processors, memory, user.</summary>
public sealed class ComputerInfoViewModel
{
    public string MachineName { get; } = Environment.MachineName;
    public string DomainText { get; } = JoinText();
    public string ProcessorsText { get; } = $"Processors: {Environment.ProcessorCount}";
    public string MemoryText { get; } = $"Memory: {SizeFormatter.Format(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)}";
    public string UserText { get; } = $@"User: {Environment.UserDomainName}\{Environment.UserName}";

    /// <summary>"Domain: X" or "Workgroup: X", as Windows reports it.</summary>
    private static string JoinText()
    {
        if (!OperatingSystem.IsWindows()) return "";
        if (NetGetJoinInformation(null, out var name, out var status) != 0) return "";
        try
        {
            var joined = Marshal.PtrToStringUni(name) ?? "";
            return status switch
            {
                3 => $"Domain: {joined}",
                2 => $"Workgroup: {joined}",
                _ => "",
            };
        }
        finally
        {
            NetApiBufferFree(name);
        }
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr name, out int status);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
