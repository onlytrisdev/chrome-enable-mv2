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
    IReadOnlyList<string> Diagnostics,
    bool CacheHit = false,
    long AnalysisMilliseconds = 0);

internal static class AnalysisService
{
    public static AnalysisReport Analyze(ChromeInstallation installation, bool useCache = true, string? cacheDirectory = null)
    {
        var timer = Stopwatch.StartNew();
        using var source = new FileStream(installation.DllPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        var image = PeImage.LoadHeaders(source, installation.DllPath);
        source.Position = 0;
        var sha256 = Convert.ToHexString(SHA256.HashData(source));
        var version = FileVersionInfo.GetVersionInfo(installation.DllPath).FileVersion ?? installation.Version;
        ChromeResourceAnchor? resource = null;
        string? resourceError = null;
        try { resource = ChromeResourceAnchor.Load(installation.DllPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        { resourceError = error.Message; }
        var resourceHash = resource?.Sha256 ?? "unavailable";
        var cache = new AnalysisCache(cacheDirectory);
        var cached = useCache && resourceError is null ? cache.Read(image, sha256, resourceHash, source) : null;
        var cacheHit = cached is not null;
        if (!cacheHit)
        {
            source.Position = 0; var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
            image = PeImage.Parse(installation.DllPath, bytes, bytes.Length);
        }
        var locator = cached ?? (resource is not null ? AutomaticGateLocator.Locate(image, resource) :
            new LocatorResult(false, null, 0, 0, ["Automatic discovery needs the exact English resource anchor."]));
        var automaticDiagnostics = locator.Diagnostics;
        if (!locator.Success && cached is null)
        {
            // Retain the old full, fail-closed validators for older code families;
            // a malformed/ambiguous resource pack is never silently ignored.
            var legacy = resourceError is null && CanUseLegacyFallback(version, resource is not null)
                ? Mv2GateLocator.Locate(image) : null;
            if (legacy?.Success == true) locator = legacy with
            { Diagnostics = [.. automaticDiagnostics, "Using the independently verified legacy layout validator.", .. legacy.Diagnostics] };
        }
        if (locator.Target is { } plan) locator = locator with { Target = plan with { ImageSha256 = sha256 } };
        if (!cacheHit && useCache && resourceError is null) cache.Write(image, sha256, resourceHash, locator);
        var diagnostics = locator.Diagnostics.ToList();
        if (resourceError is not null) diagnostics.Add($"Resource validation failed: {resourceError}");
        diagnostics.Add(cacheHit ? "Cache HIT: DLL/resource SHA-256, engine identity, DPAPI integrity and all planned bytes verified; no gate scan."
            : "Cache MISS: local analysis performed; only successful original-image plans may be cached.");
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
        if (locator.Target is { } target && target.RuleId != AutomaticGateLocator.Rule)
        {
            var expectedMajor = target.RuleId.EndsWith(".v12", StringComparison.Ordinal) ? "158." : target.RuleId.EndsWith(".v11", StringComparison.Ordinal) ? "157." : target.RuleId.EndsWith(".v10", StringComparison.Ordinal)
                ? "156."
                : target.RuleId.EndsWith(".v9", StringComparison.Ordinal)
                ? "156."
                : target.RuleId.EndsWith(".v8", StringComparison.Ordinal)
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
            image.FileSize,
            "x64 PE32+",
            $"0x{image.PreferredImageBase:X}",
            locator.Target,
            locator.PatternMatchCount,
            locator.SemanticMatchCount,
            diagnostics,
            cacheHit,
            timer.ElapsedMilliseconds);
    }

    internal static bool CanUseLegacyFallback(string version, bool resourceAvailable) =>
        Version.TryParse(version, out var parsed) &&
        (parsed.Major is >= 151 and <= 154 || !resourceAvailable && parsed.Major is >= 155 and <= 158);
}
