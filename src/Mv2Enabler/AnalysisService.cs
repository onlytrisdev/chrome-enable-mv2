using System.Diagnostics;
using System.Security.Cryptography;

namespace Mv2Enabler;

internal sealed record AnalysisReport(
    bool Success,
    string ChromePath,
    string DllPath,
    string Version,
    string Sha256,
    long FileSize,
    string Architecture,
    string PreferredImageBase,
    PatchTarget? Target,
    int PatternMatchCount,
    int SemanticMatchCount,
    IReadOnlyList<string> Diagnostics);

internal static class AnalysisService
{
    public static AnalysisReport Analyze(ChromeInstallation installation)
    {
        var image = PeImage.Load(installation.DllPath);
        var locator = Mv2GateLocator.Locate(image);
        var sha256 = Convert.ToHexString(SHA256.HashData(image.Bytes));
        var version = FileVersionInfo.GetVersionInfo(installation.DllPath).FileVersion ?? installation.Version;
        var diagnostics = locator.Diagnostics.ToList();
        if (!locator.Success)
        {
            var leads = SemanticCandidateProbe.Find(image);
            diagnostics.Add(
                $"Read-only semantic probe found {leads.Count} possible MV2 compare sites; no byte will be patched from probe results.");
            foreach (var lead in leads.Take(12))
            {
                diagnostics.Add(
                    $"Probe RVA 0x{lead.Rva:X}: {lead.Kind}, manifest field displacement 0x{lead.ManifestFieldOffset:X2}.");
            }
        }
        if (locator.Target is { } target)
        {
            var expectedMajor = target.RuleId.EndsWith(".v8", StringComparison.Ordinal)
                ? "155."
                : target.RuleId.EndsWith(".v7", StringComparison.Ordinal)
                ? "154."
                : target.RuleId.EndsWith(".v6", StringComparison.Ordinal)
                    ? "153."
                    : target.RuleId.EndsWith(".v5", StringComparison.Ordinal)
                        ? "152."
                        : "151.";
            if (!version.StartsWith(expectedMajor, StringComparison.Ordinal))
            {
                diagnostics.Insert(
                    0,
                    $"Warning: {target.RuleId} was validated against Chrome {expectedMajor.TrimEnd('.')}; " +
                    $"detected {version}. Semantic checks still apply.");
            }
        }

        return new AnalysisReport(
            locator.Success,
            installation.ExecutablePath,
            installation.DllPath,
            version,
            sha256,
            image.Bytes.LongLength,
            "x64 PE32+",
            $"0x{image.PreferredImageBase:X}",
            locator.Target,
            locator.PatternMatchCount,
            locator.SemanticMatchCount,
            diagnostics);
    }
}
