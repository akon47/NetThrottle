using System.Diagnostics;

namespace NetThrottle.App.Services;

public interface IProcessListProvider
{
    /// <summary>Distinct, sorted image names (e.g. "chrome.exe") of running processes.</summary>
    IReadOnlyList<string> GetRunningProcessNames();
}

public sealed class ProcessListProvider : IProcessListProvider
{
    public IReadOnlyList<string> GetRunningProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrEmpty(process.ProcessName))
                    names.Add(process.ProcessName + ".exe");
            }
            catch
            {
                // Access denied for some system processes; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }
        return names.ToList();
    }
}
