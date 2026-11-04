using System.Security.Cryptography;
using System.Text;

namespace Mv2Enabler.Tests;

public sealed class AnalysisCacheTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mv2-cache-tests-" + Guid.NewGuid().ToString("N")));
    private PeImage Image(byte expected = 0x7f) => SemanticFixtures.Image([0xcc, expected, 0x01, 0xc3], Path.Combine(root, "chrome.dll"));
    private static string Hash(PeImage image) => Convert.ToHexString(SHA256.HashData(image.Bytes));
    private static LocatorResult Plan(PeImage image) => new(true,
        new(AutomaticGateLocator.Rule, "fixture", 0x401, 0x401, 0x1001, 0x7f, 0xeb,
            PatchState.Original, [], ["fixture"], Hash(image)), 2, 2, ["fixture"]);

    [Fact]
    public void RoundTripProtectedCacheWithHeadersAndRandomAccessBytes()
    {
        var image = Image(); var cache = new AnalysisCache(root); var plan = Plan(image);
        Assert.True(cache.Write(image, Hash(image), "resource", plan));
        using var stream = new MemoryStream(image.Bytes);
        var headers = PeImage.LoadHeaders(stream, image.Path);
        Assert.True(headers.Bytes.Length < image.Bytes.Length);
        Assert.NotNull(cache.Read(headers, Hash(image), "resource", stream));
    }

    [Fact]
    public void RejectsChangedDllHashEvenAtSamePath()
    {
        var image = Image(); var cache = new AnalysisCache(root);
        Assert.True(cache.Write(image, Hash(image), "resource", Plan(image)));
        var changed = image.Bytes.ToArray(); changed[^1] ^= 1;
        var replacement = PeImage.Parse(image.Path, changed, changed.Length);
        Assert.Null(cache.Read(replacement, Hash(replacement), "resource"));
    }

    [Theory]
    [InlineData("different-resource")]
    [InlineData("")]
    public void InvalidatesWhenResourceIdentityChanges(string resource)
    {
        var image = Image(); var cache = new AnalysisCache(root);
        cache.Write(image, Hash(image), "resource", Plan(image));
        Assert.Null(cache.Read(image, Hash(image), resource));
    }

    [Fact]
    public void InvalidatesWhenEngineChanges()
    {
        var image = Image(); var cache = new AnalysisCache(root); cache.Write(image, Hash(image), "resource", Plan(image));
        var entropy = Encoding.UTF8.GetBytes("onlytrisdev/ChromeMv2Launcher/analysis-cache/v1");
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(cache.PathFor(image.Path)), entropy, DataProtectionScope.CurrentUser);
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(plain).Replace(AnalysisCache.EngineIdentity, "old-engine", StringComparison.Ordinal));
        File.WriteAllBytes(cache.PathFor(image.Path), ProtectedData.Protect(changed, entropy, DataProtectionScope.CurrentUser));
        Assert.Null(cache.Read(image, Hash(image), "resource"));
    }

    [Fact]
    public void RejectsCorruptionAndUnauthenticatedJson()
    {
        var image = Image(); var cache = new AnalysisCache(root); cache.Write(image, Hash(image), "resource", Plan(image));
        var bytes = File.ReadAllBytes(cache.PathFor(image.Path)); bytes[^4] ^= 1;
        File.WriteAllBytes(cache.PathFor(image.Path), bytes);
        Assert.Null(cache.Read(image, Hash(image), "resource"));
        File.WriteAllText(cache.PathFor(image.Path), "{\"Success\":true}");
        Assert.Null(cache.Read(image, Hash(image), "resource"));
    }

    [Fact]
    public void RejectsOversizedCacheWithoutDecryptingIt()
    {
        var image = Image(); var cache = new AnalysisCache(root); Directory.CreateDirectory(root);
        File.WriteAllBytes(cache.PathFor(image.Path), new byte[1024 * 1024 + 1]);
        Assert.Null(cache.Read(image, Hash(image), "resource"));
    }

    [Theory]
    [InlineData("rva")]
    [InlineData("expected")]
    [InlineData("duplicate")]
    [InlineData("outside")]
    public void RejectsInvalidPlanDespiteValidDpapi(string mutation)
    {
        var image = Image(); var cache = new AnalysisCache(root); var plan = Plan(image); var target = plan.Target!;
        target = mutation switch
        {
            "rva" => target with { PatchRva = 99 },
            "expected" => target with { ExpectedByte = 0x90 },
            "outside" => target with { PatchRawOffset = int.MaxValue },
            _ => target with { AdditionalEdits = [new(0x401, 0x1001, 0x7f, 0xeb)] }
        };
        cache.Write(image, Hash(image), "resource", plan with { Target = target });
        Assert.Null(cache.Read(image, Hash(image), "resource"));
    }

    [Fact]
    public void DoesNotCacheFailuresOrModifiedDiskImages()
    {
        var image = Image(); var cache = new AnalysisCache(root);
        Assert.False(cache.Write(image, Hash(image), "resource", new(false, null, 0, 0, [])));
        var plan = Plan(image);
        Assert.False(cache.Write(image, Hash(image), "resource", plan with { Target = plan.Target! with { State = PatchState.AlreadyPatched } }));
    }

    [Fact]
    public void UnwritableCacheDoesNotBreakAnalysis()
    {
        Directory.CreateDirectory(root); var blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "fixture");
        var image = Image(); var cache = new AnalysisCache(blocked);
        Assert.False(cache.Write(image, Hash(image), "resource", Plan(image)));
        Assert.Null(cache.Read(image, Hash(image), "resource"));
    }

    [Fact]
    public void ConcurrentWritesPublishOnlyCompletePlans()
    {
        var image = Image(); var cache = new AnalysisCache(root); var plan = Plan(image);
        Parallel.For(0, 8, _ => cache.Write(image, Hash(image), "resource", plan));
        Assert.NotNull(cache.Read(image, Hash(image), "resource"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    public void Dispose()
    {
        var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("mv2-cache-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
