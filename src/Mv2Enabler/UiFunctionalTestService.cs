using System.Diagnostics;

namespace Mv2Enabler;

internal sealed record UiFunctionalTestResult(
    bool Success,
    uint ProcessId,
    string ExpectedExtensionId,
    string DialogTitle,
    bool FolderDialogClosed,
    byte LivePatchByte,
    IReadOnlyList<int> PatchedChromeProcessIds,
    string? ExtensionTargetTitle,
    string? ExtensionTargetUrl,
    bool RestartPersistenceVerified,
    string? RestartExtensionTargetUrl,
    string ExtensionManagerState,
    string ExtensionsPageText,
    bool TemporaryProfileRemoved,
    string Message);

internal static class UiFunctionalTestService
{
    public static UiFunctionalTestResult Run(
        ChromeInstallation installation,
        PatchTarget target,
        string extensionPath,
        TimeSpan timeout)
    {
        extensionPath = Path.GetFullPath(extensionPath);
        var manifestPath = Path.Combine(extensionPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new DirectoryNotFoundException($"No manifest.json was found in extension directory: {extensionPath}");
        }

        var expectedId = FunctionalTestService.ReadExtensionId(manifestPath);
        var temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mv2ctl-ui-functional"));
        var profilePath = Path.GetFullPath(Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")));
        if (!profilePath.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to use an unexpected UI-test profile path.");
        }

        Directory.CreateDirectory(profilePath);
        LaunchResult? launch = null;
        Process? browser = null;
        var dialogTitle = string.Empty;
        var folderDialogClosed = false;
        byte livePatchByte = 0;
        IReadOnlyList<int> patchedChromeProcessIds = [];
        DevToolsTarget? extensionTarget = null;
        DevToolsTarget? restartExtensionTarget = null;
        var extensionManagerState = string.Empty;
        var extensionsPageText = string.Empty;
        var removed = false;
        try
        {
            launch = ChromeDebugLauncher.Launch(
                installation,
                target,
                [
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--remote-debugging-port=0",
                    $"--user-data-dir={profilePath}",
                    "chrome://extensions/"
                ],
                timeout);

            browser = Process.GetProcessById(checked((int)launch.ProcessId));
            var port = ChromeDevTools.WaitForPort(profilePath, browser, TimeSpan.FromSeconds(10));
            DevToolsTarget? extensionsPage = null;
            var lastTargets = ChromeDevTools.GetTargets(port);
            var initialPage = lastTargets.FirstOrDefault(item =>
                string.Equals(item.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                item.WebSocketDebuggerUrl is not null);
            if (initialPage?.WebSocketDebuggerUrl is not null)
            {
                ChromeDevTools.Navigate(initialPage.WebSocketDebuggerUrl, "chrome://extensions/");
            }

            var pageDeadline = Stopwatch.StartNew();
            while (pageDeadline.Elapsed < TimeSpan.FromSeconds(10) && extensionsPage is null)
            {
                lastTargets = ChromeDevTools.GetTargets(port);
                extensionsPage = lastTargets
                    .FirstOrDefault(item => item.Url?.StartsWith("chrome://extensions", StringComparison.OrdinalIgnoreCase) == true);
                if (extensionsPage is null)
                {
                    Thread.Sleep(100);
                }
            }

            if (extensionsPage?.WebSocketDebuggerUrl is null)
            {
                var discovered = string.Join(", ", lastTargets.Select(item => $"{item.Type}:{item.Url}"));
                throw new InvalidOperationException($"The chrome://extensions DevTools target was not found. Targets: {discovered}");
            }

            ChromeDevTools.TriggerLoadUnpacked(extensionsPage.WebSocketDebuggerUrl);
            livePatchByte = ChromeDebugLauncher.ReadRemoteByte(launch.ProcessId, launch.RemoteAddressValue);
            patchedChromeProcessIds = ChromeDebugLauncher.PatchExistingChromeProcesses(target);
            var dialog = WindowsFolderDialog.WaitForDialog(launch.ProcessId, TimeSpan.FromSeconds(8));
            if (dialog == IntPtr.Zero)
            {
                throw new TimeoutException("The Load unpacked folder dialog did not appear.");
            }

            dialogTitle = WindowsFolderDialog.GetTitle(dialog);
            folderDialogClosed = WindowsFolderDialog.SelectFolder(dialog, extensionPath);

            Thread.Sleep(1500);
            var extensionInfoResponse = ChromeDevTools.Evaluate(
                extensionsPage.WebSocketDebuggerUrl,
                "new Promise(resolve => chrome.developerPrivate.getExtensionsInfo({includeDisabled:true,includeTerminated:true}, items => resolve(items.map(item => ({id:item.id,name:item.name,state:item.state,disableReasons:item.disableReasons,runtimeErrorCount:item.runtimeErrors?.length || 0,manifestErrorCount:item.manifestErrors?.length || 0})))));"
            );
            extensionManagerState = extensionInfoResponse.GetRawText();
            var pageTextResponse = ChromeDevTools.Evaluate(
                extensionsPage.WebSocketDebuggerUrl,
                "(() => { const out=[]; const walk=n=>{if(n instanceof ShadowRoot){for(const c of n.children)walk(c);return;} if(n instanceof HTMLElement){const own=[...n.childNodes].filter(x=>x.nodeType===3).map(x=>x.textContent.trim()).filter(Boolean).join(' ');if(own)out.push(own);if(n.shadowRoot)walk(n.shadowRoot);for(const c of n.children)walk(c);}};walk(document.body);return [...new Set(out)].join(' | ');})()"
            );
            extensionsPageText = pageTextResponse.GetRawText();

            var extensionDeadline = Stopwatch.StartNew();
            while (extensionDeadline.Elapsed < TimeSpan.FromSeconds(12) && extensionTarget is null)
            {
                extensionTarget = ChromeDevTools.GetTargets(port).FirstOrDefault(item =>
                    item.Url is not null &&
                    Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) &&
                    string.Equals(uri.Scheme, "chrome-extension", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(uri.Host, expectedId, StringComparison.OrdinalIgnoreCase));
                if (extensionTarget is null)
                {
                    Thread.Sleep(200);
                }
            }

            if (extensionTarget is not null)
            {
                StopBrowserForRestart(browser);
                browser.Dispose();
                browser = null;

                var activePortFile = Path.Combine(profilePath, "DevToolsActivePort");
                if (File.Exists(activePortFile))
                {
                    File.Delete(activePortFile);
                }

                var restartLaunch = ChromeDebugLauncher.Launch(
                    installation,
                    target,
                    [
                        "--no-first-run",
                        "--no-default-browser-check",
                        "--remote-debugging-port=0",
                        $"--user-data-dir={profilePath}",
                        "chrome://extensions/"
                    ],
                    timeout);
                browser = Process.GetProcessById(checked((int)restartLaunch.ProcessId));
                var restartPort = ChromeDevTools.WaitForPort(profilePath, browser, TimeSpan.FromSeconds(10));
                var restartDeadline = Stopwatch.StartNew();
                while (restartDeadline.Elapsed < TimeSpan.FromSeconds(12) && restartExtensionTarget is null)
                {
                    restartExtensionTarget = ChromeDevTools.GetTargets(restartPort).FirstOrDefault(item =>
                        item.Url is not null &&
                        Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) &&
                        string.Equals(uri.Scheme, "chrome-extension", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(uri.Host, expectedId, StringComparison.OrdinalIgnoreCase));
                    if (restartExtensionTarget is null)
                    {
                        Thread.Sleep(200);
                    }
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

        var success = extensionTarget is not null && restartExtensionTarget is not null;
        return new UiFunctionalTestResult(
            success,
            launch.ProcessId,
            expectedId,
            dialogTitle,
            folderDialogClosed,
            livePatchByte,
            patchedChromeProcessIds,
            extensionTarget?.Title,
            extensionTarget?.Url,
            restartExtensionTarget is not null,
            restartExtensionTarget?.Url,
            extensionManagerState,
            extensionsPageText,
            removed,
            success
                ? "Chrome loaded the exact MV2 test extension, then preserved and restarted its background page after a full browser restart."
                : extensionTarget is null
                    ? "The folder dialog completed, but the expected MV2 extension background target did not appear."
                    : "Chrome loaded the MV2 extension initially, but its background page did not return after restarting the same profile.");
    }

    private static void StopBrowserForRestart(Process browser)
    {
        if (!browser.HasExited)
        {
            _ = browser.CloseMainWindow();
            if (!browser.WaitForExit(8000))
            {
                browser.Kill(entireProcessTree: true);
                browser.WaitForExit(5000);
            }
        }

        // The test starts only after verifying that no user Chrome instance is
        // running, so any surviving Chrome helper belongs to this isolated profile.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var helpers = Process.GetProcessesByName("chrome");
            if (helpers.Length == 0)
            {
                break;
            }

            foreach (var helper in helpers)
            {
                try
                {
                    if (!helper.HasExited)
                    {
                        helper.Kill(entireProcessTree: true);
                        helper.WaitForExit(1000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The helper exited during cleanup.
                }
                finally
                {
                    helper.Dispose();
                }
            }

            Thread.Sleep(250);
        }
    }
}
