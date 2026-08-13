using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Mv2Enabler;

internal static class NativeMethods
{
    internal const uint DebugOnlyThisProcess = 0x00000002;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint CreateProcessDebugEvent = 3;
    internal const uint ExitProcessDebugEvent = 5;
    internal const uint LoadDllDebugEvent = 6;
    internal const uint ExceptionDebugEvent = 1;
    internal const uint DbgContinue = 0x00010002;
    internal const uint DbgExceptionNotHandled = 0x80010001;
    internal const uint ExceptionBreakpoint = 0x80000003;
    internal const uint PageExecuteReadWrite = 0x40;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessVmWrite = 0x0020;
    internal const uint ProcessVmOperation = 0x0008;
    internal const uint BmClick = 0x00f5;
    internal const uint WmCommand = 0x0111;
    internal const uint InputKeyboard = 1;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint KeyEventUnicode = 0x0004;
    internal const ushort VkControl = 0x11;
    internal const ushort VkL = 0x4c;
    internal const ushort VkV = 0x56;
    internal const ushort VkReturn = 0x0d;

    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal uint cb;
        internal string? lpReserved;
        internal string? lpDesktop;
        internal string? lpTitle;
        internal uint dwX;
        internal uint dwY;
        internal uint dwXSize;
        internal uint dwYSize;
        internal uint dwXCountChars;
        internal uint dwYCountChars;
        internal uint dwFillAttribute;
        internal uint dwFlags;
        internal ushort wShowWindow;
        internal ushort cbReserved2;
        internal IntPtr lpReserved2;
        internal IntPtr hStdInput;
        internal IntPtr hStdOutput;
        internal IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal IntPtr hProcess;
        internal IntPtr hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateProcessDebugInfo
    {
        internal IntPtr hFile;
        internal IntPtr hProcess;
        internal IntPtr hThread;
        internal IntPtr lpBaseOfImage;
        internal uint dwDebugInfoFileOffset;
        internal uint nDebugInfoSize;
        internal IntPtr lpThreadLocalBase;
        internal IntPtr lpStartAddress;
        internal IntPtr lpImageName;
        internal ushort fUnicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LoadDllDebugInfo
    {
        internal IntPtr hFile;
        internal IntPtr lpBaseOfDll;
        internal uint dwDebugInfoFileOffset;
        internal uint nDebugInfoSize;
        internal IntPtr lpImageName;
        internal ushort fUnicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint type;
        internal InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort virtualKey;
        internal ushort scanCode;
        internal uint flags;
        internal uint time;
        internal UIntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    internal struct DebugEvent
    {
        [FieldOffset(0)] internal uint dwDebugEventCode;
        [FieldOffset(4)] internal uint dwProcessId;
        [FieldOffset(8)] internal uint dwThreadId;
        [FieldOffset(16)] internal uint exceptionCode;
        [FieldOffset(16)] internal CreateProcessDebugInfo createProcessInfo;
        [FieldOffset(16)] internal LoadDllDebugInfo loadDll;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WaitForDebugEventEx(out DebugEvent lpDebugEvent, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ContinueDebugEvent(uint dwProcessId, uint dwThreadId, uint dwContinueStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DebugSetProcessKillOnExit([MarshalAs(UnmanagedType.Bool)] bool killOnExit);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DebugActiveProcessStop(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualProtectEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nuint dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandleW(
        IntPtr hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(IntPtr window, StringBuilder title, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDlgItem(IntPtr dialog, int itemId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr memory);

    internal static Win32Exception Error(string operation) =>
        new(Marshal.GetLastWin32Error(), operation);
}
