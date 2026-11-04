[CmdletBinding()]
param(
    [string]$CliPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64-self-contained\mv2ctl.exe'),
    [string]$ChromePath = '',
    [ValidateRange(1, 20)][int]$Iterations = 5
)
$ErrorActionPreference = 'Stop'
$cli = (Get-Item -LiteralPath $CliPath).FullName
function Measure-Analysis([bool]$Cold)
{
    $arguments = @('analyze', '--json')
    if ($ChromePath) { $arguments += @('--chrome', $ChromePath) }
    if ($Cold) { $arguments += '--no-cache' }
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $json = & $cli @arguments
    $exit = $LASTEXITCODE
    $timer.Stop()
    if ($exit -ne 0) { throw "Analysis failed ($exit): $json" }
    $report = ($json -join "`n") | ConvertFrom-Json
    if (-not $report.Success -or $report.Target.RuleId -ne 'chromium.mv2-semantic.local.v1')
    { throw 'Benchmark did not exercise automatic semantic discovery.' }
    if ($Cold -and $report.CacheHit) { throw 'Forced analysis unexpectedly used cache.' }
    [pscustomobject]@{ Mode = $(if ($Cold) { 'forced-scan' } else { 'cache' });
        Chrome = $report.Version; CacheHit = $report.CacheHit;
        AnalysisMilliseconds = $report.AnalysisMilliseconds; ProcessMilliseconds = $timer.ElapsedMilliseconds }
}
$results = @()
for ($i = 0; $i -lt $Iterations; $i++) { $results += Measure-Analysis $true }
# Seed a successful cache; the warm samples must then genuinely avoid discovery.
[void](Measure-Analysis $false)
for ($i = 0; $i -lt $Iterations; $i++)
{
    $sample = Measure-Analysis $false
    if (-not $sample.CacheHit) { throw 'Warm analysis did not reuse its verified cache.' }
    $results += $sample
}
$results
