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

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ScriptFolder = '%~dp0'; Get-Content -LiteralPath '%~f0' -Encoding UTF8 | Select-Object -Skip 14 | Out-String | Invoke-Expression"
exit /b
#>
#OnlyTris_Dev - Nguyễn Trí & AI
# Chrome Manifest V2 Manager (Batch/PowerShell Hybrid with Multi-language support)

# ----------------- LOCALIZATION SETTINGS -----------------

$Lang = "en"  # Default language is English

$Msg = @{
    "en" = @{
        Title = "           CHROME MANIFEST V2 MANAGER & EXTENSIONS"
        Opt1 = " [1] Patch Google Chrome (Enable Manifest V2 support)"
        Opt2 = " [2] Download & Extract uBlock Origin MV2 from GitHub"
        Opt3 = " [3] Download & Extract any Extension from Chrome Web Store"
        Opt4 = " [4] Restore Google Chrome to original state"
        Opt5 = " [5] Switch Language (Tiếng Việt)"
        Opt6 = " [6] Exit"
        Prompt = "Select an option (1-6)"
        Invalid = "Invalid option! Please choose from 1 to 6."
        EnterToReturn = "Press Enter to return to menu..."
        
        PatchTitle = "=== PATCHING GOOGLE CHROME ==="
        ClosingChrome = "Closing all running Chrome processes..."
        NoChromeFound = "Could not find any Google Chrome installation or chrome.dll automatically."
        AllDoneChrome = "ALL DONE. Please restart Google Chrome!"
        
        PatchFoundDll = "Found chrome.dll at: {0}"
        PatchAccessDenied = "[ERROR] Access Denied: You must run this script as Administrator to patch this Chrome installation ({0})."
        PatchOpenError = "[ERROR] Could not open chrome.dll: {0}"
        PatchReading = "`tREADING Chrome.dll..."
        PatchAlreadyPatched = "Already patched {0}"
        PatchBackupFound = "Found an existing backup of the original dll."
        PatchBackupCreating = "Backing up the original dll..."
        PatchingFeature = "Patching {0}..."
        PatchSuccess = "Patch completed successfully for this DLL."
        PatchSkipping = "Skipping {0} (not found or removed in this version)"
        
        UBlockTitle = "=== DOWNLOAD & EXTRACT UBLOCK ORIGIN MV2 ==="
        CheckingUBlock = "Checking latest uBlock Origin version on GitHub..."
        UBlockAPIFail = "Could not connect to GitHub API. Trying fallback method..."
        UBlockFallbackFail = "Fallback method failed: {0}"
        UBlockTagFallback = "Failed to fetch tag, switching to default (1.57.2)..."
        TargetTag = "Target Tag: {0}"
        DownloadUrl = "Download URL: {0}"
        DownloadingFile = "Downloading {0}..."
        DownloadSuccess = "Download completed successfully!"
        DownloadFail = "[ERROR] Failed to download uBlock Origin: {0}"
        ExtractingTo = "Extracting to {0}..."
        ExtractSuccess = "Extraction completed!"
        ExtractFail = "[ERROR] Failed to extract archive: {0}"
        UpdatingPrefs = "Updating Preferences..."
        DevModeEnabled = "`t[OK] Enabled Developer Mode for profile: {0}"
        DevModeAlreadyEnabled = "`t[OK] Developer Mode already enabled for profile: {0}"
        DevModeFail = "`t[WARNING] Could not edit Preferences for profile {0}: {1}"
        ClipboardSuccess = "[OK] Copied extension path to clipboard!"
        ClipboardPath = "Path: {0}"
        ClipboardFail = "[WARNING] Could not copy path to Clipboard: {0}"
        OpeningExplorer = "Opening extracted folder..."
        GuideTitle = "              EXTENSION INSTALLATION GUIDE"
        GuideStep1 = "1. Open Google Chrome."
        GuideStep2 = "2. Go to: chrome://extensions"
        GuideStep3 = "3. Enable 'Developer mode' toggle in the top-right corner."
        GuideStep4 = "4. Click the 'Load unpacked' button in the top-left corner."
        GuideStep5 = "5. Paste the copied path by pressing Ctrl + V, then press Enter."
        
        StoreTitle = "=== DOWNLOAD & EXTRACT CHROME WEB STORE EXTENSION ==="
        InputStorePrompt = "Enter Chrome Web Store link or Extension ID"
        InvalidStoreId = "[ERROR] Invalid Extension ID (must be a 32-character string from a to p)."
        StoreIdFound = "Extension ID detected: {0}"
        DownloadingStore = "Downloading extension from Chrome Web Store..."
        DownloadStoreFail = "[ERROR] Failed to download extension: {0}"
        CRXHeaderFail = "[WARNING] Error parsing CRX header: {0}. Will try extracting directly..."
        
        RestoreTitle = "=== RESTORING ORIGINAL CHROME ==="
        FoundBackup = "Found backup file at: {0}"
        RestoreSuccess = "[SUCCESS] Restored original chrome.dll for: {0}"
        RestoreAccessDenied = "[ERROR] Access Denied: You must run this script as Administrator to restore ({0})."
        RestoreFail = "[ERROR] Failed to restore {0}: {1}"
        NoBackupFound = "No backup files (chrome.dll.BAK) were found to restore."
        RestoreAllDone = "ALL DONE. Original Chrome has been restored!"
    }
    "vi" = @{
        Title = "           CHROME MANIFEST V2 MANAGER & EXTENSIONS"
        Opt1 = " [1] Vá Google Chrome (Kích hoạt hỗ trợ Manifest V2)"
        Opt2 = " [2] Tải & Cài đặt tự động uBlock Origin MV2 từ GitHub"
        Opt3 = " [3] Tải & Giải nén Extension bất kỳ từ Chrome Web Store"
        Opt4 = " [4] Khôi phục Google Chrome về nguyên bản"
        Opt5 = " [5] Chuyển đổi ngôn ngữ (English)"
        Opt6 = " [6] Thoát"
        Prompt = "Nhập lựa chọn của bạn (1-6)"
        Invalid = "Lựa chọn không hợp lệ! Vui lòng chọn từ 1 đến 6."
        EnterToReturn = "Nhấn Enter để quay lại menu..."
        
        PatchTitle = "=== ĐANG TIẾN HÀNH VÁ CHROME ==="
        ClosingChrome = "Đang đóng các tiến trình Chrome đang chạy..."
        NoChromeFound = "Không tìm thấy đường dẫn cài đặt Google Chrome hoặc chrome.dll tự động."
        AllDoneChrome = "HOÀN TẤT. Vui lòng khởi động lại Google Chrome!"
        
        PatchFoundDll = "Tìm thấy tệp chrome.dll tại: {0}"
        PatchAccessDenied = "[ERROR] Quyền truy cập bị từ chối: Bạn phải chạy script này với tư cách Quản trị viên để vá Chrome ({0})."
        PatchOpenError = "[ERROR] Không thể mở chrome.dll: {0}"
        PatchReading = "`tĐang đọc file Chrome.dll..."
        PatchAlreadyPatched = "Đã được vá sẵn tính năng: {0}"
        PatchBackupFound = "Tìm thấy tệp sao lưu dll gốc đã có sẵn."
        PatchBackupCreating = "Đang sao lưu tệp dll gốc..."
        PatchingFeature = "Đang vá tính năng: {0}..."
        PatchSuccess = "Đã vá thành công tệp DLL này."
        PatchSkipping = "Bỏ qua {0} (không tìm thấy hoặc đã bị gỡ bỏ trong phiên bản này)"
        
        UBlockTitle = "=== TẢI & CÀI ĐẶT UBLOCK ORIGIN MV2 ==="
        CheckingUBlock = "Đang kiểm tra phiên bản uBlock Origin mới nhất trên GitHub..."
        UBlockAPIFail = "Không thể kết nối API GitHub (có thể do bị giới hạn IP). Đang thử phương pháp dự phòng..."
        UBlockFallbackFail = "Lỗi phương pháp dự phòng: {0}"
        UBlockTagFallback = "Không lấy được thông tin từ GitHub, chuyển sang tải bản mặc định (1.57.2)..."
        TargetTag = "Phiên bản đích: {0}"
        DownloadUrl = "Đường dẫn tải: {0}"
        DownloadingFile = "Đang tải tệp {0}..."
        DownloadSuccess = "Tải xuống thành công!"
        DownloadFail = "[ERROR] Không thể tải xuống uBlock Origin: {0}"
        ExtractingTo = "Đang giải nén vào: {0}..."
        ExtractSuccess = "Giải nén hoàn tất!"
        ExtractFail = "[ERROR] Lỗi khi giải nén tệp tin: {0}"
        UpdatingPrefs = "Đang cập nhật cấu hình Preferences..."
        DevModeEnabled = "`t[OK] Đã tự động kích hoạt Developer Mode cho profile: {0}"
        DevModeAlreadyEnabled = "`t[OK] Developer Mode đã được bật sẵn ở profile: {0}"
        DevModeFail = "`t[CẢNH BÁO] Không thể chỉnh sửa Preferences của profile {0}: {1}"
        ClipboardSuccess = "[OK] Đã tự động copy đường dẫn tiện ích vào clipboard!"
        ClipboardPath = "Đường dẫn: {0}"
        ClipboardFail = "[CẢNH BÁO] Không thể copy đường dẫn tự động vào Clipboard: {0}"
        OpeningExplorer = "Đang mở thư mục giải nén..."
        GuideTitle = "              HƯỚNG DẪN CÀI ĐẶT TIỆN ÍCH"
        GuideStep1 = "1. Mở trình duyệt Google Chrome."
        GuideStep2 = "2. Truy cập địa chỉ: chrome://extensions"
        GuideStep3 = "3. Bật 'Chế độ dành cho nhà phát triển' (Developer mode) ở góc trên bên phải."
        GuideStep4 = "4. Nhấp vào nút 'Tải tiện ích đã giải nén' (Load unpacked) ở góc trên bên trái."
        GuideStep5 = "5. Dán đường dẫn đã copy bằng cách nhấn Ctrl + V rồi nhấn Enter."
        
        StoreTitle = "=== TẢI & GIẢI NÉN EXTENSION TỪ CHROME WEB STORE ==="
        InputStorePrompt = "Nhập link Chrome Web Store hoặc Extension ID"
        InvalidStoreId = "[ERROR] Không tìm thấy Extension ID hợp lệ (phải là chuỗi 32 ký tự từ a đến p)."
        StoreIdFound = "Extension ID phát hiện: {0}"
        DownloadingStore = "Đang tải tệp extension từ Chrome Web Store..."
        DownloadStoreFail = "[ERROR] Không thể tải xuống tiện ích: {0}"
        CRXHeaderFail = "[CẢNH BÁO] Lỗi khi phân tích tiêu đề CRX: {0}. Sẽ thử giải nén trực tiếp..."
        
        RestoreTitle = "=== ĐANG TIẾN HÀNH KHÔI PHỤC CHROME NGUYÊN BẢN ==="
        FoundBackup = "Tìm thấy tệp sao lưu tại: {0}"
        RestoreSuccess = "[SUCCESS] Đã khôi phục chrome.dll nguyên bản cho: {0}"
        RestoreAccessDenied = "[ERROR] Quyền truy cập bị từ chối: Bạn phải chạy script này với tư cách Quản trị viên để khôi phục ({0})."
        RestoreFail = "[ERROR] Không thể khôi phục {0}: {1}"
        NoBackupFound = "Không tìm thấy tệp sao lưu (chrome.dll.BAK) để khôi phục."
        RestoreAllDone = "HOÀN TẤT. Google Chrome nguyên bản đã được khôi phục!"
    }
}

# ----------------- GLOBALS & HELPERS -----------------

function doPatch([string]$dll) {
    Write-Host ($Msg[$Lang].PatchFoundDll -f $dll) -ForegroundColor Yellow

    $stream = $null
    try {
        $stream = [IO.File]::Open($dll, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    } catch [System.UnauthorizedAccessException] {
        Write-Host ($Msg[$Lang].PatchAccessDenied -f $dll) -ForegroundColor Red
        return
    } catch {
        Write-Host ($Msg[$Lang].PatchOpenError -f $_) -ForegroundColor Red
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

    Write-Host $Msg[$Lang].PatchReading -ForegroundColor DarkGray
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
        $i = [Array]::IndexOf($words, $needle)
        while ($i -ge 0) {
            if ($i + 1 -ge $words.Length) {
                $i = -1
                break
            }
            $nextWord = $words[$i + 1]
            $nextVal = if ($is64) { $nextWord -band 0xFFFFFFFFL } else { $nextWord }
            if ($nextVal -in 0,1) { break }
            $i = [Array]::IndexOf($words, $needle, $i + 1)
        }
        if (!++$i) {
            Write-Host ($Msg[$Lang].PatchSkipping -f $featName) -ForegroundColor Yellow
            continue
        }
        $currentVal = if ($is64) { $words[$i] -band 0xFFFFFFFFL } else { $words[$i] }
        if ($currentVal -eq $val) {
            Write-Host ($Msg[$Lang].PatchAlreadyPatched -f $featName) -ForegroundColor DarkCyan
            continue
        }
        if (!$copied) {
            $copied = $true
            $stream.close()
            try {
                $stream = [IO.File]::Open($dll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
                if (Test-Path -LiteralPath "$dll.BAK") {
                    Write-Host $Msg[$Lang].PatchBackupFound -ForegroundColor DarkGray
                } else {
                    Write-Host $Msg[$Lang].PatchBackupCreating -ForegroundColor DarkGray
                    [IO.File]::Copy($dll, "$dll.BAK")
                }
            } catch [System.UnauthorizedAccessException] {
                Write-Host ($Msg[$Lang].PatchAccessDenied -f $dll) -ForegroundColor Red
                return
            } catch {
                throw $_
            }
        }
        Write-Host ($Msg[$Lang].PatchingFeature -f $featName) -ForegroundColor Cyan
        
        $multiplier = if ($is64) { 8 } else { 4 }
        [void]$stream.seek(($sections.data.filePos + $i * $multiplier), 0)
        $stream.WriteByte($val)
    }
    $stream.close()
    Write-Host $Msg[$Lang].PatchSuccess -ForegroundColor Green
}

function Get-ChromeDirs {
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
    
    return $searchDirs | Select-Object -Unique | Where-Object { $_ -and (Test-Path -Path $_ -PathType Container) }
}

# ----------------- OPTION 1: PATCH CHROME -----------------

function Option-PatchChrome {
    Write-Host "`n$($Msg[$Lang].PatchTitle)" -ForegroundColor Yellow
    Write-Host $Msg[$Lang].ClosingChrome -ForegroundColor Yellow
    Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    $chromeDirs = Get-ChromeDirs
    $dllsToPatch = @()
    foreach ($dir in $chromeDirs) {
        $pathsToCheck = @()
        $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }
        foreach ($sd in $subDirs) {
            $pathsToCheck += $sd.FullName
        }
        $pathsToCheck += $dir
        foreach ($path in ($pathsToCheck | Select-Object -Unique)) {
            $dllPath = Join-Path $path "chrome.dll"
            if (Test-Path -LiteralPath $dllPath) {
                $dllsToPatch += $dllPath
            }
        }
    }

    if ($dllsToPatch.Count -eq 0) {
        Write-Host $Msg[$Lang].NoChromeFound -ForegroundColor Red
    } else {
        foreach ($targetDll in $dllsToPatch) {
            try {
                doPatch $targetDll
            } catch {
                Write-Host "Error occurred during patch for $targetDll : $_" -ForegroundColor Red
            }
        }
        Write-Host "`n$($Msg[$Lang].AllDoneChrome)" -ForegroundColor Green
    }
    
    Write-Host ""
    Read-Host $Msg[$Lang].EnterToReturn
}

# ----------------- OPTION 2: DOWNLOAD & INSTALL UBLOCK -----------------

function Option-InstalluBlock {
    Write-Host "`n$($Msg[$Lang].UBlockTitle)" -ForegroundColor Yellow
    
    $tag = $null
    $downloadUrl = $null
    $fileName = $null

    # 1. Fetch latest release information
    Write-Host $Msg[$Lang].CheckingUBlock -ForegroundColor DarkGray
    try {
        $apiURL = "https://api.github.com/repos/gorhill/uBlock/releases/latest"
        $response = Invoke-RestMethod -Uri $apiURL -Headers @{"User-Agent"="Mozilla/5.0 (Windows NT 10.0; Win64; x64)"} -ErrorAction Stop
        $asset = $response.assets | Where-Object { $_.name -like "*chromium.zip" } | Select-Object -First 1
        if ($asset) {
            $downloadUrl = $asset.browser_download_url
            $fileName = $asset.name
            $tag = $response.tag_name
        }
    } catch {
        Write-Host $Msg[$Lang].UBlockAPIFail -ForegroundColor Yellow
    }

    if (-not $downloadUrl) {
        try {
            $req = [System.Net.WebRequest]::Create("https://github.com/gorhill/uBlock/releases/latest")
            $req.AllowAutoRedirect = $false
            $req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            $resp = $req.GetResponse()
            $loc = $resp.Headers["Location"]
            $resp.Close()
            if ($loc -and $loc -match "tag/(.*)$") {
                $tag = $Matches[1]
                $fileName = "uBlock0_$tag.chromium.zip"
                $downloadUrl = "https://github.com/gorhill/uBlock/releases/download/$tag/$fileName"
            }
        } catch {
            Write-Host ($Msg[$Lang].UBlockFallbackFail -f $_) -ForegroundColor Red
        }
    }

    if (-not $downloadUrl) {
        $tag = "1.57.2"
        $fileName = "uBlock0_1.57.2.chromium.zip"
        $downloadUrl = "https://github.com/gorhill/uBlock/releases/download/1.57.2/uBlock0_1.57.2.chromium.zip"
        Write-Host $Msg[$Lang].UBlockTagFallback -ForegroundColor Yellow
    }

    Write-Host ($Msg[$Lang].TargetTag -f $tag) -ForegroundColor Green
    Write-Host ($Msg[$Lang].DownloadUrl -f $downloadUrl) -ForegroundColor DarkGray

    $destZip = Join-Path $ScriptFolder $fileName
    $extractDir = Join-Path $ScriptFolder "uBlock0_chromium"

    try {
        Write-Host ($Msg[$Lang].DownloadingFile -f $fileName) -ForegroundColor Yellow
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 -bor 12288 } catch {}
        Invoke-WebRequest -Uri $downloadUrl -OutFile $destZip -UseBasicParsing -ErrorAction Stop
        Write-Host $Msg[$Lang].DownloadSuccess -ForegroundColor Green
    } catch {
        Write-Host ($Msg[$Lang].DownloadFail -f $_) -ForegroundColor Red
        Write-Host ""
        Read-Host $Msg[$Lang].EnterToReturn
        return
    }

    try {
        Write-Host ($Msg[$Lang].ExtractingTo -f $extractDir) -ForegroundColor Yellow
        if (Test-Path $extractDir) {
            Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        
        if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) {
            Expand-Archive -Path $destZip -DestinationPath $extractDir -Force
        } else {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($destZip, $extractDir)
        }
        
        Remove-Item -Path $destZip -Force -ErrorAction SilentlyContinue
        Write-Host $Msg[$Lang].ExtractSuccess -ForegroundColor Green
    } catch {
        Write-Host ($Msg[$Lang].ExtractFail -f $_) -ForegroundColor Red
        Write-Host ""
        Read-Host $Msg[$Lang].EnterToReturn
        return
    }

    $extensionPath = $extractDir
    $manifestFile = Get-ChildItem -Path $extractDir -Filter "manifest.json" -Recurse | Select-Object -First 1
    if ($manifestFile) {
        $extensionPath = $manifestFile.Directory.FullName
    }

    Write-Host "`n$($Msg[$Lang].UpdatingPrefs)" -ForegroundColor Yellow

    $userDataDir = "$env:LocalAppData\Google\Chrome\User Data"
    if (Test-Path $userDataDir) {
        $prefFiles = Get-ChildItem -Path $userDataDir -Filter "Preferences" -Recurse -Depth 2 -ErrorAction SilentlyContinue
        foreach ($prefFile in $prefFiles) {
            try {
                $jsonText = Get-Content -LiteralPath $prefFile.FullName -Raw -Encoding UTF8
                $json = ConvertFrom-Json $jsonText
                
                $modified = $false
                if ($null -eq $json.extensions) {
                    $json | Add-Member -MemberType NoteProperty -Name "extensions" -Value ([pscustomobject]@{ ui = ([pscustomobject]@{ developer_mode = $true }) })
                    $modified = $true
                } else {
                    if ($null -eq $json.extensions.ui) {
                        $json.extensions | Add-Member -MemberType NoteProperty -Name "ui" -Value ([pscustomobject]@{ developer_mode = $true })
                        $modified = $true
                    } else {
                        if ($json.extensions.ui.developer_mode -ne $true) {
                            if ($null -eq $json.extensions.ui.developer_mode) {
                                $json.extensions.ui | Add-Member -MemberType NoteProperty -Name "developer_mode" -Value $true -Force
                            } else {
                                $json.extensions.ui.developer_mode = $true
                            }
                            $modified = $true
                        }
                    }
                }
                
                if ($modified) {
                    $newContent = $json | ConvertTo-Json -Depth 100
                    [IO.File]::WriteAllText($prefFile.FullName, $newContent, [System.Text.Encoding]::UTF8)
                    Write-Host ($Msg[$Lang].DevModeEnabled -f $prefFile.Directory.Name) -ForegroundColor Green
                } else {
                    Write-Host ($Msg[$Lang].DevModeAlreadyEnabled -f $prefFile.Directory.Name) -ForegroundColor DarkCyan
                }
            } catch {
                Write-Host ($Msg[$Lang].DevModeFail -f $prefFile.Directory.Name, $_) -ForegroundColor Yellow
            }
        }
    }

    try {
        if (Get-Command Set-Clipboard -ErrorAction SilentlyContinue) {
            Set-Clipboard -Value $extensionPath
        } else {
            $extensionPath | clip.exe
        }
        Write-Host "`n$($Msg[$Lang].ClipboardSuccess)" -ForegroundColor Green
        Write-Host ($Msg[$Lang].ClipboardPath -f $extensionPath) -ForegroundColor Cyan
    } catch {
        Write-Host "`n$($Msg[$Lang].ClipboardFail -f $_)" -ForegroundColor Yellow
    }

    Write-Host "`n$($Msg[$Lang].OpeningExplorer)" -ForegroundColor DarkGray
    Start-Process explorer.exe -ArgumentList ('"{0}"' -f $extensionPath)

    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].GuideTitle -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].GuideStep1 -ForegroundColor White
    Write-Host $Msg[$Lang].GuideStep2 -ForegroundColor White
    Write-Host $Msg[$Lang].GuideStep3 -ForegroundColor Yellow
    Write-Host $Msg[$Lang].GuideStep4 -ForegroundColor Yellow
    Write-Host $Msg[$Lang].GuideStep5 -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Cyan

    Write-Host ""
    Read-Host $Msg[$Lang].EnterToReturn
}

# ----------------- OPTION 3: DOWNLOAD & INSTALL ANY EXTENSION FROM WEB STORE -----------------

function Option-InstallFromStore {
    Write-Host "`n$($Msg[$Lang].StoreTitle)" -ForegroundColor Yellow
    
    $inputUrl = Read-Host $Msg[$Lang].InputStorePrompt
    $extId = $null
    
    if ($inputUrl -match '([a-p]{32})') {
        $extId = $Matches[1]
    } else {
        Write-Host $Msg[$Lang].InvalidStoreId -ForegroundColor Red
        Write-Host ""
        Read-Host $Msg[$Lang].EnterToReturn
        return
    }

    Write-Host ($Msg[$Lang].StoreIdFound -f $extId) -ForegroundColor Green
    $downloadUrl = "https://clients2.google.com/service/update2/crx?response=redirect&acceptformat=crx2,crx3&prodversion=110.0.0.0&x=id%3D$extId%26installsource%3Dondemand%26uc"
    $crxPath = Join-Path $ScriptFolder "$extId.crx"
    $extractDir = Join-Path $ScriptFolder $extId

    # Tải tệp CRX
    try {
        Write-Host $Msg[$Lang].DownloadingStore -ForegroundColor Yellow
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 -bor 12288 } catch {}
        Invoke-WebRequest -Uri $downloadUrl -OutFile $crxPath -UseBasicParsing -ErrorAction Stop
        Write-Host $Msg[$Lang].DownloadSuccess -ForegroundColor Green
    } catch {
        Write-Host ($Msg[$Lang].DownloadStoreFail -f $_) -ForegroundColor Red
        Write-Host ""
        Read-Host $Msg[$Lang].EnterToReturn
        return
    }

    # Giải nén tệp CRX
    $zipOffset = 0
    $stream = $null
    $reader = $null
    try {
        $stream = [IO.File]::OpenRead($crxPath)
        $reader = New-Object IO.BinaryReader($stream)
        $magicBytes = $reader.ReadBytes(4)
        $magic = [Text.Encoding]::ASCII.GetString($magicBytes)
        if ($magic -eq "Cr24") {
            $version = $reader.ReadUInt32()
            if ($version -eq 3) {
                $headerLength = $reader.ReadUInt32()
                $zipOffset = 12 + $headerLength
            } elseif ($version -eq 2) {
                $pubKeyLength = $reader.ReadUInt32()
                $sigLength = $reader.ReadUInt32()
                $zipOffset = 16 + $pubKeyLength + $sigLength
            }
        }
    } catch {
        Write-Host ($Msg[$Lang].CRXHeaderFail -f $_) -ForegroundColor Yellow
    } finally {
        if ($null -ne $reader) { $reader.Close() }
        if ($null -ne $stream) { $stream.Close() }
    }

    $tempZip = [System.IO.Path]::GetTempFileName() + ".zip"
    try {
        $allBytes = [System.IO.File]::ReadAllBytes($crxPath)
        if ($zipOffset -gt 0 -and $zipOffset -lt $allBytes.Length) {
            $zipBytes = [byte[]]::new($allBytes.Length - $zipOffset)
            [System.Array]::Copy($allBytes, $zipOffset, $zipBytes, 0, $zipBytes.Length)
            [System.IO.File]::WriteAllBytes($tempZip, $zipBytes)
        } else {
            [System.IO.File]::Copy($crxPath, $tempZip, $true)
        }
        
        Write-Host ($Msg[$Lang].ExtractingTo -f $extractDir) -ForegroundColor Yellow
        if (Test-Path $extractDir) {
            Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        
        # Dùng Expand-Archive hoặc thư viện .NET
        if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) {
            Expand-Archive -Path $tempZip -DestinationPath $extractDir -Force
        } else {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($tempZip, $extractDir)
        }
        Write-Host $Msg[$Lang].ExtractSuccess -ForegroundColor Green
    } catch {
        Write-Host ($Msg[$Lang].ExtractFail -f $_) -ForegroundColor Red
        Write-Host ""
        Read-Host $Msg[$Lang].EnterToReturn
        return
    } finally {
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue }
        if (Test-Path $crxPath) { Remove-Item $crxPath -Force -ErrorAction SilentlyContinue }
    }

    # Xác định đường dẫn thực tế chứa manifest.json
    $extensionPath = $extractDir
    $manifestFile = Get-ChildItem -Path $extractDir -Filter "manifest.json" -Recurse | Select-Object -First 1
    if ($manifestFile) {
        $extensionPath = $manifestFile.Directory.FullName
    }

    # Tự động bật Developer Mode
    Write-Host "`n$($Msg[$Lang].UpdatingPrefs)" -ForegroundColor Yellow

    $userDataDir = "$env:LocalAppData\Google\Chrome\User Data"
    if (Test-Path $userDataDir) {
        $prefFiles = Get-ChildItem -Path $userDataDir -Filter "Preferences" -Recurse -Depth 2 -ErrorAction SilentlyContinue
        foreach ($prefFile in $prefFiles) {
            try {
                $jsonText = Get-Content -LiteralPath $prefFile.FullName -Raw -Encoding UTF8
                $json = ConvertFrom-Json $jsonText
                
                $modified = $false
                if ($null -eq $json.extensions) {
                    $json | Add-Member -MemberType NoteProperty -Name "extensions" -Value ([pscustomobject]@{ ui = ([pscustomobject]@{ developer_mode = $true }) })
                    $modified = $true
                } else {
                    if ($null -eq $json.extensions.ui) {
                        $json.extensions | Add-Member -MemberType NoteProperty -Name "ui" -Value ([pscustomobject]@{ developer_mode = $true })
                        $modified = $true
                    } else {
                        if ($json.extensions.ui.developer_mode -ne $true) {
                            if ($null -eq $json.extensions.ui.developer_mode) {
                                $json.extensions.ui | Add-Member -MemberType NoteProperty -Name "developer_mode" -Value $true -Force
                            } else {
                                $json.extensions.ui.developer_mode = $true
                            }
                            $modified = $true
                        }
                    }
                }
                
                if ($modified) {
                    $newContent = $json | ConvertTo-Json -Depth 100
                    [IO.File]::WriteAllText($prefFile.FullName, $newContent, [System.Text.Encoding]::UTF8)
                    Write-Host ($Msg[$Lang].DevModeEnabled -f $prefFile.Directory.Name) -ForegroundColor Green
                } else {
                    Write-Host ($Msg[$Lang].DevModeAlreadyEnabled -f $prefFile.Directory.Name) -ForegroundColor DarkCyan
                }
            } catch {
                Write-Host ($Msg[$Lang].DevModeFail -f $prefFile.Directory.Name, $_) -ForegroundColor Yellow
            }
        }
    }

    # Copy đường dẫn vào Clipboard
    try {
        if (Get-Command Set-Clipboard -ErrorAction SilentlyContinue) {
            Set-Clipboard -Value $extensionPath
        } else {
            $extensionPath | clip.exe
        }
        Write-Host "`n$($Msg[$Lang].ClipboardSuccess)" -ForegroundColor Green
        Write-Host ($Msg[$Lang].ClipboardPath -f $extensionPath) -ForegroundColor Cyan
    } catch {
        Write-Host "`n$($Msg[$Lang].ClipboardFail -f $_)" -ForegroundColor Yellow
    }

    # Mở Windows Explorer
    Write-Host "`n$($Msg[$Lang].OpeningExplorer)" -ForegroundColor DarkGray
    Start-Process explorer.exe -ArgumentList ('"{0}"' -f $extensionPath)

    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].GuideTitle -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].GuideStep1 -ForegroundColor White
    Write-Host $Msg[$Lang].GuideStep2 -ForegroundColor White
    Write-Host $Msg[$Lang].GuideStep3 -ForegroundColor Yellow
    Write-Host $Msg[$Lang].GuideStep4 -ForegroundColor Yellow
    Write-Host $Msg[$Lang].GuideStep5 -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Cyan

    Write-Host ""
    Read-Host $Msg[$Lang].EnterToReturn
}

# ----------------- OPTION 4: RESTORE CHROME -----------------

function Option-RestoreChrome {
    Write-Host "`n$($Msg[$Lang].RestoreTitle)" -ForegroundColor Yellow
    Write-Host $Msg[$Lang].ClosingChrome -ForegroundColor Yellow
    Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    $chromeDirs = Get-ChromeDirs
    $restoredCount = 0
    foreach ($dir in $chromeDirs) {
        $pathsToCheck = @()
        $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }
        foreach ($sd in $subDirs) {
            $pathsToCheck += $sd.FullName
        }
        $pathsToCheck += $dir
        foreach ($path in ($pathsToCheck | Select-Object -Unique)) {
            $bakPath = Join-Path $path "chrome.dll.BAK"
            $dllPath = Join-Path $path "chrome.dll"
            if (Test-Path -LiteralPath $bakPath) {
                Write-Host ($Msg[$Lang].FoundBackup -f $bakPath) -ForegroundColor Yellow
                try {
                    if (Test-Path -LiteralPath $dllPath) {
                        Remove-Item -LiteralPath $dllPath -Force
                    }
                    Rename-Item -LiteralPath $bakPath -NewName "chrome.dll" -Force
                    Write-Host ($Msg[$Lang].RestoreSuccess -f (Split-Path -Leaf $path)) -ForegroundColor Green
                    $restoredCount++
                } catch [System.UnauthorizedAccessException] {
                    Write-Host ($Msg[$Lang].RestoreAccessDenied -f $dllPath) -ForegroundColor Red
                } catch {
                    Write-Host ($Msg[$Lang].RestoreFail -f $dllPath, $_) -ForegroundColor Red
                }
            }
        }
    }

    if ($restoredCount -eq 0) {
        Write-Host $Msg[$Lang].NoBackupFound -ForegroundColor Yellow
    } else {
        Write-Host "`n$($Msg[$Lang].RestoreAllDone)" -ForegroundColor Green
    }
    
    Write-Host ""
    Read-Host $Msg[$Lang].EnterToReturn
}

# ----------------- MENU LOOP -----------------

do {
    Clear-Host
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].Title -ForegroundColor Cyan
    Write-Host "               OnlyTris_Dev        " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].Opt1 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt2 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt3 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt4 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt5 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt6 -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Cyan
    $choice = Read-Host $Msg[$Lang].Prompt
    
    switch ($choice) {
        "1" { Option-PatchChrome }
        "2" { Option-InstalluBlock }
        "3" { Option-InstallFromStore }
        "4" { Option-RestoreChrome }
        "5" { 
            # Toggle language
            if ($Lang -eq "en") { $Lang = "vi" } else { $Lang = "en" }
        }
        "6" { break }
        default { 
            Write-Host $Msg[$Lang].Invalid -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
} while ($choice -ne "6")
