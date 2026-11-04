using System.Security.Cryptography;

namespace Mv2Enabler.Tests;

public sealed class AutomaticDiscoveryIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AutomaticPlanEqualsVerifiedLegacyAndSurvivesResourceRenumbering()
    {
        var installation = ChromeInstallationFinder.Find(); var image = PeImage.Load(installation.DllPath);
        var anchor = Assert.IsType<ChromeResourceAnchor>(ChromeResourceAnchor.Load(installation.DllPath));
        var legacy = Mv2GateLocator.Locate(image); var automatic = AutomaticGateLocator.Locate(image, anchor);
        Assert.True(automatic.Success, string.Join("\n", automatic.Diagnostics));
        Assert.Equal(AutomaticGateLocator.Rule, automatic.Target!.RuleId);
        Assert.Equal(ChromeDebugLauncher.GetEdits(legacy.Target!), ChromeDebugLauncher.GetEdits(automatic.Target));

        var installBranch = automatic.Target.AdditionalEdits[5].PatchRawOffset;
        var needle = new byte[] { 0xba }.Concat(BitConverter.GetBytes(anchor.ResourceId)).ToArray();
        var matches = BytePattern.Parse(Convert.ToHexString(needle).Chunk(2).Select(x => new string(x)).Aggregate((a, b) => a + " " + b))
            .FindAll(image.Bytes.AsSpan(installBranch, 4096));
        var idOffset = installBranch + Assert.Single(matches) + 1;
        var original = image.Bytes.AsSpan(idOffset, 4).ToArray();
        try
        {
            BitConverter.GetBytes(65000).CopyTo(image.Bytes, idOffset);
            Assert.False(Mv2GateLocator.Locate(image).Success);
            Assert.False(AutomaticGateLocator.Locate(image, anchor).Success);
            var renumbered = AutomaticGateLocator.Locate(image, anchor with { ResourceId = 65000 });
            Assert.True(renumbered.Success, string.Join("\n", renumbered.Diagnostics));
            Assert.Equal(ChromeDebugLauncher.GetEdits(automatic.Target), ChromeDebugLauncher.GetEdits(renumbered.Target!));
        }
        finally { original.CopyTo(image.Bytes, idOffset); }

        var integer = automatic.Target.AdditionalEdits[1].PatchRawOffset;
        var maskOffset = integer + image.Bytes.AsSpan(integer, 64).IndexOf((ReadOnlySpan<byte>)[0x0a, 0x01, 0x00, 0x00]);
        Assert.True(maskOffset >= integer);
        image.Bytes[maskOffset] ^= 1;
        try { Assert.False(AutomaticGateLocator.Locate(image, anchor).Success); }
        finally { image.Bytes[maskOffset] ^= 1; }

        var startup = automatic.Target.AdditionalEdits[^1].PatchRawOffset;
        image.Bytes[startup] ^= 1;
        try { Assert.False(AutomaticGateLocator.Locate(image, anchor).Success); }
        finally { image.Bytes[startup] ^= 1; }
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installation.DllPath))),
            Convert.ToHexString(SHA256.HashData(image.Bytes)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void CacheInvalidatesOnSameSizeSameTimestampReplacementAndCorruption()
    {
        var installed = ChromeInstallationFinder.Find();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mv2-discovery-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "Locales"));
        try
        {
            var dll = Path.Combine(root, "chrome.dll"); var pak = Path.Combine(root, "Locales", "en-US.pak");
            File.Copy(installed.DllPath, dll);
            File.Copy(Path.Combine(Path.GetDirectoryName(installed.DllPath)!, "Locales", "en-US.pak"), pak);
            var installation = installed with { DllPath = dll }; var cache = Path.Combine(root, "cache");
            var first = AnalysisService.Analyze(installation, cacheDirectory: cache);
            Assert.True(first.Success); Assert.False(first.CacheHit); Assert.Equal(AutomaticGateLocator.Rule, first.Target!.RuleId);
            var warm = AnalysisService.Analyze(installation, cacheDirectory: cache);
            Assert.True(warm.Success); Assert.True(warm.CacheHit);
            var time = File.GetLastWriteTimeUtc(dll); var length = new FileInfo(dll).Length;
            using (var file = new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { file.Position = 0x40; var value = file.ReadByte(); file.Position = 0x40; file.WriteByte((byte)(value ^ 1)); }
            File.SetLastWriteTimeUtc(dll, time);
            Assert.Equal(length, new FileInfo(dll).Length); Assert.Equal(time, File.GetLastWriteTimeUtc(dll));
            using (var changed = File.OpenRead(dll))
                Assert.Throws<InvalidOperationException>(() => ChromeDebugLauncher.VerifyImageIdentity(changed, first.Target!));
            var replaced = AnalysisService.Analyze(installation, cacheDirectory: cache);
            Assert.True(replaced.Success); Assert.False(replaced.CacheHit); Assert.NotEqual(first.Sha256, replaced.Sha256);
            Assert.True(AnalysisService.Analyze(installation, cacheDirectory: cache).CacheHit);

            var cacheFile = Assert.Single(Directory.GetFiles(cache, "*.bin")); File.WriteAllText(cacheFile, "corrupt cache");
            var recovered = AnalysisService.Analyze(installation, cacheDirectory: cache);
            Assert.True(recovered.Success); Assert.False(recovered.CacheHit);
            Assert.True(AnalysisService.Analyze(installation, cacheDirectory: cache).CacheHit);
            using (var changedPak = new FileStream(pak, FileMode.Append)) changedPak.WriteByte(0);
            var newResources = AnalysisService.Analyze(installation, cacheDirectory: cache);
            Assert.True(newResources.Success); Assert.False(newResources.CacheHit);
            Assert.False(AnalysisService.Analyze(installation, useCache: false, cacheDirectory: cache).CacheHit);
        }
        finally
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("mv2-discovery-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected integration-test cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }
}
