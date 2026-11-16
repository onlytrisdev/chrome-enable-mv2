using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mv2Enabler;

internal enum ExecutableMatch { Selected, Other, Unknown }

// A process name is a candidate, never proof of browser identity. Unreadable
// candidates remain blockers; only a proven different executable is ignored.
internal sealed class ChromeProcessScope : IDisposable
{
    private readonly List<Process> processes;
    internal IReadOnlyList<Process> Processes => processes;
    internal int UnidentifiedCount { get; }
    internal int Count => processes.Count + UnidentifiedCount;
    internal bool IsClear => Count == 0;

    private ChromeProcessScope(List<Process> processes, int unidentifiedCount)
    { this.processes = processes; UnidentifiedCount = unidentifiedCount; }

    internal static ExecutableMatch ClassifyExecutable(string selected, string? actual)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(actual) || !Path.IsPathFullyQualified(selected) || !Path.IsPathFullyQualified(actual))
                return ExecutableMatch.Unknown;
            return Path.GetFullPath(selected).Equals(Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase)
                ? ExecutableMatch.Selected : ExecutableMatch.Other;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        { return ExecutableMatch.Unknown; }
    }

    internal static ChromeProcessScope Capture(string executable)
    {
        var selected = new List<Process>(); var unknown = 0;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            var keep = false;
            try
            {
                if (process.HasExited) continue;
                switch (ClassifyExecutable(executable, ReadExecutable(process)))
                {
                    case ExecutableMatch.Selected: selected.Add(process); keep = true; break;
                    case ExecutableMatch.Unknown: if (!process.HasExited) unknown++; break;
                }
            }
            catch (Exception error) when (IsInspectionFailure(error)) { unknown++; }
            finally { if (!keep) process.Dispose(); }
        }
        return new(selected, unknown);
    }

    private static string? ReadExecutable(Process process)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768); var size = (uint)path.Capacity;
            return NativeMethods.QueryFullProcessImageNameW(handle, 0, path, ref size) ? path.ToString() : null;
        }
        finally { _ = NativeMethods.CloseHandle(handle); }
    }

    // Test helpers may touch only this root and its proven descendants, not
    // every process of the same browser installation (or the same basename).
    internal static ChromeProcessScope CaptureOwnedTree(Process root)
    {
        _ = root.Handle; // Keep the root PID pinned through restart/cleanup.
        var started = root.StartTime.ToUniversalTime().Ticks;
        var executable = ReadExecutable(root) ?? throw new InvalidOperationException("Cannot identify owned browser root.");
        var parents = ReadProcessParents();
        using var candidates = Capture(executable);
        var owned = new List<Process>();
        foreach (var process in candidates.processes.ToArray())
        {
            try
            {
                _ = process.Handle;
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks >= started && IsDescendant(process.Id, root.Id, parents))
                { owned.Add(process); candidates.processes.Remove(process); }
            }
            catch (Exception error) when (IsInspectionFailure(error)) { /* Unproven ownership grants no mutation authority. */ }
        }
        return new(owned, 0);
    }

    internal static bool IsDescendant(int processId, int rootId, IReadOnlyDictionary<int, int> parents)
    {
        var visited = new HashSet<int>();
        while (processId != rootId)
            if (!visited.Add(processId) || !parents.TryGetValue(processId, out processId)) return false;
        return true;
    }

    internal static Dictionary<int, int> ReadProcessParents()
    {
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) throw NativeMethods.Error("Could not inspect process ancestry");
        try
        {
            var entry = new NativeMethods.ProcessEntry32 { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>(), Executable = "" };
            if (!NativeMethods.Process32FirstW(snapshot, ref entry)) throw NativeMethods.Error("Could not read process ancestry");
            var parents = new Dictionary<int, int>();
            do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
            while (NativeMethods.Process32NextW(snapshot, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw NativeMethods.Error("Incomplete process ancestry snapshot");
            return parents;
        }
        finally { _ = NativeMethods.CloseHandle(snapshot); }
    }

    internal static bool IsInspectionFailure(Exception error) => error is
        Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or EntryPointNotFoundException or OverflowException;

    public void Dispose() { foreach (var process in processes) process.Dispose(); }
}
