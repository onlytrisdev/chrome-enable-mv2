using System.Diagnostics;

namespace Mv2Enabler.Tests;

public sealed class RestartSessionIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void LiveSessionRestartRejectsPartialWrongProfileStaleAndPatchedSessions()
    {
        var existing = Process.GetProcessesByName("chrome");
        try { Assert.Empty(existing); }
        finally { foreach (var item in existing) item.Dispose(); }
        var installation = ChromeInstallationFinder.Find();
        var report = AnalysisService.Analyze(installation);
        Assert.True(report.Success);
        var target = Assert.IsType<PatchTarget>(report.Target);
        var profile = Path.Combine(Path.GetTempPath(), "mv2restart-test-" + Guid.NewGuid().ToString("N"));
        string[] arguments = ["--headless=new", "--no-first-run", "--no-default-browser-check",
            "--remote-debugging-port=0", "--disable-background-networking", "--user-data-dir=" + profile, "about:blank"];
        Process? browser = null;
        try
        {
            var start = new ProcessStartInfo(installation.ExecutablePath) { UseShellExecute = false };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            browser = Process.Start(start)!;
            _ = ChromeDevTools.WaitForPort(profile, browser, TimeSpan.FromSeconds(15));
            var startup = Stopwatch.StartNew();
            var session = RunningChromeSession.Inspect(arguments);
            while (!session.CanRestart && startup.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(150);
                session = RunningChromeSession.Inspect(arguments);
            }
            Assert.True(session.CanRestart, session.ToString());
            var wrongProfile = new[] { "--user-data-dir=" + profile + "-other" };
            Assert.Equal(ChromeSessionState.Unknown, RunningChromeSession.Inspect(wrongProfile).State);
            Assert.False(RunningChromeSession.CloseUnpatchedSession(wrongProfile, true, TimeSpan.FromSeconds(1), session));
            Assert.False(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(1),
                session with { StartTimeUtcTicks = session.StartTimeUtcTicks - 1 }));
            Assert.False(browser.HasExited);

            var module = browser.Modules.Cast<ProcessModule>().Single(item => item.ModuleName == "chrome.dll");
            var address = new IntPtr(module.BaseAddress.ToInt64() + target.PatchRva);
            WriteOwnedTestByte(browser, address, target.ReplacementByte);
            var partialTimer = Stopwatch.StartNew();
            var partial = RunningChromeSession.Inspect(arguments).State;
            while (partial == ChromeSessionState.Unknown && partialTimer.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(150);
                partial = RunningChromeSession.Inspect(arguments).State;
            }
            Assert.Equal(ChromeSessionState.Partial, partial);
            Assert.False(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(1), session));
            Assert.False(browser.HasExited);
            WriteOwnedTestByte(browser, address, target.ExpectedByte);

            Assert.False(RunningChromeSession.CloseUnpatchedSession(arguments, false, TimeSpan.FromMilliseconds(500), session));
            Assert.False(browser.HasExited); // Background/headless browser has no window to close.
            Assert.True(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(8), session));
            Assert.True(browser.WaitForExit(5000));
            browser.Dispose();
            browser = Process.Start(start)!;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10) && !RunningChromeSession.Inspect(arguments).CanRestart) Thread.Sleep(100);
            var replacement = RunningChromeSession.Inspect(arguments);
            Assert.True(replacement.CanRestart);
            Assert.False(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(1), session));
            Assert.False(browser.HasExited); // A previous confirmation cannot close a new session.
            Assert.True(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(8), replacement));
            browser.Dispose();
            var launch = ChromeDebugLauncher.Launch(installation, target, arguments, TimeSpan.FromSeconds(20));
            browser = Process.GetProcessById((int)launch.ProcessId);
            _ = ChromeDevTools.WaitForPort(profile, browser, TimeSpan.FromSeconds(15));
            Assert.Equal(ChromeSessionState.Patched, RunningChromeSession.Inspect(arguments).State);
            Assert.False(RunningChromeSession.CloseUnpatchedSession(arguments, true, TimeSpan.FromSeconds(1), replacement));
            Assert.False(browser.HasExited);
        }
        finally
        {
            if (browser is not null)
            {
                try { if (!browser.HasExited) { browser.Kill(true); browser.WaitForExit(5000); } }
                finally { browser.Dispose(); }
            }
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
    }

    private static void WriteOwnedTestByte(Process browser, IntPtr address, byte value)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite |
            NativeMethods.ProcessVmOperation, false, (uint)browser.Id);
        Assert.NotEqual(IntPtr.Zero, handle);
        try
        {
            Assert.True(NativeMethods.VirtualProtectEx(handle, address, 1, NativeMethods.PageExecuteReadWrite, out var old));
            try
            {
                Assert.True(NativeMethods.WriteProcessMemory(handle, address, [value], 1, out var count));
                Assert.Equal((nuint)1, count);
                Assert.True(NativeMethods.FlushInstructionCache(handle, address, 1));
            }
            finally { _ = NativeMethods.VirtualProtectEx(handle, address, 1, old, out _); }
        }
        finally { _ = NativeMethods.CloseHandle(handle); }
    }
}
