using System.Runtime.InteropServices;
using System.Text;

namespace Mv2Enabler;

internal static class WindowsFolderDialog
{
    public static IntPtr WaitForDialog(uint processId, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var dialog = FindDialog(processId);
            if (dialog != IntPtr.Zero)
            {
                return dialog;
            }

            Thread.Sleep(100);
        }

        return IntPtr.Zero;
    }

    public static string GetTitle(IntPtr dialog)
    {
        var title = new StringBuilder(512);
        _ = NativeMethods.GetWindowTextW(dialog, title, title.Capacity);
        return title.ToString();
    }

    public static bool SelectFolder(IntPtr dialog, string folderPath)
    {
        // Use the dialog's filename field, not global Ctrl+L/clipboard input.
        // Focus can remain in another window, silently selecting the previous
        // directory and making a runtime test exercise the wrong extension.
        folderPath = Path.GetFullPath(folderPath);
        var edit = NativeMethods.GetDlgItem(dialog, 0x480); // edt1 (common dialog)
        if (edit == IntPtr.Zero || !Directory.Exists(folderPath)) return false;
        var text = Marshal.StringToHGlobalUni(folderPath);
        try
        {
            if (NativeMethods.SendMessageW(edit, 0x000c, IntPtr.Zero, text) == IntPtr.Zero) return false; // WM_SETTEXT
        }
        finally { Marshal.FreeHGlobal(text); }
        var readback = Marshal.AllocHGlobal((folderPath.Length + 2) * 2);
        try
        {
            Marshal.WriteInt16(readback, 0);
            if (NativeMethods.SendMessageW(edit, 0x000d, new IntPtr(folderPath.Length + 2), readback).ToInt64() != folderPath.Length) return false; // WM_GETTEXT
            if (!string.Equals(Marshal.PtrToStringUni(readback), folderPath, StringComparison.OrdinalIgnoreCase)) return false;
        }
        finally { Marshal.FreeHGlobal(readback); }
        var selectButton = NativeMethods.GetDlgItem(dialog, 1);
        if (selectButton == IntPtr.Zero) selectButton = FindSelectFolderButton(dialog);
        if (selectButton != IntPtr.Zero)
        {
            _ = NativeMethods.PostMessageW(selectButton, NativeMethods.BmClick, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            _ = NativeMethods.PostMessageW(dialog, NativeMethods.WmCommand, new IntPtr(1), IntPtr.Zero);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3) && NativeMethods.IsWindow(dialog))
        {
            Thread.Sleep(100);
        }

        return !NativeMethods.IsWindow(dialog);
    }

    private static IntPtr FindDialog(uint processId)
    {
        var result = IntPtr.Zero;
        var chromeProcessIds = System.Diagnostics.Process.GetProcessesByName("chrome")
            .Select(process =>
            {
                var id = process.Id;
                process.Dispose();
                return checked((uint)id);
            })
            .Append(processId)
            .ToHashSet();
        NativeMethods.EnumWindows((window, parameter) =>
        {
            _ = NativeMethods.GetWindowThreadProcessId(window, out var ownerProcessId);
            if (!chromeProcessIds.Contains(ownerProcessId) || !NativeMethods.IsWindowVisible(window))
            {
                return true;
            }

            var className = new StringBuilder(64);
            _ = NativeMethods.GetClassNameW(window, className, className.Capacity);
            if (string.Equals(className.ToString(), "#32770", StringComparison.Ordinal))
            {
                result = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr FindSelectFolderButton(IntPtr dialog)
    {
        var result = IntPtr.Zero;
        NativeMethods.EnumChildWindows(dialog, (window, parameter) =>
        {
            var className = new StringBuilder(64);
            _ = NativeMethods.GetClassNameW(window, className, className.Capacity);
            if (!string.Equals(className.ToString(), "Button", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var title = new StringBuilder(256);
            _ = NativeMethods.GetWindowTextW(window, title, title.Capacity);
            if (title.ToString().Contains("Select Folder", StringComparison.OrdinalIgnoreCase) ||
                title.ToString().Contains("Select", StringComparison.OrdinalIgnoreCase))
            {
                result = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

}
