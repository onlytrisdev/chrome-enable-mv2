using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Principal;

namespace Mv2Enabler;

internal enum ChromeSessionState { NoChrome, Patched, Unpatched, Partial, Unknown }

internal sealed record ChromeSessionInspection(ChromeSessionState State, int? ProcessId = null,
    long StartTimeUtcTicks = 0)
{
    internal bool CanRestart => State == ChromeSessionState.Unpatched && ProcessId.HasValue;
}

internal static class RunningChromeSession
{
    internal static ChromeSessionInspection Inspect(ChromeInstallation installation, IReadOnlyList<string> arguments)
    {
        using var scope = ChromeProcessScope.Capture(installation.ExecutablePath);
        var processes = scope.Processes;
        try
        {
            if (scope.IsClear) return new(ChromeSessionState.NoChrome);
            if (scope.UnidentifiedCount != 0) return new(ChromeSessionState.Unknown);
            if (!IsBrowserRoot(arguments) || UserDataDirectory(arguments) is not { } directory)
                return new(ChromeSessionState.Unknown);
            var executable = installation.ExecutablePath;
            ChromeSessionInspection? result = null;
            var children = new List<Process>();
            foreach (var process in processes)
            {
                if (process.HasExited) continue;
                var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)process.Id);
                if (handle == IntPtr.Zero)
                {
                    if (process.HasExited) continue;
                    return new(ChromeSessionState.Unknown);
                }
                try
                {
                    var commandLine = ReadCommandLine(handle);
                    if (commandLine is null)
                    {
                        if (process.HasExited) continue;
                        return new(ChromeSessionState.Unknown);
                    }
                    var runningArguments = ParseCommandLine(commandLine).Skip(1).ToArray();
                    if (!IsBrowserRoot(runningArguments)) { children.Add(process); continue; }
                    var path = new StringBuilder(32768);
                    var size = (uint)path.Capacity;
                    if (result is not null || !IsCurrentUserSession(process, handle) ||
                        !NativeMethods.QueryFullProcessImageNameW(handle, 0, path, ref size) ||
                        !Path.GetFullPath(path.ToString()).Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase) ||
                        !directory.Equals(UserDataDirectory(runningArguments), StringComparison.OrdinalIgnoreCase))
                        return new(ChromeSessionState.Unknown);
                    var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(item =>
                        item.ModuleName.Equals("chrome.dll", StringComparison.OrdinalIgnoreCase));
                    if (module is null) return new(ChromeSessionState.Unknown);
                    var report = AnalysisService.Analyze(ChromeInstallationFinder.Find(executable, module.FileName));
                    if (!report.Success || report.Target is not { State: PatchState.Original } target)
                        return new(ChromeSessionState.Unknown);
                    var readHandle = NativeMethods.OpenProcess(NativeMethods.ProcessVmRead, false, (uint)process.Id);
                    if (readHandle == IntPtr.Zero) return new(ChromeSessionState.Unknown);
                    try
                    {
                        result = new(ClassifyPatchSet(target, rva => ReadModuleByte(readHandle, module, rva)),
                            process.Id, process.StartTime.ToUniversalTime().Ticks);
                    }
                    finally { _ = NativeMethods.CloseHandle(readHandle); }
                }
                finally { _ = NativeMethods.CloseHandle(handle); }
            }
            if (result?.ProcessId is not { } rootId) return new(ChromeSessionState.Unknown);
            var parents = ChromeProcessScope.ReadProcessParents();
            foreach (var child in children)
            {
                if (!child.HasExited && (child.StartTime.ToUniversalTime().Ticks < result.StartTimeUtcTicks ||
                    !ChromeProcessScope.IsDescendant(child.Id, rootId, parents))) return new(ChromeSessionState.Unknown);
            }
            return result;
        }
        catch (Exception error) when (ChromeProcessScope.IsInspectionFailure(error)) { return new(ChromeSessionState.Unknown); }
    }

    private static bool IsCurrentUserSession(Process process, IntPtr handle)
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (process.SessionId != currentProcess.SessionId || !NativeMethods.OpenProcessToken(handle, 0x0008, out var token)) return false;
        try
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            using var identity = new WindowsIdentity(token);
            return identity.User is not null && currentIdentity.User is not null && identity.User == currentIdentity.User;
        }
        finally { _ = NativeMethods.CloseHandle(token); }
    }

    internal static ChromeSessionState ClassifyPatchSet(PatchTarget target, Func<int, byte?> readByte)
    {
        if (target.State != PatchState.Original) return ChromeSessionState.Unknown;
        var original = true;
        var patched = true;
        foreach (var edit in ChromeDebugLauncher.GetEdits(target))
        {
            var value = readByte(edit.PatchRva);
            if (value is null || value != edit.ExpectedByte && value != edit.ReplacementByte)
                return ChromeSessionState.Unknown;
            original &= value == edit.ExpectedByte;
            patched &= value == edit.ReplacementByte;
        }
        return original ? ChromeSessionState.Unpatched : patched ? ChromeSessionState.Patched : ChromeSessionState.Partial;
    }

    private static byte? ReadModuleByte(IntPtr handle, ProcessModule module, int rva)
    {
        if (rva < 0 || rva >= module.ModuleMemorySize) return null;
        var value = new byte[1];
        return NativeMethods.ReadProcessMemory(handle, new IntPtr(checked(module.BaseAddress.ToInt64() + rva)),
            value, 1, out var count) && count == 1 ? value[0] : null;
    }

    internal static bool CloseUnpatchedSession(ChromeInstallation installation, IReadOnlyList<string> arguments, bool forceClose, TimeSpan timeout,
        ChromeSessionInspection expectedSession)
    {
        var session = Inspect(installation, arguments);
        if (!session.CanRestart || !expectedSession.CanRestart ||
            session.ProcessId != expectedSession.ProcessId || session.StartTimeUtcTicks != expectedSession.StartTimeUtcTicks)
            return false;
        // Pin the process object to prevent PID reuse during confirmation-to-close dispatch.
        var anchor = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)session.ProcessId!.Value);
        if (anchor == IntPtr.Zero) return false;
        try
        {
            var freshReport = AnalysisService.Analyze(ChromeInstallationFinder.Find(installation.ExecutablePath));
            if (!freshReport.Success || freshReport.Target is not { State: PatchState.Original }) return false;
            using var process = Process.GetProcessById(session.ProcessId.Value);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != session.StartTimeUtcTicks) return false;
            var recheck = Inspect(installation, arguments);
            if (!recheck.CanRestart || recheck.ProcessId != session.ProcessId || recheck.StartTimeUtcTicks != session.StartTimeUtcTicks)
                return false;
            if (forceClose) process.Kill(entireProcessTree: true);
            else
            {
                NativeMethods.EnumWindows((window, parameter) =>
                {
                    _ = NativeMethods.GetWindowThreadProcessId(window, out var pid);
                    var className = new StringBuilder(256);
                    if (pid == (uint)process.Id && NativeMethods.GetClassNameW(window, className, className.Capacity) > 0 &&
                        className.ToString() == "Chrome_WidgetWin_1")
                        _ = NativeMethods.PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    return true;
                }, IntPtr.Zero);
            }
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                using var remaining = ChromeProcessScope.Capture(installation.ExecutablePath);
                if (remaining.IsClear) return true;
                Thread.Sleep(100);
            }
            return false;
        }
        catch (Exception error) when (ChromeProcessScope.IsInspectionFailure(error)) { return false; }
        finally { _ = NativeMethods.CloseHandle(anchor); }
    }

    // Read-only inspection: never repair or patch an arbitrary running Chrome process.
    internal static bool TryOpenPatchedWindow(ChromeInstallation installation, IReadOnlyList<string> arguments)
    {
        if (!IsBrowserRoot(arguments)) return false;
        using var scope = ChromeProcessScope.Capture(installation.ExecutablePath);
        var processes = scope.Processes;
        try
        {
            if (scope.IsClear || scope.UnidentifiedCount != 0) return false;
            var executable = installation.ExecutablePath;
            var requestedDirectory = UserDataDirectory(arguments);
            if (requestedDirectory is null) return false;
            foreach (var process in processes)
            {
                if (TryOpen(process, executable, requestedDirectory, arguments)) return true;
            }
            return false;
        }
        catch (Exception error) when (ChromeProcessScope.IsInspectionFailure(error))
        {
            return false;
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
            if (!IsCurrentUserSession(process, handle) || !NativeMethods.QueryFullProcessImageNameW(handle, 0, path, ref size) ||
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
        catch (Exception error) when (ChromeProcessScope.IsInspectionFailure(error))
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

}
