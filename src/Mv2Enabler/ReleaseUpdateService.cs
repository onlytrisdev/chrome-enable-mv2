using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mv2Enabler;

internal sealed record ReleaseUpdate(Version Version, string Tag, Uri DownloadUrl, string Sha256);

internal static class ReleaseUpdateService
{
    private const string Repository = "onlytrisdev/chrome-enable-mv2";
    private const long MaximumArchiveBytes = 300L * 1024 * 1024;
    private const long MaximumExpandedBytes = 900L * 1024 * 1024;
    private static readonly Uri LatestReleaseUrl = new(
        "https://api.github.com/repos/onlytrisdev/chrome-enable-mv2/releases/latest");

    public static async Task<ReleaseUpdate?> CheckAsync(Version installedVersion, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return ParseLatestRelease(document.RootElement, installedVersion);
    }

    internal static ReleaseUpdate? ParseLatestRelease(JsonElement release, Version installedVersion)
    {
        var tag = release.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("The release has no tag.");
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
            version.Build < 0 || version.Revision >= 0)
        {
            throw new InvalidDataException("The latest release tag is not vMAJOR.MINOR.PATCH.");
        }

        if (Normalize(version).CompareTo(Normalize(installedVersion)) <= 0)
        {
            return null;
        }

        var filename = $"ChromeMv2Launcher-{tag}-win-x64.zip";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != filename)
            {
                continue;
            }

            var digest = asset.GetProperty("digest").GetString();
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("The GUI release asset has no valid SHA-256 digest.");
            }

            var urlText = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                url.AbsolutePath != $"/{Repository}/releases/download/{tag}/{filename}")
            {
                throw new InvalidDataException("The GUI release asset URL is unexpected.");
            }

            return new ReleaseUpdate(Normalize(version), tag, url, digest[7..].ToLowerInvariant());
        }

        throw new InvalidDataException($"The latest release does not contain {filename}.");
    }

    public static async Task<string> InstallAsync(ReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChromeMv2Launcher", "versions");
        var destination = Path.Combine(root, $"{update.Version.Major}.{update.Version.Minor}.{update.Version.Build}-{update.Sha256[..12]}");
        if (Directory.Exists(destination))
        {
            return ValidateInstalledApp(destination, update.Version);
        }

        Directory.CreateDirectory(root);
        var stage = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var client = CreateClient(TimeSpan.FromMinutes(10)))
            using (var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumArchiveBytes)
                {
                    throw new InvalidDataException("The update archive is too large.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(archivePath);
                var buffer = new byte[128 * 1024];
                long length = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    length += read;
                    if (length > MaximumArchiveBytes)
                    {
                        throw new InvalidDataException("The update archive is too large.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await using (var input = File.OpenRead(archivePath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
                }
            }

            Directory.CreateDirectory(stage);
            ExtractVerifiedArchive(archivePath, stage);
            ValidateInstalledApp(stage, update.Version);
            Directory.Move(stage, destination);
            return Path.Combine(destination, "ChromeMv2Launcher.exe");
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    internal static void ExtractVerifiedArchive(string archivePath, string stage)
    {
        var stagePrefix = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long expanded = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The expanded update is too large.");
            }

            var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedName) || normalizedName.Contains(':'))
            {
                throw new InvalidDataException("The update contains an invalid path.");
            }

            var output = Path.GetFullPath(Path.Combine(stage, normalizedName));
            if (!output.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update contains a path outside its staging directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, overwrite: false);
        }
    }

    private static string ValidateInstalledApp(string directory, Version expectedVersion)
    {
        var executable = Path.Combine(directory, "ChromeMv2Launcher.exe");
        if (!File.Exists(executable) ||
            !File.Exists(Path.Combine(directory, "Mv2Enabler.Core.dll")) ||
            !File.Exists(Path.Combine(directory, "ChromeMv2Launcher.runtimeconfig.json")))
        {
            throw new InvalidDataException("The update archive is missing required application files.");
        }

        var fileVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion;
        if (!Version.TryParse(fileVersion, out var actualVersion) ||
            Normalize(actualVersion) != Normalize(expectedVersion))
        {
            throw new InvalidDataException("The update executable version does not match the release tag.");
        }

        return executable;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChromeMv2Launcher/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
