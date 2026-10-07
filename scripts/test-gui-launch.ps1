[CmdletBinding()]
param(
    [string]$LauncherPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64-gui\ChromeMv2Launcher.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$launcher = (Get-Item -LiteralPath $LauncherPath -ErrorAction Stop).FullName
if (Get-Process -Name chrome, ChromeMv2Launcher -ErrorAction SilentlyContinue)
{
    throw 'Close Chrome and Chrome MV2 Launcher before running the GUI integration test.'
}

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = Join-Path $temporaryBase ('mv2gui-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$guiProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Start-TestGui([string]$Mode, [string]$Profile)
{
    $arguments = @('--', ('--user-data-dir="' + $Profile + '"'),
        '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check', 'about:blank')
    if ($Mode -eq 'auto') { $arguments = @('--auto-launch') + $arguments }
    $gui = Start-Process -FilePath $launcher -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $guiProcesses.Add($gui)
    return $gui
}

function Find-Control([int]$ProcessId, [string]$AutomationId)
{
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
    $idCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $processCondition)
    foreach ($window in $windows)
    {
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCondition)
        if ($null -ne $control) { return $control }
    }
    return $null
}

function Invoke-TestButton([System.Diagnostics.Process]$Gui, [string]$AutomationId)
{
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 20)
    {
        if ($Gui.HasExited) { throw "GUI exited before $AutomationId could be invoked." }
        $button = Find-Control $Gui.Id $AutomationId
        if ($null -ne $button -and $button.Current.IsEnabled)
        {
            $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $pattern.Invoke()
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for the enabled $AutomationId button."
}

function Assert-GuiExited([System.Diagnostics.Process]$Gui)
{
    if (-not $Gui.WaitForExit(20000)) { throw "Launcher PID $($Gui.Id) did not exit after launch." }
    if ($Gui.ExitCode -ne 0) { throw "Launcher exited with code $($Gui.ExitCode)." }
}

function Assert-BrowserAlive([string]$Profile)
{
    $portFile = Join-Path $Profile 'DevToolsActivePort'
    $lastError = 'DevToolsActivePort was not created.'
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 10)
    {
        if (Test-Path -LiteralPath $portFile)
        {
            try
            {
                $port = [int](Get-Content -LiteralPath $portFile -TotalCount 1)
                $version = Invoke-RestMethod -Uri "http://127.0.0.1:$port/json/version" -TimeoutSec 2
                if ($version.Browser)
                {
                    return $version.Browser
                }
                $lastError = 'The version endpoint returned no Browser value.'
            }
            catch { $lastError = $_.Exception.Message }
        }
        Start-Sleep -Milliseconds 100
    }
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Contains($Profile, [System.StringComparison]::OrdinalIgnoreCase)
    })
    throw "Chrome did not remain available after the launcher exited. Profile: $Profile; matching processes: $($processes.Count); last endpoint error: $lastError"
}

function Stop-TestBrowsers
{
    $testBrowsers = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Contains($testRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.CommandLine.Contains('--type=', [System.StringComparison]::OrdinalIgnoreCase)
    }
    foreach ($browser in $testBrowsers)
    {
        $process = Get-Process -Id $browser.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $process)
        {
            try { $process.Kill($true); [void]$process.WaitForExit(5000) }
            finally { $process.Dispose() }
        }
    }
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ((Get-Process -Name chrome -ErrorAction SilentlyContinue) -and $timer.Elapsed.TotalSeconds -lt 5)
    {
        Start-Sleep -Milliseconds 100
    }
}

try
{
    $autoProfile = Join-Path $testRoot 'auto'
    $autoGui = Start-TestGui 'auto' $autoProfile
    Assert-GuiExited $autoGui
    $browserVersion = Assert-BrowserAlive $autoProfile
    Write-Host "PASS: shortcut launch exits the GUI while $browserVersion remains alive."

    $errorProfile = Join-Path $testRoot 'error'
    $errorGui = Start-TestGui 'auto' $errorProfile
    $errorShown = $false
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 15 -and -not $errorShown)
    {
        if ($errorGui.HasExited) { throw 'GUI closed instead of displaying the Chrome-already-running error.' }
        $bar = Find-Control $errorGui.Id 'ResultInfoBar'
        if ($null -ne $bar)
        {
            $text = $bar.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            $errorShown = ($text -join ' ') -match 'đóng hoàn toàn|Close every Chrome|完全关闭'
        }
        if (-not $errorShown) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $errorShown) { throw 'The expected Chrome-already-running error was not displayed.' }
    if (Test-Path -LiteralPath $errorProfile) { throw 'The failed launch unexpectedly created a browser profile.' }
    Write-Host 'PASS: failed launch keeps the GUI open and displays the close-Chrome error.'
    $errorGui.Kill(); [void]$errorGui.WaitForExit(5000)
    Stop-TestBrowsers

    foreach ($mode in @('manual', 'normal'))
    {
        $profile = Join-Path $testRoot $mode
        $gui = Start-TestGui $mode $profile
        $buttonId = if ($mode -eq 'manual') { 'LaunchButton' } else { 'OpenChromeButton' }
        Invoke-TestButton $gui $buttonId
        Assert-GuiExited $gui
        [void](Assert-BrowserAlive $profile)
        Write-Host "PASS: $mode launch exits the GUI and preserves the browser."
        Stop-TestBrowsers
    }
}
finally
{
    foreach ($gui in $guiProcesses)
    {
        try { if (-not $gui.HasExited) { $gui.Kill(); [void]$gui.WaitForExit(5000) } }
        finally { $gui.Dispose() }
    }
    Stop-TestBrowsers
    $resolvedRoot = [System.IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedRoot.StartsWith($temporaryBase + 'mv2gui-test-', [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw 'Refusing to clean an unexpected GUI-test directory.'
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
