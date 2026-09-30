using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;

namespace Mv2Enabler.Tests;

public sealed class ReleaseUpdateServiceTests
{
    [Fact]
    public void SelectsNewerGuiAssetWithDigest()
    {
        using var json = JsonDocument.Parse("""
            {
              "tag_name": "v3.5.0",
              "assets": [{
                "name": "ChromeMv2Launcher-v3.5.0-win-x64.zip",
                "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "browser_download_url": "https://github.com/onlytrisdev/chrome-enable-mv2/releases/download/v3.5.0/ChromeMv2Launcher-v3.5.0-win-x64.zip"
              }]
            }
            """);

        var update = ReleaseUpdateService.ParseLatestRelease(json.RootElement, new Version(3, 4, 0, 0));
        Assert.NotNull(update);
        Assert.Equal(new Version(3, 5, 0), update.Version);
        Assert.Equal(new string('a', 64), update.Sha256);
        Assert.Null(ReleaseUpdateService.ParseLatestRelease(json.RootElement, new Version(3, 5, 0, 0)));
    }

    [Fact]
    public void RejectsArchivePathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mv2-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "update.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntry("../outside.exe");
            }

            Assert.Throws<InvalidDataException>(() =>
                ReleaseUpdateService.ExtractVerifiedArchive(zip, Path.Combine(root, "stage")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractsFilesWithinStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "mv2-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "update.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("subfolder/readme.txt").Open());
                writer.Write("verified");
            }

            var stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(stage);
            ReleaseUpdateService.ExtractVerifiedArchive(zip, stage);
            Assert.Equal("verified", File.ReadAllText(Path.Combine(stage, "subfolder", "readme.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreatesWindowsShortcutInSpecifiedDirectory()
    {
        var executable = Environment.ProcessPath;
        Assert.NotNull(executable);
        var root = Path.Combine(Path.GetTempPath(), "mv2-shortcut-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var shortcut = Path.Combine(root, "Launcher.lnk");
            DesktopShortcutService.Create(shortcut, executable, "--auto-launch");
            Assert.True(new FileInfo(shortcut).Length > 0);
            Assert.True(DesktopShortcutService.IsOwnedShortcut(shortcut));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void PublishedGuiArchiveExtractsAsExpectedVersion()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var archivePath = Path.Combine(projectRoot, "artifacts", "release", "ChromeMv2Launcher-v3.5.0-win-x64.zip");
        if (!File.Exists(archivePath))
        {
            return;
        }

        var stage = Path.Combine(Path.GetTempPath(), "mv2-release-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            ReleaseUpdateService.ExtractVerifiedArchive(archivePath, stage);
            var launcher = Path.Combine(stage, "ChromeMv2Launcher.exe");
            Assert.True(File.Exists(launcher));
            Assert.True(File.Exists(Path.Combine(stage, "Mv2Enabler.Core.dll")));
            Assert.Equal("3.5.0.0", FileVersionInfo.GetVersionInfo(launcher).FileVersion);
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
        }
    }
}
