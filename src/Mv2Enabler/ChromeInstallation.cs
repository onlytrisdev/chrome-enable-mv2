using System.Diagnostics;

namespace Mv2Enabler;

internal sealed record ChromeInstallation(string ExecutablePath, string DllPath, string Version);

internal static class ChromeInstallationFinder
{
    public static ChromeInstallation Find(string? explicitExecutable = null, string? explicitDll = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDll))
        {
            var suppliedDll = Path.GetFullPath(explicitDll);
            if (!File.Exists(suppliedDll))
            {
                throw new FileNotFoundException("chrome.dll was not found.", suppliedDll);
            }

            var executable = explicitExecutable is null ? FindChromeExecutable() : Path.GetFullPath(explicitExecutable);
            return new ChromeInstallation(executable, suppliedDll, FileVersionInfo.GetVersionInfo(suppliedDll).FileVersion ?? "unknown");
        }

        var chrome = explicitExecutable is null ? FindChromeExecutable() : Path.GetFullPath(explicitExecutable);
        if (!File.Exists(chrome))
        {
            throw new FileNotFoundException("chrome.exe was not found.", chrome);
        }

        var version = FileVersionInfo.GetVersionInfo(chrome).FileVersion
            ?? throw new InvalidDataException("Unable to read the Chrome file version.");
        var applicationDirectory = Path.GetDirectoryName(chrome)
            ?? throw new InvalidDataException("Chrome application directory is invalid.");
        var dll = Path.Combine(applicationDirectory, version, "chrome.dll");
        if (!File.Exists(dll))
        {
            dll = Directory.EnumerateFiles(applicationDirectory, "chrome.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("No versioned chrome.dll was found below the Chrome application directory.");
        }

        return new ChromeInstallation(chrome, Path.GetFullPath(dll), version);
    }

    private static string FindChromeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome was not found in a standard installation location.");
    }
}
