using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Mv2Enabler;

internal static class DesktopShortcutService
{
    private const string ShortcutDescription = "Launch Chrome with the Manifest V2 RAM patch";

    public static string CreateForLauncher(string executablePath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
        {
            throw new DirectoryNotFoundException("The Desktop folder could not be found.");
        }

        var shortcutPath = Path.Combine(desktop, "Chrome MV2 Launcher.lnk");
        if (File.Exists(shortcutPath) && !IsOwnedShortcut(shortcutPath))
        {
            throw new IOException("A different shortcut already uses the Chrome MV2 Launcher name on the Desktop.");
        }

        Create(shortcutPath, executablePath, "--auto-launch");
        return shortcutPath;
    }

    internal static bool IsOwnedShortcut(string shortcutPath)
    {
        var linkObject = Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("00021401-0000-0000-C000-000000000046"))!);
        if (linkObject is null)
        {
            return false;
        }

        try
        {
            ((IPersistFile)linkObject).Load(shortcutPath, 0);
            var description = new StringBuilder(256);
            ((IShellLinkW)linkObject).GetDescription(description, description.Capacity);
            return string.Equals(description.ToString(), ShortcutDescription, StringComparison.Ordinal);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(linkObject);
        }
    }

    internal static void Create(string shortcutPath, string executablePath, string arguments)
    {
        var target = Path.GetFullPath(executablePath);
        if (!File.Exists(target))
        {
            throw new FileNotFoundException("The launcher executable was not found.", target);
        }

        var shortcut = Path.GetFullPath(shortcutPath);
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        var linkObject = Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("00021401-0000-0000-C000-000000000046"))!);
        if (linkObject is null)
        {
            throw new COMException("Windows could not create a shell shortcut.");
        }

        try
        {
            var link = (IShellLinkW)linkObject;
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(ShortcutDescription);
            link.SetIconLocation(target, 0);
            ((IPersistFile)linkObject).Save(shortcut, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(linkObject);
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maximum, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maximum);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory(StringBuilder directory, int maximum);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(StringBuilder arguments, int maximum);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(StringBuilder path, int maximum, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
