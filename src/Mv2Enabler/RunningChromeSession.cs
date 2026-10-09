using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mv2Enabler;

internal static class RunningChromeSession
{
    // Read-only inspection: never repair or patch an arbitrary running Chrome process.
    internal static bool TryOpenPatchedWindow(IReadOnlyList<string> arguments)
    {
        if (!IsBrowserRoot(arguments)) return false;
        var processes = Process.GetProcessesByName("chrome");
        try
        {
            if (processes.Length == 0) return false;
            var executable = ChromeInstallationFinder.Find().ExecutablePath;
            var requestedDirectory = UserDataDirectory(arguments);
            if (requestedDirectory is null) return false;
            foreach (var process in processes)
            {
                if (TryOpen(process, executable, requestedDirectory, arguments)) return true;
            }
            return false;
        }
        catch (Exception error) when (IsInspectionFailure(error))
        {
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool TryOpen(Process process, string executable, string requestedDirectory,
        IReadOnlyList<string> arguments)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessVmRead | NativeMethods.ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == IntPtr.Zero) return false;
        try
        {
            var path = new StringBuilder(32768);
            var size = (uint)path.Capacity;
            if (!NativeMethods.QueryFullProcessImageNameW(handle, 0, path, ref size) ||
                !Path.GetFullPath(path.ToString()).Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                return false;
            var commandLine = ReadCommandLine(handle);
            if (commandLine is null) return false;
            var runningArguments = ParseCommandLine(commandLine).Skip(1).ToArray();
            if (!IsBrowserRoot(runningArguments) ||
                !requestedDirectory.Equals(UserDataDirectory(runningArguments), StringComparison.OrdinalIgnoreCase))
                return false;

            var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(item =>
                item.ModuleName.Equals("chrome.dll", StringComparison.OrdinalIgnoreCase));
            if (module is null) return false;
            // Chrome may have updated on disk. Analyze the DLL actually loaded by this session,
            // not the newest installed version's RVAs. Missing/unsupported old DLLs fail closed.
            var installation = ChromeInstallationFinder.Find(executable, module.FileName);
            var report = AnalysisService.Analyze(installation);
            if (!report.Success || report.Target is not { State: PatchState.Original } target) return false;
            byte? ReadByte(int rva)
            {
                if (rva < 0 || rva >= module.ModuleMemorySize) return null;
                var value = new byte[1];
                return NativeMethods.ReadProcessMemory(handle, new IntPtr(checked(module.BaseAddress.ToInt64() + rva)),
                    value, 1, out var count) && count == 1 ? value[0] : null;
            }
            if (!IsPatchSetApplied(target, ReadByte) || process.HasExited) return false;

            var start = new ProcessStartInfo(executable) { UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)! };
            start.ArgumentList.Add("--new-window");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var activation = Process.Start(start);
            if (activation is null) return false;
            // The short-lived Chrome client must hand off to the same verified browser. If the
            // browser exits during dispatch, stop only our client instead of leaving a new,
            // unpatched browser behind. Never terminate the pre-existing browser.
            if (!activation.WaitForExit(5000))
            {
                if (!activation.HasExited) { activation.Kill(entireProcessTree: true); activation.WaitForExit(5000); }
                return false;
            }
            return activation.ExitCode == 0 && !process.HasExited && IsPatchSetApplied(target, ReadByte);
        }
        catch (Exception error) when (IsInspectionFailure(error))
        {
            return false;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    internal static bool IsPatchSetApplied(PatchTarget target, Func<int, byte?> readByte) =>
        target.State == PatchState.Original && ChromeDebugLauncher.GetEdits(target)
            .All(edit => readByte(edit.PatchRva) == edit.ReplacementByte);

    internal static bool IsBrowserRoot(IReadOnlyList<string> arguments) =>
        !arguments.TakeWhile(argument => argument != "--").Select(SwitchText)
            .Any(argument => argument.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith("type=", StringComparison.OrdinalIgnoreCase));

    private static string SwitchText(string argument) => argument.StartsWith("--", StringComparison.Ordinal)
        ? argument[2..] : argument.StartsWith('-') || argument.StartsWith('/') ? argument[1..] : "";

    internal static string? UserDataDirectory(IReadOnlyList<string> arguments)
    {
        string? directory = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--") break;
            argument = SwitchText(argument);
            if (argument.StartsWith("user-data-dir=", StringComparison.OrdinalIgnoreCase))
                directory = argument["user-data-dir=".Length..];
            else if (argument.Equals("user-data-dir", StringComparison.OrdinalIgnoreCase))
                directory = ++index < arguments.Count ? arguments[index] : "";
        }
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data");
        // The other process's working directory is unknown; relative paths cannot prove a match.
        if (!Path.IsPathFullyQualified(directory)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    internal static string[] ParseCommandLine(string commandLine)
    {
        var pointer = NativeMethods.CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) throw NativeMethods.Error("Could not parse Chrome's command line");
        try
        {
            return Enumerable.Range(0, count).Select(index =>
                Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size)) ?? "").ToArray();
        }
        finally { _ = NativeMethods.LocalFree(pointer); }
    }

    private static string? ReadCommandLine(IntPtr process)
    {
        // ProcessCommandLineInformation (60) is optional native Windows functionality. Do not
        // fall back to hardcoded PEB offsets if it is unavailable or its result is unexpected.
        _ = NativeMethods.NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out var length);
        if (length < 16 || length > 1024 * 1024) return null;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (NativeMethods.NtQueryInformationProcess(process, 60, buffer, length, out _) < 0) return null;
            var byteLength = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
            var offset = text.ToInt64() - buffer.ToInt64();
            if (byteLength == 0 || byteLength % 2 != 0 || byteLength > length ||
                offset < (IntPtr.Size == 8 ? 16 : 8) || offset > length - byteLength) return null;
            return Marshal.PtrToStringUni(text, byteLength / 2);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool IsInspectionFailure(Exception error) => error is
        Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or EntryPointNotFoundException or OverflowException;
}
