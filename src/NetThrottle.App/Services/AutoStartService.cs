using System.Diagnostics;

namespace NetThrottle.App.Services;

/// <summary>
/// "Start with Windows" via a Scheduled Task. Because NetThrottle runs elevated,
/// a logon task with "highest privileges" launches it at sign-in without a UAC
/// prompt (a plain Run key entry would be blocked for an elevated app).
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "NetThrottle";

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe)) return;
            Run($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        }
        else
        {
            Run($"/Delete /TN \"{TaskName}\" /F");
        }
    }

    private static int Run(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return -1;
            process.WaitForExit(5000);
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
