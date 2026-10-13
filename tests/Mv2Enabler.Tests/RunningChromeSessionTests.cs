namespace Mv2Enabler.Tests;

public sealed class RunningChromeSessionTests
{
    private static PatchTarget Target() => new("test", "test", 0, 0, 100, 0x7f, 0xeb,
        PatchState.Original, Enumerable.Range(1, 8).Select(index =>
            new PatchEdit(index, 100 + index, 0x75, 0xeb)).ToArray(), []);

    [Fact]
    public void InspectionDistinguishesPartialAndUnknownFromVerifiedOriginal()
    {
        var target = Target();
        var original = ChromeDebugLauncher.GetEdits(target).ToDictionary(edit => edit.PatchRva, edit => edit.ExpectedByte);
        Assert.Equal(ChromeSessionState.Unpatched, RunningChromeSession.ClassifyPatchSet(target, rva => original[rva]));
        Assert.Equal(ChromeSessionState.Patched, RunningChromeSession.ClassifyPatchSet(target, _ => 0xeb));
        Assert.Equal(ChromeSessionState.Partial, RunningChromeSession.ClassifyPatchSet(target, rva => rva == 100 ? (byte)0xeb : original[rva]));
        Assert.Equal(ChromeSessionState.Unknown, RunningChromeSession.ClassifyPatchSet(target, _ => null));
        Assert.Equal(ChromeSessionState.Unknown, RunningChromeSession.ClassifyPatchSet(target, _ => 0xcc));
        foreach (var state in Enum.GetValues<ChromeSessionState>())
            Assert.Equal(state == ChromeSessionState.Unpatched, new ChromeSessionInspection(state, 1).CanRestart);
    }

    [Fact]
    public void RestartPreferenceDefaultsOffAndInvalidContentFailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mv2-settings-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "setting.txt");
        try
        {
            Assert.False(LauncherSettings.ReadRestartSetting(path));
            LauncherSettings.WriteRestartSetting(path, true);
            Assert.True(LauncherSettings.ReadRestartSetting(path));
            LauncherSettings.WriteRestartSetting(path, false);
            Assert.False(LauncherSettings.ReadRestartSetting(path));
            File.WriteAllText(path, "true or arbitrary corrupted setting");
            Assert.False(LauncherSettings.ReadRestartSetting(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void DescendantCheckRejectsForeignOrphanAndCyclicProcessTrees()
    {
        var parents = new Dictionary<int, int> { [2] = 1, [3] = 2, [4] = 99, [5] = 6, [6] = 5 };
        Assert.True(RunningChromeSession.IsDescendant(3, 1, parents));
        Assert.False(RunningChromeSession.IsDescendant(4, 1, parents));
        Assert.False(RunningChromeSession.IsDescendant(5, 1, parents));
        Assert.False(RunningChromeSession.IsDescendant(10, 1, parents));
    }

    [Fact]
    public void CompletePatchSetIsRequiredIncludingEveryAdditionalEdit()
    {
        var target = Target();
        Assert.True(RunningChromeSession.IsPatchSetApplied(target, _ => 0xeb));
        foreach (var edit in ChromeDebugLauncher.GetEdits(target))
        {
            Assert.False(RunningChromeSession.IsPatchSetApplied(target,
                rva => rva == edit.PatchRva ? edit.ExpectedByte : (byte)0xeb));
            Assert.False(RunningChromeSession.IsPatchSetApplied(target,
                rva => rva == edit.PatchRva ? null : (byte)0xeb));
            Assert.False(RunningChromeSession.IsPatchSetApplied(target,
                rva => rva == edit.PatchRva ? (byte)0xcc : (byte)0xeb));
        }
    }

    [Fact]
    public void PatchedDiskImageCannotAuthorizeSessionReuse() =>
        Assert.False(RunningChromeSession.IsPatchSetApplied(Target() with { State = PatchState.AlreadyPatched }, _ => 0xeb));

    [Theory]
    [InlineData("--type=renderer")]
    [InlineData("--type=gpu-process")]
    [InlineData("--type=utility")]
    [InlineData("--type")]
    [InlineData("-type=renderer")]
    [InlineData("/type=renderer")]
    public void SubprocessCannotAuthorizeReuse(string argument) =>
        Assert.False(RunningChromeSession.IsBrowserRoot([argument]));

    [Fact]
    public void BrowserRootAcceptsProfileAndUrlArguments() =>
        Assert.True(RunningChromeSession.IsBrowserRoot(["--profile-directory=Profile 1", "https://example.com"]));

    [Fact]
    public void DirectoryMatchingHandlesQuotesSeparatorsAndLastSwitch()
    {
        var arguments = RunningChromeSession.ParseCommandLine(
            "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --user-data-dir=C:\\old " +
            ChromeDebugLauncher.QuoteArgument("--user-data-dir=C:\\Profile With Spaces\\") +
            " --profile-directory=Default https://example.com");
        Assert.Equal("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", arguments[0]);
        Assert.Equal("C:\\Profile With Spaces", RunningChromeSession.UserDataDirectory(arguments.Skip(1).ToArray()));
        Assert.Equal("https://example.com", arguments[^1]);
        Assert.Equal("C:\\Profile With Spaces", RunningChromeSession.UserDataDirectory(
            ["--user-data-dir", "C:\\Profile With Spaces\\"]));
        Assert.Equal("C:\\Profile With Spaces", RunningChromeSession.UserDataDirectory(
            ["/user-data-dir=C:\\Profile With Spaces"]));
        Assert.NotEqual(RunningChromeSession.UserDataDirectory(["--user-data-dir=C:\\one"]),
            RunningChromeSession.UserDataDirectory(["--user-data-dir=C:\\two"]));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("")]
    [InlineData("C:profile")]
    public void AmbiguousDirectoryCannotAuthorizeReuse(string directory) =>
        Assert.Null(RunningChromeSession.UserDataDirectory(["--user-data-dir=" + directory]));

    [Fact]
    public void DefaultProfileIsStableAndMissingDirectoryIsRejected()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data"), RunningChromeSession.UserDataDirectory([]));
        Assert.Null(RunningChromeSession.UserDataDirectory(["--user-data-dir"]));
    }

    [Fact]
    public void LauncherOptionsPreserveUrlsWithoutSeparatorAndAllowExplicitUi()
    {
        var options = LauncherArguments.Parse(["--auto-launch", "https://example.com", "--profile-directory=Profile 1"]);
        Assert.True(options.AutoLaunch);
        Assert.False(options.ShowUi);
        Assert.Equal(["https://example.com", "--profile-directory=Profile 1"], options.ChromeArguments);
        options = LauncherArguments.Parse(["--auto-launch", "--show-ui", "--", "--auto-launch"]);
        Assert.False(options.AutoLaunch);
        Assert.True(options.ShowUi);
        Assert.Equal(["--auto-launch"], options.ChromeArguments);
    }
}
