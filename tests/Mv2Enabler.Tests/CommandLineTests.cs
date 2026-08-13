namespace Mv2Enabler.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("say\"hello", "\"say\\\"hello\"")]
    public void QuoteArgumentUsesWindowsCommandLineRules(string input, string expected)
    {
        Assert.Equal(expected, ChromeDebugLauncher.QuoteArgument(input));
    }
}
