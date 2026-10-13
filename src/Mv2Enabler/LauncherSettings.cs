namespace Mv2Enabler;

internal static class LauncherSettings
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChromeMv2Launcher", "restart-unpatched.txt");

    internal static bool RestartUnpatchedEnabled
    {
        get => ReadRestartSetting(SettingsPath);
        set => WriteRestartSetting(SettingsPath, value);
    }

    internal static bool ReadRestartSetting(string path)
    {
        try { return File.Exists(path) && File.ReadAllText(path).Trim() == "enabled"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static void WriteRestartSetting(string path, bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, enabled ? "enabled" : "disabled");
    }
}
