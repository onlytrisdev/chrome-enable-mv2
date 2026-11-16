using System.Diagnostics;

namespace Mv2Enabler.Tests;

[Collection("Chrome process integration")]
public sealed class BrowserCoexistenceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void ForeignChromeExeDoesNotBlockLaunchOrBecomeAnUnknownChromeSession()
    {
        var installation = ChromeInstallationFinder.Find();
        using var foreign = new ForeignChromeFixture();
        ChromeDebugLauncher.EnsureChromeIsNotRunning(installation);
        Assert.Equal(ChromeSessionState.NoChrome, RunningChromeSession.Inspect(installation, []).State);
        Assert.False(RunningChromeSession.TryOpenPatchedWindow(installation, []));
        Assert.False(RunningChromeSession.CloseUnpatchedSession(installation, [], true, TimeSpan.FromMilliseconds(100),
            new(ChromeSessionState.Unpatched, foreign.Process.Id, foreign.Process.StartTime.ToUniversalTime().Ticks)));
        Assert.False(foreign.Process.HasExited);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ExplicitSelectedExecutableRemainsBlockedAndAnUnrelatedPeerIsNotOwned()
    {
        using var selected = new ForeignChromeFixture();
        using var peer = selected.StartPeer();
        try
        {
            var installation = new ChromeInstallation(selected.Executable, "unused.dll", "test");
            Assert.Throws<InvalidOperationException>(() => ChromeDebugLauncher.EnsureChromeIsNotRunning(installation));
            Assert.Equal(ChromeSessionState.Unknown, RunningChromeSession.Inspect(installation, []).State);
            using var all = ChromeProcessScope.Capture(selected.Executable);
            Assert.Equal(2, all.Processes.Count);
            using var owned = ChromeProcessScope.CaptureOwnedTree(selected.Process);
            Assert.Equal(selected.Process.Id, Assert.Single(owned.Processes).Id);
            Assert.DoesNotContain(owned.Processes, p => p.Id == peer.Id);
            Assert.False(peer.HasExited);
        }
        finally { if (!peer.HasExited) { peer.Kill(); peer.WaitForExit(5000); } }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RealMv2UiInstallationAndRestartLeaveForeignBrowserAlive()
    {
        var installation = ChromeInstallationFinder.Find();
        var report = AnalysisService.Analyze(installation);
        Assert.True(report.Success, string.Join("\n", report.Diagnostics));
        using var foreign = new ForeignChromeFixture();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "test-extension", "manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var result = UiFunctionalTestService.Run(installation, report.Target!,
            Path.Combine(directory.FullName, "test-extension"), TimeSpan.FromSeconds(20));
        Assert.True(result.Success, result.Message + "\n" + result.ExtensionManagerState);
        Assert.True(result.RestartPersistenceVerified);
        Assert.True(result.TemporaryProfileRemoved);
        Assert.False(foreign.Process.HasExited);
        Assert.DoesNotContain(foreign.Process.Id, result.PatchedChromeProcessIds);
    }
}

[CollectionDefinition("Chrome process integration", DisableParallelization = true)]
public sealed class ChromeProcessIntegrationCollection;

internal sealed class ForeignChromeFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mv2-foreign-browser-" + Guid.NewGuid().ToString("N"));
    internal Process Process { get; }
    internal string Executable { get; }

    internal ForeignChromeFixture()
    {
        Directory.CreateDirectory(directory);
        Executable = Path.Combine(directory, "chrome.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe"), Executable);
        Process = StartPeer();
        if (Process.WaitForExit(100)) throw new InvalidOperationException("Browser-name fixture exited early.");
    }

    internal Process StartPeer()
    {
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-n", "300", "127.0.0.1" }) start.ArgumentList.Add(argument);
        return System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned browser-name fixture.");
    }

    public void Dispose()
    {
        try { if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(5000); } }
        finally { Process.Dispose(); }
        Directory.Delete(directory, recursive: true);
    }
}
