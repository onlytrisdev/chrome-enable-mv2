<# :
@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ScriptFolder = '%~dp0'; Get-Content -LiteralPath '%~f0' | Select-Object -Skip 14 | Out-String | Invoke-Expression"
pause
exit /b
#>
#OnlyTris_Dev - Nguyễn Trí

Write-Host "Closing all running Chrome processes..." -ForegroundColor Yellow
Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

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
# tìm path
$dllsToPatch = @()
foreach ($dir in $searchDirs) {
    $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }
    foreach ($sd in $subDirs) {
        $dllPath = Join-Path $sd.FullName "chrome.dll"
        if (Test-Path -LiteralPath $dllPath) {
            $dllsToPatch += $dllPath
        }
    }
}

if ($dllsToPatch.Count -eq 0) {
    Write-Host "Could not find any Google Chrome installation or chrome.dll automatically." -ForegroundColor Red
    Exit
}

function doPatch([string]$dll) {
    Write-Host "`nFound chrome.dll at: $dll" -ForegroundColor Yellow

    $stream = $null
    try {
        $stream = [IO.File]::Open($dll, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    } catch [System.UnauthorizedAccessException] {
        Write-Host "[ERROR] Access Denied: You must run this script as Administrator to patch this Chrome installation ($dll)." -ForegroundColor Red
        return
    } catch {
        Write-Host "[ERROR] Could not open chrome.dll: $_" -ForegroundColor Red
        return
    }

    $reader = [IO.BinaryReader]$stream
    $enc = [Text.Encoding]::GetEncoding(28591)
    $features = @(
        '-ExtensionManifestV2Unsupported'
        '-ExtensionManifestV2Disabled'
        '-ExtensionsManifestV3Only'
        '+AllowLegacyMV2Extensions'
    )

    Write-Host "`tREADING Chrome.dll..." -ForegroundColor DarkGray
    [void]$stream.seek(0x3C, 0)
    $coff = $reader.ReadUint32() + 4
    $coffSize = 20
    [void]$stream.seek($coff, 0)
    $bytes = $reader.ReadBytes($coffSize + 2)
    $is64 = [BitConverter]::ToUInt16($bytes, 0) -eq 0x8664
    $isPE32 = [BitConverter]::ToUInt16($bytes, $coffSize) -eq 0x010b
    
    $peOffset = if ($isPE32) { 4 } else { 0 }
    [void]$stream.seek(($coff + $coffSize + 24 + $peOffset), 0)
    $imageBase = if ($isPE32) { $reader.ReadUInt32() } else { $reader.ReadUInt64() }

    $extraHeaderSize = [BitConverter]::ToUInt16($bytes, 16)
    $numSections = [BitConverter]::ToUInt16($bytes, 2)
    [void]$stream.seek(($coff + $coffSize + $extraHeaderSize), 0)
    $sections = @{}
    foreach ($i in 1..$numSections) {
        $sec = $reader.ReadBytes(40)
        $idx = $sec.IndexOf([byte]0)
        
        $len = 8
        if ($idx -ge 0) {
            $len = [System.Math]::Min(8, $idx)
        }
        
        $name = $enc.GetString($sec, 0, $len)
        if ($name -eq '.rdata' -or $name -eq '.data') {
            $sections[$name.substring(1)] = @{
                addr = $imageBase + [uint64][BitConverter]::ToUInt32($sec, 12);
                size = [BitConverter]::ToUInt32($sec, 16);
                filePos = [BitConverter]::ToUInt32($sec, 20);
            }
            if ($sections.count -eq 2) { break }
        }
    }

    if (!$sections.rdata) {
        throw "Could not find .rdata section"
    }
    [void]$stream.seek($sections.rdata.filePos, 0)
    $cur = 0
    $step = 1MB
    $featArea = 1kB
    $featData = [ordered]@{}
    while ($cur -lt $sections.rdata.size) {
        $bytes = $reader.ReadBytes($step)
        $str = $enc.GetString($bytes)
        foreach ($feat in $features) {
            if ($featData[$feat]) { continue }
            $pos = $str.indexOf($feat.substring(1))
            if ($pos -lt 0) { continue }
            $featData[$feat] = $sections.rdata.addr + $cur + $pos
        }
        if ($featData.count -eq $features.length) { break }
        $cur += $step - $featArea
        [void]$stream.seek(-$featArea, [IO.SeekOrigin]::Current)
    }
    if (!$sections.data) {
        throw "Could not find .data section"
    }

    [void]$stream.seek($sections.data.filePos, 0)
    $len = $sections.data.size
    $bytes = $reader.ReadBytes($len)
    if ($is64) { 
        $words = [uint64[]]::new($len -shr 3) 
    } else { 
        $words = [uint32[]]::new($len -shr 2) 
    }
    [Buffer]::BlockCopy($bytes, 0, $words, 0, $len)
    $bytes = $null
    $copied = $false
    $wordType = $words[0].getType()
    foreach ($feat in $featData.Keys) {
        $needle = $featData[$feat]
        $needle = $needle -as $wordType
        $val = ($feat[0] -eq '+') -as $wordType
        $featName = $feat.substring(1)
        $i = $words.indexOf($needle)
        while ($i -ge 0 -and !($words[$i + 1] -in 0,1)) {
            $i = [Array]::IndexOf($words, $needle, $i + 1)
        }
        if (!++$i) {
            Write-Host "Skipping $featName (not found or removed in this version)" -ForegroundColor Yellow
            continue
        }
        if ($words[$i] -eq $val) {
            Write-Host "Already patched $featName" -ForegroundColor DarkCyan
            continue
        }
        if (!$copied) {
            $copied = $true
            $stream.close()
            try {
                $stream = [IO.File]::Open($dll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
                if (Test-Path -LiteralPath "$dll.BAK") {
                    Write-Host "Found an existing backup of the original dll." -ForegroundColor DarkGray
                } else {
                    Write-Host "Backing up the original dll..." -ForegroundColor DarkGray
                    [IO.File]::Copy($dll, "$dll.BAK")
                }
            } catch [System.UnauthorizedAccessException] {
                Write-Host "[ERROR] Access Denied: You must run this script as Administrator to patch this Chrome installation ($dll)." -ForegroundColor Red
                return
            } catch {
                throw $_
            }
        }
        Write-Host "Patching $featName..." -ForegroundColor Cyan
        
        $multiplier = if ($is64) { 8 } else { 4 }
        [void]$stream.seek(($sections.data.filePos + $i * $multiplier), 0)
        $stream.WriteByte($val)
    }
    $stream.close()
    Write-Host "Patch completed successfully for this DLL." -ForegroundColor Green
}

foreach ($targetDll in $dllsToPatch) {
    try {
        doPatch $targetDll
    } catch {
        Write-Host "Error occurred during patch for $targetDll : $_" -ForegroundColor Red
    }
}

Write-Host "`nALL DONE. Please restart Google Chrome!" -ForegroundColor Green
