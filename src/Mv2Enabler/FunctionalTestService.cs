using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mv2Enabler;

internal sealed record FunctionalTestResult(
    bool Success,
    uint ProcessId,
    bool DevToolsStarted,
    bool ExtensionTargetFound,
    string ExpectedExtensionId,
    string? ExtensionTargetTitle,
    string? ExtensionTargetUrl,
    IReadOnlyList<string> DiscoveredExtensionTargets,
    bool TemporaryProfileRemoved,
    string Message);

internal static class FunctionalTestService
{
    public static FunctionalTestResult Run(
        ChromeInstallation installation,
        PatchTarget target,
        string extensionPath,
        TimeSpan timeout)
    {
        extensionPath = Path.GetFullPath(extensionPath);
        if (!File.Exists(Path.Combine(extensionPath, "manifest.json")))
        {
            throw new DirectoryNotFoundException($"No manifest.json was found in extension directory: {extensionPath}");
        }

        var expectedExtensionId = ReadExtensionId(Path.Combine(extensionPath, "manifest.json"));

        var temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mv2ctl-functional"));
        var profilePath = Path.GetFullPath(Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")));
        if (!profilePath.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to use an unexpected functional-test profile path.");
        }

        Directory.CreateDirectory(profilePath);
        LaunchResult? launch = null;
        Process? browser = null;
        var devToolsStarted = false;
        string? extensionTargetTitle = null;
        string? extensionTargetUrl = null;
        var discoveredExtensionTargets = new List<string>();
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
                    "--remote-debugging-port=0",
                    $"--user-data-dir={profilePath}",
                    $"--disable-extensions-except={extensionPath}",
                    $"--load-extension={extensionPath}",
                    "about:blank"
                ],
                timeout);

            browser = Process.GetProcessById(checked((int)launch.ProcessId));
            var activePortFile = Path.Combine(profilePath, "DevToolsActivePort");
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(10) && !File.Exists(activePortFile))
            {
                if (browser.HasExited)
                {
                    throw new InvalidOperationException($"Patched Chrome exited early with code {browser.ExitCode}.");
                }

                Thread.Sleep(100);
            }

            if (File.Exists(activePortFile))
            {
                var lines = File.ReadAllLines(activePortFile);
                if (lines.Length > 0 && int.TryParse(lines[0], out var port))
                {
                    devToolsStarted = true;
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var json = client.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
                    using var document = JsonDocument.Parse(json);
                    var targets = document.RootElement.EnumerateArray()
                        .Select(item => new
                        {
                            Title = item.TryGetProperty("title", out var title) ? title.GetString() : null,
                            Url = item.TryGetProperty("url", out var url) ? url.GetString() : null
                        })
                        .ToList();
                    discoveredExtensionTargets.AddRange(targets
                        .Where(item => item.Url?.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) == true)
                        .Select(item => $"{item.Title ?? "(no title)"} | {item.Url}"));
                    var extensionTarget = targets
                        .FirstOrDefault(item =>
                            item.Url is not null &&
                            Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) &&
                            string.Equals(uri.Scheme, "chrome-extension", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(uri.Host, expectedExtensionId, StringComparison.OrdinalIgnoreCase));
                    extensionTargetTitle = extensionTarget?.Title;
                    extensionTargetUrl = extensionTarget?.Url;
                }
            }
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
                    // The process exited during cleanup.
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
        }

        var found = extensionTargetUrl is not null;
        var message = found
            ? "The MV2 persistent background page appeared as a DevTools extension target."
            : "Chrome started with the RAM patch, but Google Chrome did not expose an extension target; this build may ignore --load-extension.";
        return new FunctionalTestResult(
            found,
            launch.ProcessId,
            devToolsStarted,
            found,
            expectedExtensionId,
            extensionTargetTitle,
            extensionTargetUrl,
            discoveredExtensionTargets,
            removed,
            message);
    }

    internal static string ReadExtensionId(string manifestPath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifest.RootElement.TryGetProperty("key", out var keyElement) || keyElement.GetString() is not { } key)
        {
            throw new InvalidDataException("The functional test extension must declare a fixed public 'key' in manifest.json.");
        }

        var publicKey = Convert.FromBase64String(key);
        var hash = SHA256.HashData(publicKey);
        Span<char> extensionId = stackalloc char[32];
        for (var index = 0; index < 16; index++)
        {
            extensionId[index * 2] = (char)('a' + (hash[index] >> 4));
            extensionId[index * 2 + 1] = (char)('a' + (hash[index] & 0x0f));
        }

        return new string(extensionId);
    }
}
