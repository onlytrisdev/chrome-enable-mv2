[CmdletBinding()]
param(
    [string]$Version = "3.6.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot "artifacts"
$releaseRoot = Join-Path $artifactRoot "release"
$guiPublish = Join-Path $artifactRoot "win-x64-gui"
$cliPublish = Join-Path $artifactRoot "win-x64-self-contained"

$runningLauncher = Get-Process -Name "ChromeMv2Launcher" -ErrorAction SilentlyContinue
if ($null -ne $runningLauncher)
{
    $processIds = ($runningLauncher.Id | Sort-Object) -join ", "
    throw "Close Chrome MV2 Launcher before building. Running PID(s): $processIds"
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
foreach ($publishPath in @($guiPublish, $cliPublish))
{
    $resolvedPublishPath = [System.IO.Path]::GetFullPath($publishPath)
    if (-not $resolvedPublishPath.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean publish path outside the artifacts directory: $resolvedPublishPath"
    }

    Remove-Item -LiteralPath $resolvedPublishPath -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish (Join-Path $projectRoot "src\Mv2Enabler.Gui\Mv2Enabler.Gui.csproj") `
    -c Release -r win-x64 --self-contained true -o $guiPublish
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }

dotnet publish (Join-Path $projectRoot "src\Mv2Enabler\Mv2Enabler.csproj") `
    -c Release -r win-x64 --self-contained true -o $cliPublish
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }

Get-ChildItem -Path $guiPublish, $cliPublish -Filter "*.pdb" -File | Remove-Item -Force
Copy-Item (Join-Path $projectRoot "README.vi.md") (Join-Path $guiPublish "README.vi.md") -Force
Copy-Item (Join-Path $projectRoot "README.md") (Join-Path $guiPublish "README.md") -Force
Copy-Item (Join-Path $projectRoot "SECURITY.md") (Join-Path $guiPublish "SECURITY.md") -Force
Copy-Item (Join-Path $projectRoot "README.vi.md") (Join-Path $cliPublish "README.vi.md") -Force
Copy-Item (Join-Path $projectRoot "README.md") (Join-Path $cliPublish "README.md") -Force
Copy-Item (Join-Path $projectRoot "SECURITY.md") (Join-Path $cliPublish "SECURITY.md") -Force

$guiZip = Join-Path $releaseRoot "ChromeMv2Launcher-v$Version-win-x64.zip"
$cliZip = Join-Path $releaseRoot "mv2ctl-v$Version-win-x64.zip"
Remove-Item -LiteralPath $guiZip, $cliZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $guiPublish "*") -DestinationPath $guiZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $cliPublish "*") -DestinationPath $cliZip -CompressionLevel Optimal

$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"
$checksums = @($guiZip, $cliZip) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $_)
}
Set-Content -LiteralPath $checksumPath -Value $checksums -Encoding utf8NoBOM

Write-Host "Release artifacts:"
Get-Item -LiteralPath $guiZip, $cliZip, $checksumPath | Select-Object Name, Length, LastWriteTime
