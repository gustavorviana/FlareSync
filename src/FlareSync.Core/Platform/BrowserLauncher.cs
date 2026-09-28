using System.Diagnostics;
using FlareSync.Core.Abstractions;

namespace FlareSync.Core.Platform;

/// <summary>Opens URLs with the platform default browser (never on headless Linux).</summary>
public sealed class BrowserLauncher : IBrowserLauncher
{
    public bool CanOpen =>
        OperatingSystem.IsWindows()
        || OperatingSystem.IsMacOS()
        || (OperatingSystem.IsLinux()
            && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))));

    public bool TryOpen(string url)
    {
        if (!CanOpen)
        {
            return false;
        }

        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [url])
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
