using System.Diagnostics;

namespace Mv2Enabler;

internal sealed record SmokeTestResult(
    bool Success,
    uint ProcessId,
    string RemoteAddress,
    bool ProcessStayedAlive,
    bool TemporaryProfileRemoved,
    string Message);

internal static class SmokeTestService
{
    public static SmokeTestResult Run(ChromeInstallation installation, PatchTarget target, TimeSpan timeout)
    {
        var temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mv2ctl-smoke"));
        var profilePath = Path.GetFullPath(Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")));
        if (!profilePath.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to use an unexpected smoke-test profile path.");
        }

        Directory.CreateDirectory(profilePath);
        LaunchResult? launch = null;
        Process? browser = null;
        var stayedAlive = false;
        var removed = false;
        try
        {
            launch = ChromeDebugLauncher.Launch(
                installation,
                target,
                [
                    "--headless=new",
                    "--no-first-run",
                    "--disable-background-networking",
                    $"--user-data-dir={profilePath}",
                    "about:blank"
                ],
                timeout);

            browser = Process.GetProcessById(checked((int)launch.ProcessId));
            if (browser.WaitForExit(2500))
            {
                throw new InvalidOperationException($"Patched Chrome exited early with code {browser.ExitCode}.");
            }

            stayedAlive = true;
        }
        finally
        {
            if (browser is not null)
            {
                try
                {
                    if (!browser.HasExited)
                    {
                        browser.Kill(entireProcessTree: true);
                        browser.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the state check and cleanup.
                }
                finally
                {
                    browser.Dispose();
                }
            }

            for (var attempt = 0; attempt < 5 && Directory.Exists(profilePath); attempt++)
            {
                try
                {
                    Directory.Delete(profilePath, recursive: true);
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(250);
                }
            }

            removed = !Directory.Exists(profilePath);
            if (launch is not null && stayedAlive && !removed)
            {
                Console.Error.WriteLine($"WARNING: temporary smoke-test profile could not be removed: {profilePath}");
            }
        }

        return new SmokeTestResult(
            true,
            launch.ProcessId,
            launch.RemoteAddress,
            stayedAlive,
            removed,
            "The target byte was patched and verified in RAM; headless Chrome remained alive for 2.5 seconds.");
    }
}
