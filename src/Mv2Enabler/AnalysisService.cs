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
        if (!version.StartsWith("151.", StringComparison.Ordinal))
        {
            diagnostics.Insert(0, $"Warning: rule v1 was developed against Chrome 151; detected {version}. Semantic checks still apply.");
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
