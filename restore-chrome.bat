<# :
@echo off
if "%~1"=="-Elevated" goto :AdminChecked
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '-Elevated' -Verb RunAs"
    exit /b
)
:AdminChecked

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ScriptFolder = '%~dp0'; Get-Content -LiteralPath '%~f0' | Select-Object -Skip 15 | Out-String | Invoke-Expression"
pause
exit /b
#>

# Close all Chrome processes to prevent file locking
Write-Host "Closing all running Chrome processes..." -ForegroundColor Yellow
Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 1. Gather all potential Chrome directories
$searchDirs = @()

if ($ScriptFolder) {
    $searchDirs += $ScriptFolder
}

$appPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
)
foreach ($ap in $appPaths) {
    $p = (Get-ItemProperty -Path $ap -ErrorAction SilentlyContinue)."(default)"
    if ($p) { 
        $searchDirs += Split-Path -Parent $p 
    }
}

$searchDirs += "C:\Program Files\Google\Chrome\Application"
$searchDirs += "C:\Program Files (x86)\Google\Chrome\Application"
$searchDirs += "$env:LocalAppData\Google\Chrome\Application"

$searchDirs = $searchDirs | Select-Object -Unique | Where-Object { $_ -and (Test-Path -Path $_ -PathType Container) }

$restoredCount = 0
foreach ($dir in $searchDirs) {
    $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }
    foreach ($sd in $subDirs) {
        $bakPath = Join-Path $sd.FullName "chrome.dll.BAK"
        $dllPath = Join-Path $sd.FullName "chrome.dll"
        if (Test-Path -LiteralPath $bakPath) {
            Write-Host "Found backup file at: $bakPath" -ForegroundColor Yellow
            try {
                # Remove current patched DLL if it exists
                if (Test-Path -LiteralPath $dllPath) {
                    Remove-Item -LiteralPath $dllPath -Force
                }
                # Rename BAK to DLL
                Rename-Item -LiteralPath $bakPath -NewName "chrome.dll" -Force
                Write-Host "[SUCCESS] Restored original chrome.dll for: $($sd.Name)" -ForegroundColor Green
                $restoredCount++
            } catch [System.UnauthorizedAccessException] {
                Write-Host "[ERROR] Access Denied: You must run this script as Administrator to restore (${dllPath})." -ForegroundColor Red
            } catch {
                Write-Host "[ERROR] Failed to restore ${dllPath}: $_" -ForegroundColor Red
            }
        }
    }
}

if ($restoredCount -eq 0) {
    Write-Host "No backup files (chrome.dll.BAK) were found to restore." -ForegroundColor Yellow
} else {
    Write-Host "`nALL DONE. Original Chrome has been restored!" -ForegroundColor Green
}
