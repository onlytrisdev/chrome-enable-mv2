namespace Mv2Enabler.Tests;

public sealed class RunningChromeSessionTests
{
    private static PatchTarget Target() => new("test", "test", 0, 0, 100, 0x7f, 0xeb,
        PatchState.Original, Enumerable.Range(1, 8).Select(index =>
            new PatchEdit(index, 100 + index, 0x75, 0xeb)).ToArray(), []);

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
