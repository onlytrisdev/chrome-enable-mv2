namespace Mv2Enabler.Tests;

public sealed class ChromeProcessScopeTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", nameof(ExecutableMatch.Selected))]
    [InlineData(@"c:\PROGRAM FILES\Google\Chrome\Application\CHROME.EXE", nameof(ExecutableMatch.Selected))]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\.\chrome.exe", nameof(ExecutableMatch.Selected))]
    [InlineData(@"C:\Program Files\Helium\chrome.exe", nameof(ExecutableMatch.Other))]
    [InlineData(@"C:\Program Files\Google\Chrome Beta\Application\chrome.exe", nameof(ExecutableMatch.Other))]
    [InlineData(@"C:\Program Files\Google\Chrome Dev\Application\chrome.exe", nameof(ExecutableMatch.Other))]
    [InlineData(@"C:\Portable\chrome.exe", nameof(ExecutableMatch.Other))]
    [InlineData(@"C:\Program Files\Google\Chrome\Application2\chrome.exe", nameof(ExecutableMatch.Other))]
    [InlineData(null, nameof(ExecutableMatch.Unknown))]
    [InlineData("", nameof(ExecutableMatch.Unknown))]
    [InlineData("chrome.exe", nameof(ExecutableMatch.Unknown))]
    [InlineData("\0invalid", nameof(ExecutableMatch.Unknown))]
    public void ExecutableIdentityDistinguishesSamePathForeignBrowsersAndUnidentifiedCandidates(string? actual, string expected) =>
        Assert.Equal(expected, ChromeProcessScope.ClassifyExecutable(@"C:\Program Files\Google\Chrome\Application\chrome.exe", actual).ToString());
}
