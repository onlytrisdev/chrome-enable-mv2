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
        using var clipboard = new ClipboardTextScope(folderPath);
        var dialogThread = NativeMethods.GetWindowThreadProcessId(dialog, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = dialogThread != currentThread && NativeMethods.AttachThreadInput(currentThread, dialogThread, true);
        try
        {
            _ = NativeMethods.SetForegroundWindow(dialog);
            _ = NativeMethods.SetFocus(dialog);
            Thread.Sleep(150);
            SendVirtualKey(NativeMethods.VkControl, keyUp: false);
            SendVirtualKey(NativeMethods.VkL, keyUp: false);
            SendVirtualKey(NativeMethods.VkL, keyUp: true);
            SendVirtualKey(NativeMethods.VkControl, keyUp: true);
            Thread.Sleep(100);
            SendVirtualKey(NativeMethods.VkControl, keyUp: false);
            SendVirtualKey(NativeMethods.VkV, keyUp: false);
            SendVirtualKey(NativeMethods.VkV, keyUp: true);
            SendVirtualKey(NativeMethods.VkControl, keyUp: true);
            SendVirtualKey(NativeMethods.VkReturn, keyUp: false);
            SendVirtualKey(NativeMethods.VkReturn, keyUp: true);
            Thread.Sleep(500);
        }
        finally
        {
            if (attached)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, dialogThread, false);
            }
        }

        var selectButton = FindSelectFolderButton(dialog);
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

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        SendKeyboardInput(virtualKey, '\0', keyUp ? NativeMethods.KeyEventKeyUp : 0);
    }

    private static void SendKeyboardInput(ushort virtualKey, char scanCode, uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.Input
            {
                type = NativeMethods.InputKeyboard,
                union = new NativeMethods.InputUnion
                {
                    keyboard = new NativeMethods.KeyboardInput
                    {
                        virtualKey = virtualKey,
                        scanCode = scanCode,
                        flags = flags
                    }
                }
            }
        };

        if (NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.Input>()) != 1)
        {
            throw NativeMethods.Error("SendInput failed while controlling the folder dialog");
        }
    }
}
