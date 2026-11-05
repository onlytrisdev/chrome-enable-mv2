using System.Security.Cryptography;
using Iced.Intel;

namespace Mv2Enabler.Tests;

public sealed class AutomaticDiscoveryIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void StartupCookieFailureSummaryRequiresSameCookieAndNonReturningHelper()
    {
        var installation = ChromeInstallationFinder.Find(); var image = PeImage.Load(installation.DllPath);
        var anchor = Assert.IsType<ChromeResourceAnchor>(ChromeResourceAnchor.Load(installation.DllPath));
        var plan = AutomaticGateLocator.Locate(image, anchor);
        Assert.True(plan.Success, string.Join("\n", plan.Diagnostics));
        var edit = plan.Target!.AdditionalEdits[^1].PatchRawOffset;
        var text = image.GetSection(".text"); var pdata = image.GetSection(".pdata");
        var ranges = new List<(int Start, int End)>();
        for (var p = pdata.RawOffset; p + 12 <= pdata.RawOffset + Math.Min(pdata.RawSize, pdata.VirtualSize); p += 12)
            ranges.Add((BitConverter.ToInt32(image.Bytes, p) - text.VirtualAddress + text.RawOffset,
                BitConverter.ToInt32(image.Bytes, p + 4) - text.VirtualAddress + text.RawOffset));
        var range = Assert.Single(ranges, f => edit >= f.Start && edit < f.End);
        Instruction At(int p)
        {
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, p, Math.Min(15, image.Bytes.Length - p)));
            decoder.IP = (ulong)p; decoder.Decode(out var i); return i;
        }
        var calls = new List<Instruction>(); var prior = default(Instruction);
        for (var p = range.Start; p < range.End;)
        {
            var i = At(p); Assert.False(i.IsInvalid);
            if (i.FlowControl == FlowControl.Call && prior.Mnemonic == Mnemonic.Xor &&
                prior.Op0Register == Register.RCX && prior.Op1Register == Register.RSP) calls.Add(i);
            prior = i; p = (int)i.NextIP;
        }
        var sink = Assert.Single(calls); var helper = At((int)sink.NearBranchTarget);
        Assert.Equal(Mnemonic.Cmp, helper.Mnemonic); Assert.True(helper.IsIPRelativeMemoryOperand);
        var branch = At((int)helper.NextIP); Assert.Equal(Mnemonic.Jne, branch.Mnemonic);
        var cursor = (int)branch.NearBranchTarget; Instruction fastfail = default;
        for (var n = 0; n < 8; n++)
        {
            var i = At(cursor);
            if (i.Mnemonic == Mnemonic.Int) { fastfail = i; break; }
            cursor = i.Mnemonic == Mnemonic.Jmp ? (int)i.NearBranchTarget : (int)i.NextIP;
        }
        Assert.Equal(Mnemonic.Int, fastfail.Mnemonic); Assert.Equal(0x29UL, fastfail.GetImmediate(0));
        // Both changes are in the in-memory copy. The installed DLL is untouched.
        foreach (var offset in new[] { (int)helper.NextIP - 1, (int)fastfail.IP + 1 })
        {
            var original = image.Bytes[offset]; image.Bytes[offset] ^= 1;
            try { Assert.False(AutomaticGateLocator.Locate(image, anchor).Success); }
            finally { image.Bytes[offset] = original; }
        }
        Assert.True(AutomaticGateLocator.Locate(image, anchor).Success);
    }

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

        // Reverse CMP operands: equality is unchanged, but the V6/V12 byte
        // signature is gone. Automatic discovery must still prove the same plan.
        var firstStartupEdit = automatic.Target.AdditionalEdits[6].PatchRawOffset;
        var branches = new List<Instruction>();
        for (var p = firstStartupEdit - 6; p < firstStartupEdit; p++)
        {
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, p, 15)); decoder.IP = (ulong)p;
            decoder.Decode(out var i);
            if (i.Mnemonic == Mnemonic.Jne && i.NextIP > (ulong)firstStartupEdit && i.NearBranchTarget > i.NextIP) branches.Add(i);
        }
        var startupEntry = Assert.Single(branches);
        var cmpRaw = (int)startupEntry.IP - 3;
        var cmpDecoder = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, cmpRaw, 3)); cmpDecoder.IP = (ulong)cmpRaw;
        cmpDecoder.Decode(out var cmp);
        Assert.Equal(Mnemonic.Cmp, cmp.Mnemonic); Assert.Equal(startupEntry.IP, cmp.NextIP);
        using var stream = new MemoryStream(); var encoder = Encoder.Create(64, new StreamCodeWriter(stream));
        encoder.Encode(Instruction.Create(Code.Cmp_rm64_r64, cmp.Op1Register, cmp.Op0Register), (ulong)cmpRaw);
        var reversed = stream.ToArray(); Assert.Equal(3, reversed.Length);
        var originalCompare = image.Bytes.AsSpan(cmpRaw, 3).ToArray();
        try
        {
            reversed.CopyTo(image.Bytes, cmpRaw);
            Assert.False(Mv2GateLocator.TryLocateStartup(image, out _, out _));
            var independent = AutomaticGateLocator.Locate(image, anchor);
            Assert.True(independent.Success, string.Join("\n", independent.Diagnostics));
            Assert.Equal(ChromeDebugLauncher.GetEdits(automatic.Target), ChromeDebugLauncher.GetEdits(independent.Target!));
        }
        finally { originalCompare.CopyTo(image.Bytes, cmpRaw); }

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
