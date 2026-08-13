using System.Text;
using System.Text.Json;

namespace Mv2Enabler;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            return args[0] switch
            {
                "analyze" => RunAnalyze(args[1..]),
                "launch" => RunLaunch(args[1..]),
                "smoke-test" => RunSmokeTest(args[1..]),
                "functional-test" => RunFunctionalTest(args[1..]),
                "functional-ui-test" => RunUiFunctionalTest(args[1..]),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static int RunAnalyze(string[] args)
    {
        ParseCommonOptions(args, out var chromePath, out var dllPath, out var json, out _, out _);
        var installation = ChromeInstallationFinder.Find(chromePath, dllPath);
        var report = AnalysisService.Analyze(installation);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintReport(report);
        }

        return report.Success ? 0 : 2;
    }

    private static int RunLaunch(string[] args)
    {
        ParseCommonOptions(args, out var chromePath, out var dllPath, out var json, out var timeoutSeconds, out var chromeArguments);
        var installation = ChromeInstallationFinder.Find(chromePath, dllPath);
        var report = AnalysisService.Analyze(installation);
        if (!report.Success || report.Target is null)
        {
            PrintReport(report);
            return 2;
        }

        ChromeDebugLauncher.EnsureChromeIsNotRunning();
        var profileRepair = ChromeProfileRepair.RepairBeforeLaunch(installation, chromeArguments);
        var result = ChromeDebugLauncher.Launch(
            installation,
            report.Target,
            chromeArguments,
            TimeSpan.FromSeconds(timeoutSeconds));

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { ProfileRepair = profileRepair, Launch = result },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (profileRepair.ExtensionsReEnabled > 0)
            {
                Console.WriteLine(
                    $"Re-enabled {profileRepair.ExtensionsReEnabled} persisted MV2 extension(s) in " +
                    $"{profileRepair.ProfilesChanged} profile(s).");
                Console.WriteLine($"  Profiles:        {string.Join(", ", profileRepair.ChangedProfiles)}");
                foreach (var backup in profileRepair.BackupFiles)
                {
                    Console.WriteLine($"  Profile backup:  {backup}");
                }
            }

            Console.WriteLine("Chrome launched with the MV2 gate patched in memory.");
            Console.WriteLine($"  PID:            {result.ProcessId}");
            Console.WriteLine($"  Module:         {result.ModulePath}");
            Console.WriteLine($"  Remote address: {result.RemoteAddress}");
            Console.WriteLine($"  Byte:           0x{result.OriginalByte:X2} -> 0x{result.ReplacementByte:X2}");
            Console.WriteLine("  On-disk chrome.dll was not modified.");
        }

        return 0;
    }

    private static int RunSmokeTest(string[] args)
    {
        ParseCommonOptions(args, out var chromePath, out var dllPath, out var json, out var timeoutSeconds, out var extraArguments);
        if (extraArguments.Count != 0)
        {
            throw new ArgumentException("smoke-test does not accept pass-through Chrome arguments.");
        }

        var installation = ChromeInstallationFinder.Find(chromePath, dllPath);
        var report = AnalysisService.Analyze(installation);
        if (!report.Success || report.Target is null)
        {
            PrintReport(report);
            return 2;
        }

        var result = SmokeTestService.Run(installation, report.Target, TimeSpan.FromSeconds(timeoutSeconds));
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine(result.Success ? "SMOKE TEST: PASS" : "SMOKE TEST: FAIL");
            Console.WriteLine($"  PID:                  {result.ProcessId}");
            Console.WriteLine($"  Remote address:       {result.RemoteAddress}");
            Console.WriteLine($"  Process stayed alive: {result.ProcessStayedAlive}");
            Console.WriteLine($"  Message:              {result.Message}");
            Console.WriteLine("  The temporary profile is removed during final cleanup.");
        }

        return result.Success ? 0 : 3;
    }

    private static int RunFunctionalTest(string[] args)
    {
        string? extensionPath = null;
        var commonArgs = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--extension")
            {
                extensionPath = ReadOptionValue(args, ref index, "--extension");
            }
            else
            {
                commonArgs.Add(args[index]);
            }
        }

        if (extensionPath is null)
        {
            throw new ArgumentException("functional-test requires --extension PATH.");
        }

        ParseCommonOptions(commonArgs.ToArray(), out var chromePath, out var dllPath, out var json, out var timeoutSeconds, out var extraArguments);
        if (extraArguments.Count != 0)
        {
            throw new ArgumentException("functional-test does not accept pass-through Chrome arguments.");
        }

        var installation = ChromeInstallationFinder.Find(chromePath, dllPath);
        var report = AnalysisService.Analyze(installation);
        if (!report.Success || report.Target is null)
        {
            PrintReport(report);
            return 2;
        }

        var result = FunctionalTestService.Run(
            installation,
            report.Target,
            extensionPath,
            TimeSpan.FromSeconds(timeoutSeconds));
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine(result.Success ? "FUNCTIONAL MV2 TEST: PASS" : "FUNCTIONAL MV2 TEST: INCONCLUSIVE");
            Console.WriteLine($"  PID:                    {result.ProcessId}");
            Console.WriteLine($"  DevTools started:       {result.DevToolsStarted}");
            Console.WriteLine($"  Extension target found: {result.ExtensionTargetFound}");
            Console.WriteLine($"  Expected extension ID:  {result.ExpectedExtensionId}");
            Console.WriteLine($"  Extension target title: {result.ExtensionTargetTitle ?? "(none)"}");
            Console.WriteLine($"  Extension target URL:   {result.ExtensionTargetUrl ?? "(none)"}");
            foreach (var discovered in result.DiscoveredExtensionTargets)
            {
                Console.WriteLine($"  Discovered extension:   {discovered}");
            }
            Console.WriteLine($"  Temp profile removed:   {result.TemporaryProfileRemoved}");
            Console.WriteLine($"  Message:                {result.Message}");
        }

        return result.Success ? 0 : 4;
    }

    private static int RunUiFunctionalTest(string[] args)
    {
        string? extensionPath = null;
        var commonArgs = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--extension")
            {
                extensionPath = ReadOptionValue(args, ref index, "--extension");
            }
            else
            {
                commonArgs.Add(args[index]);
            }
        }

        if (extensionPath is null)
        {
            throw new ArgumentException("functional-ui-test requires --extension PATH.");
        }

        ParseCommonOptions(commonArgs.ToArray(), out var chromePath, out var dllPath, out var json, out var timeoutSeconds, out var extraArguments);
        if (extraArguments.Count != 0)
        {
            throw new ArgumentException("functional-ui-test does not accept pass-through Chrome arguments.");
        }

        var installation = ChromeInstallationFinder.Find(chromePath, dllPath);
        var report = AnalysisService.Analyze(installation);
        if (!report.Success || report.Target is null)
        {
            PrintReport(report);
            return 2;
        }

        var result = UiFunctionalTestService.Run(
            installation,
            report.Target,
            extensionPath,
            TimeSpan.FromSeconds(timeoutSeconds));
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine(result.Success ? "FUNCTIONAL MV2 UI TEST: PASS" : "FUNCTIONAL MV2 UI TEST: FAIL");
            Console.WriteLine($"  PID:                    {result.ProcessId}");
            Console.WriteLine($"  Expected extension ID:  {result.ExpectedExtensionId}");
            Console.WriteLine($"  Folder dialog title:    {result.DialogTitle}");
            Console.WriteLine($"  Folder dialog closed:   {result.FolderDialogClosed}");
            Console.WriteLine($"  Live patch byte:        0x{result.LivePatchByte:X2}");
            Console.WriteLine($"  Patched Chrome PIDs:    {string.Join(",", result.PatchedChromeProcessIds)}");
            Console.WriteLine($"  Extension target title: {result.ExtensionTargetTitle ?? "(none)"}");
            Console.WriteLine($"  Extension target URL:   {result.ExtensionTargetUrl ?? "(none)"}");
            if (!result.Success)
            {
                Console.WriteLine($"  Extension manager:      {result.ExtensionManagerState}");
                Console.WriteLine($"  Extensions page text:   {result.ExtensionsPageText}");
            }
            Console.WriteLine($"  Temp profile removed:   {result.TemporaryProfileRemoved}");
            Console.WriteLine($"  Message:                {result.Message}");
        }

        return result.Success ? 0 : 5;
    }

    private static void ParseCommonOptions(
        string[] args,
        out string? chromePath,
        out string? dllPath,
        out bool json,
        out int timeoutSeconds,
        out IReadOnlyList<string> chromeArguments)
    {
        chromePath = null;
        dllPath = null;
        json = false;
        timeoutSeconds = 20;
        var passThrough = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--")
            {
                passThrough.AddRange(args[(index + 1)..]);
                break;
            }

            switch (argument)
            {
                case "--chrome":
                    chromePath = ReadOptionValue(args, ref index, argument);
                    break;
                case "--dll":
                    dllPath = ReadOptionValue(args, ref index, argument);
                    break;
                case "--json":
                    json = true;
                    break;
                case "--timeout":
                    var value = ReadOptionValue(args, ref index, argument);
                    if (!int.TryParse(value, out timeoutSeconds) || timeoutSeconds is < 1 or > 120)
                    {
                        throw new ArgumentException("--timeout must be an integer from 1 to 120 seconds.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown option '{argument}'. Put Chrome arguments after --.");
            }
        }

        chromeArguments = passThrough;
    }

    private static string ReadOptionValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void PrintReport(AnalysisReport report)
    {
        Console.WriteLine(report.Success ? "ANALYSIS: SAFE TARGET FOUND" : "ANALYSIS: NO SAFE TARGET");
        Console.WriteLine($"  Chrome:          {report.ChromePath}");
        Console.WriteLine($"  chrome.dll:      {report.DllPath}");
        Console.WriteLine($"  Version:         {report.Version}");
        Console.WriteLine($"  SHA-256:         {report.Sha256}");
        Console.WriteLine($"  Pattern matches: {report.PatternMatchCount}");
        Console.WriteLine($"  Semantic matches:{report.SemanticMatchCount}");

        if (report.Target is { } target)
        {
            Console.WriteLine($"  Rule:            {target.RuleId}");
            Console.WriteLine($"  File offset:     0x{target.PatchRawOffset:X}");
            Console.WriteLine($"  RVA:             0x{target.PatchRva:X}");
            Console.WriteLine($"  Planned byte:    0x{target.ExpectedByte:X2} -> 0x{target.ReplacementByte:X2}");
            Console.WriteLine($"  State:           {target.State}");
            foreach (var edit in target.AdditionalEdits)
            {
                Console.WriteLine($"  Additional RVA:  0x{edit.PatchRva:X} (0x{edit.ExpectedByte:X2} -> 0x{edit.ReplacementByte:X2})");
            }
            foreach (var item in target.Evidence)
            {
                Console.WriteLine($"    ✓ {item}");
            }
        }

        foreach (var diagnostic in report.Diagnostics)
        {
            Console.WriteLine($"  - {diagnostic}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("mv2ctl - Chrome Manifest V2 in-memory launcher");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  mv2ctl analyze [--chrome PATH] [--dll PATH] [--json]");
        Console.WriteLine("  mv2ctl launch  [--chrome PATH] [--timeout SECONDS] [--json] [-- CHROME_ARGS]");
        Console.WriteLine("  mv2ctl smoke-test [--chrome PATH] [--timeout SECONDS] [--json]");
        Console.WriteLine("  mv2ctl functional-test --extension PATH [--chrome PATH] [--timeout SECONDS] [--json]");
        Console.WriteLine("  mv2ctl functional-ui-test --extension PATH [--chrome PATH] [--timeout SECONDS] [--json]");
        Console.WriteLine();
        Console.WriteLine("The launcher changes a verified MV2 gate patch set in the browser process after chrome.dll loads.");
        Console.WriteLine("It never writes to chrome.dll on disk and refuses to run while Chrome is already open.");
    }
}
