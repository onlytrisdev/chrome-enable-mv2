using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mv2Enabler;

internal sealed record LaunchResult(uint ProcessId, string ModulePath, long RemoteAddressValue, string RemoteAddress, byte OriginalByte, byte ReplacementByte);

internal static class ChromeDebugLauncher
{
    public static LaunchResult Launch(
        ChromeInstallation installation,
        PatchTarget target,
        IReadOnlyList<string> chromeArguments,
        TimeSpan timeout)
    {
        EnsureChromeIsNotRunning();
        if (target.State == PatchState.AlreadyPatched)
        {
            throw new InvalidOperationException("chrome.dll is already modified on disk; restore the signed original before using the RAM launcher.");
        }

        var commandLine = new StringBuilder(BuildCommandLine(installation.ExecutablePath, chromeArguments));
        var startupInfo = new NativeMethods.StartupInfo
        {
            cb = checked((uint)Marshal.SizeOf<NativeMethods.StartupInfo>())
        };

        if (!NativeMethods.CreateProcessW(
                installation.ExecutablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.DebugOnlyThisProcess | NativeMethods.CreateUnicodeEnvironment,
                IntPtr.Zero,
                Path.GetDirectoryName(installation.ExecutablePath),
                ref startupInfo,
                out var processInfo))
        {
            throw NativeMethods.Error("CreateProcessW failed");
        }

        var detached = false;
        try
        {
            if (!NativeMethods.DebugSetProcessKillOnExit(false))
            {
                throw NativeMethods.Error("DebugSetProcessKillOnExit failed");
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var remaining = timeout - stopwatch.Elapsed;
                var waitMilliseconds = checked((uint)Math.Clamp((long)remaining.TotalMilliseconds, 1, 1000));
                if (!NativeMethods.WaitForDebugEventEx(out var debugEvent, waitMilliseconds))
                {
                    var error = Marshal.GetLastWin32Error();
                    const int errorSemTimeout = 121;
                    if (error == errorSemTimeout)
                    {
                        continue;
                    }

                    throw new System.ComponentModel.Win32Exception(error, "WaitForDebugEventEx failed");
                }

                var continueStatus = debugEvent.dwDebugEventCode == NativeMethods.ExceptionDebugEvent &&
                                     debugEvent.exceptionCode != NativeMethods.ExceptionBreakpoint
                    ? NativeMethods.DbgExceptionNotHandled
                    : NativeMethods.DbgContinue;

                LaunchResult? result = null;
                try
                {
                    if (debugEvent.dwDebugEventCode == NativeMethods.CreateProcessDebugEvent)
                    {
                        CloseEventFileHandle(debugEvent.createProcessInfo.hFile);
                    }
                    else if (debugEvent.dwDebugEventCode == NativeMethods.LoadDllDebugEvent)
                    {
                        var modulePath = GetPathFromHandle(debugEvent.loadDll.hFile);
                        try
                        {
                            if (PathsEqual(modulePath, installation.DllPath))
                            {
                                result = PatchRemoteProcess(
                                    processInfo.hProcess,
                                    processInfo.dwProcessId,
                                    debugEvent.loadDll.lpBaseOfDll,
                                    installation.DllPath,
                                    target);
                            }
                        }
                        finally
                        {
                            CloseEventFileHandle(debugEvent.loadDll.hFile);
                        }
                    }
                    else if (debugEvent.dwDebugEventCode == NativeMethods.ExitProcessDebugEvent)
                    {
                        throw new InvalidOperationException("Chrome exited before chrome.dll could be patched.");
                    }
                }
                finally
                {
                    if (!NativeMethods.ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, continueStatus))
                    {
                        throw NativeMethods.Error("ContinueDebugEvent failed");
                    }
                }

                if (result is not null)
                {
                    if (!NativeMethods.DebugActiveProcessStop(processInfo.dwProcessId))
                    {
                        throw NativeMethods.Error("DebugActiveProcessStop failed after the RAM patch was applied");
                    }

                    detached = true;
                    return result;
                }
            }

            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} seconds waiting for chrome.dll.");
        }
        catch
        {
            if (!detached)
            {
                _ = NativeMethods.TerminateProcess(processInfo.hProcess, 0x4d5632);
            }

            throw;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processInfo.hThread);
            _ = NativeMethods.CloseHandle(processInfo.hProcess);
        }
    }

    private static LaunchResult PatchRemoteProcess(
        IntPtr processHandle,
        uint processId,
        IntPtr moduleBase,
        string modulePath,
        PatchTarget target)
    {
        var edits = GetEdits(target);
        var observed = new List<(IntPtr Address, byte Original, PatchEdit Edit)>();
        foreach (var edit in edits)
        {
            var address = new IntPtr(checked(moduleBase.ToInt64() + edit.PatchRva));
            var current = new byte[1];
            if (!NativeMethods.ReadProcessMemory(processHandle, address, current, 1, out var bytesRead) || bytesRead != 1)
            {
                throw NativeMethods.Error("ReadProcessMemory failed at a planned MV2 target");
            }

            if (current[0] != edit.ExpectedByte)
            {
                throw new InvalidOperationException(
                    $"Remote target verification failed at RVA 0x{edit.PatchRva:X}: read 0x{current[0]:X2}, expected 0x{edit.ExpectedByte:X2}.");
            }

            observed.Add((address, current[0], edit));
        }

        foreach (var item in observed)
        {
            WriteVerifiedByte(processHandle, item.Address, item.Edit.ExpectedByte, item.Edit.ReplacementByte);
        }

        var primary = observed[0];

        return new LaunchResult(
            processId,
            modulePath,
            primary.Address.ToInt64(),
            $"0x{primary.Address.ToInt64():X}",
            primary.Original,
            target.ReplacementByte);
    }

    public static byte ReadRemoteByte(uint processId, long address)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessVmRead, false, processId);
        if (process == IntPtr.Zero)
        {
            throw NativeMethods.Error("OpenProcess failed while verifying the live RAM patch");
        }

        try
        {
            var value = new byte[1];
            if (!NativeMethods.ReadProcessMemory(process, new IntPtr(address), value, 1, out var read) || read != 1)
            {
                throw NativeMethods.Error("ReadProcessMemory failed while verifying the live RAM patch");
            }

            return value[0];
        }
        finally
        {
            _ = NativeMethods.CloseHandle(process);
        }
    }

    public static IReadOnlyList<int> PatchExistingChromeProcesses(PatchTarget target)
    {
        var patched = new List<int>();
        foreach (var process in Process.GetProcessesByName("chrome"))
        {
            try
            {
                ProcessModule? module = null;
                try
                {
                    module = process.Modules.Cast<ProcessModule>().FirstOrDefault(item =>
                        string.Equals(item.ModuleName, "chrome.dll", StringComparison.OrdinalIgnoreCase));
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    continue;
                }

                if (module is null)
                {
                    continue;
                }

                var handle = NativeMethods.OpenProcess(
                    NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite | NativeMethods.ProcessVmOperation,
                    false,
                    checked((uint)process.Id));
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var allPatched = true;
                    foreach (var edit in GetEdits(target))
                    {
                        var address = new IntPtr(checked(module.BaseAddress.ToInt64() + edit.PatchRva));
                        var current = new byte[1];
                        if (!NativeMethods.ReadProcessMemory(handle, address, current, 1, out var read) || read != 1)
                        {
                            allPatched = false;
                            break;
                        }

                        if (current[0] == edit.ReplacementByte)
                        {
                            continue;
                        }

                        if (current[0] != edit.ExpectedByte)
                        {
                            allPatched = false;
                            break;
                        }

                        WriteVerifiedByte(handle, address, edit.ExpectedByte, edit.ReplacementByte);
                    }

                    if (allPatched)
                    {
                        patched.Add(process.Id);
                    }
                }
                finally
                {
                    _ = NativeMethods.CloseHandle(handle);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return patched;
    }

    private static IReadOnlyList<PatchEdit> GetEdits(PatchTarget target)
    {
        return [
            new PatchEdit(target.PatchRawOffset, target.PatchRva, target.ExpectedByte, target.ReplacementByte),
            .. target.AdditionalEdits
        ];
    }

    private static void WriteVerifiedByte(IntPtr processHandle, IntPtr address, byte expected, byte replacement)
    {
        if (!NativeMethods.VirtualProtectEx(processHandle, address, 1, NativeMethods.PageExecuteReadWrite, out var oldProtection))
        {
            throw NativeMethods.Error("VirtualProtectEx failed before writing an MV2 target");
        }

        try
        {
            var replacementBytes = new[] { replacement };
            if (!NativeMethods.WriteProcessMemory(processHandle, address, replacementBytes, 1, out var bytesWritten) || bytesWritten != 1)
            {
                throw NativeMethods.Error("WriteProcessMemory failed at an MV2 target");
            }

            var verification = new byte[1];
            if (!NativeMethods.ReadProcessMemory(processHandle, address, verification, 1, out var bytesRead) ||
                bytesRead != 1 || verification[0] != replacement)
            {
                throw new InvalidOperationException(
                    $"The in-memory MV2 patch could not be verified after writing (expected 0x{replacement:X2}, originally 0x{expected:X2}).");
            }

            if (!NativeMethods.FlushInstructionCache(processHandle, address, 1))
            {
                throw NativeMethods.Error("FlushInstructionCache failed");
            }
        }
        finally
        {
            _ = NativeMethods.VirtualProtectEx(processHandle, address, 1, oldProtection, out _);
        }
    }

    internal static void EnsureChromeIsNotRunning()
    {
        var running = Process.GetProcessesByName("chrome");
        try
        {
            if (running.Length > 0)
            {
                var ids = string.Join(", ", running.Select(process => process.Id));
                throw new InvalidOperationException(
                    $"Chrome is already running (PID: {ids}). Close every Chrome process so a new patched browser process is guaranteed.");
            }
        }
        finally
        {
            foreach (var process in running)
            {
                process.Dispose();
            }
        }
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        return string.Join(' ', new[] { executablePath }.Concat(arguments).Select(QuoteArgument));
    }

    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new StringBuilder(argument.Length + 2);
        result.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private static string? GetPathFromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        var buffer = new StringBuilder(1024);
        var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, checked((uint)buffer.Capacity), 0);
        if (length == 0)
        {
            return null;
        }

        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)length + 1));
            length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, checked((uint)buffer.Capacity), 0);
            if (length == 0)
            {
                return null;
            }
        }

        return NormalizePath(buffer.ToString());
    }

    private static bool PathsEqual(string? left, string right)
    {
        return left is not null && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        const string extendedPrefix = "\\\\?\\";
        var normalized = path.StartsWith(extendedPrefix, StringComparison.Ordinal) ? path[extendedPrefix.Length..] : path;
        return Path.GetFullPath(normalized).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static void CloseEventFileHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
