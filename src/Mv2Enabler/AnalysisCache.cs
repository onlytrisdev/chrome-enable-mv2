using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mv2Enabler;

internal sealed class AnalysisCache(string? directory = null)
{
    private const int Schema = 1;
    internal static string EngineIdentity => $"semantic-1/{typeof(AnalysisCache).Assembly.ManifestModule.ModuleVersionId}";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("onlytrisdev/ChromeMv2Launcher/analysis-cache/v1");
    private readonly string root = directory ?? Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ChromeMv2Launcher", "analysis-cache");
    private sealed record Entry(int Schema, string Engine, string ImageHash, string ResourceHash, LocatorResult Locator);

    internal string PathFor(string path) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))) + ".bin");

    public LocatorResult? Read(PeImage image, string hash, string resourceHash, Stream? source = null)
    {
        try
        {
            using var file = new FileStream(PathFor(image.Path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length is <= 0 or > 1024 * 1024) return null;
            var encrypted = new byte[(int)file.Length]; file.ReadExactly(encrypted);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(plain);
                if (entry is null || entry.Schema != Schema || entry.Engine != EngineIdentity ||
                    entry.ImageHash != hash || entry.ResourceHash != resourceHash || entry.Locator is null ||
                    !entry.Locator.Success || entry.Locator.Target is not { State: PatchState.Original } target ||
                    entry.Locator.Diagnostics is null || string.IsNullOrWhiteSpace(target.RuleId) ||
                    target.ImageSha256 != hash || target.AdditionalEdits is null || target.Evidence is null ||
                    target.AdditionalEdits.Count > 64 || target.AdditionalEdits.Any(e => e is null)) return null;
                var edits = ChromeDebugLauncher.GetEdits(target);
                if (edits.Select(e => e.PatchRawOffset).Distinct().Count() != edits.Count) return null;
                foreach (var edit in edits)
                {
                    if (edit.PatchRawOffset < 0 || edit.PatchRawOffset >= image.FileSize ||
                        image.RawOffsetToRva(edit.PatchRawOffset) != edit.PatchRva ||
                        !image.Sections.Any(s => s.IsExecutable && edit.PatchRawOffset >= s.RawOffset &&
                            (long)edit.PatchRawOffset < (long)s.RawOffset + s.RawSize)) return null;
                    if (source is not null)
                    { source.Position = edit.PatchRawOffset; if (source.ReadByte() != edit.ExpectedByte) return null; }
                    else if (edit.PatchRawOffset >= image.Bytes.Length || image.Bytes[edit.PatchRawOffset] != edit.ExpectedByte) return null;
                }
                return entry.Locator;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (IsCacheFailure(error)) { return null; }
    }

    public bool Write(PeImage image, string hash, string resourceHash, LocatorResult locator)
    {
        if (!locator.Success || locator.Target is not { State: PatchState.Original }) return false;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(root);
            var destination = PathFor(image.Path);
            temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var plain = JsonSerializer.SerializeToUtf8Bytes(new Entry(Schema, EngineIdentity, hash, resourceHash, locator));
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(encrypted); file.Flush(flushToDisk: true); }
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception error) when (IsCacheFailure(error)) { return false; }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception error) when (IsCacheFailure(error)) { }
        }
    }

    private static bool IsCacheFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        CryptographicException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException;
}
