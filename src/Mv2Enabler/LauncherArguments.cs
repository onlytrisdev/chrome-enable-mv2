namespace Mv2Enabler;

internal sealed record LauncherArguments(bool AutoLaunch, bool ShowUi, IReadOnlyList<string> ChromeArguments)
{
    internal static LauncherArguments Parse(IReadOnlyList<string> arguments)
    {
        var autoLaunch = false;
        var showUi = false;
        var chrome = new List<string>();
        var chromeOnly = false;
        foreach (var argument in arguments)
        {
            if (!chromeOnly && argument == "--")
                chromeOnly = true;
            else if (!chromeOnly && argument.Equals("--auto-launch", StringComparison.OrdinalIgnoreCase))
                autoLaunch = true;
            else if (!chromeOnly && argument.Equals("--show-ui", StringComparison.OrdinalIgnoreCase))
                showUi = true;
            else
                chrome.Add(argument);
        }
        return new(autoLaunch && !showUi, showUi, chrome);
    }
}
