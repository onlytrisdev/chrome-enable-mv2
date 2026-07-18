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
# Chrome Manifest V2 Manager for Chrome Beta v151

# ----------------- LOCALIZATION SETTINGS -----------------

$Lang = "vi"  # Default language is Vietnamese

$Msg = @{
    "en" = @{
        Title = "           CHROME v151 MANIFEST V2 MANAGER & EXTENSIONS"
        Opt1 = " [1] Patch Google Chrome Beta v151 (Enable Manifest V2 support)"
        Opt2 = " [2] Download & Extract uBlock Origin MV2 from GitHub"
        Opt3 = " [3] Restore Google Chrome Beta v151 to original state"
        Opt4 = " [4] Switch Language (Tiếng Việt)"
        Opt5 = " [5] Exit"
        Prompt = "Select an option (1-5)"
        Invalid = "Invalid option! Please choose from 1 to 5."
        EnterToReturn = "Press Enter to return to menu..."
        
        PatchTitle = "=== PATCHING GOOGLE CHROME BETA v151 ==="
        ClosingChrome = "Closing all running Chrome processes..."
        NoChromeFound = "Could not find any Google Chrome Beta installation or chrome.dll automatically."
        AllDoneChrome = "ALL DONE. Please restart Google Chrome Beta!"
        
        PatchFoundDll = "Found chrome.dll at: {0}"
        PatchAccessDenied = "[ERROR] Access Denied: You must run this script as Administrator to patch this Chrome installation ({0})."
        PatchOpenError = "[ERROR] Could not open chrome.dll: {0}"
        PatchReading = "`tREADING Chrome.dll..."
        PatchAlreadyPatched = "Already patched {0}"
        PatchBackupFound = "Found an existing backup of the original dll."
        PatchBackupCreating = "Backing up the original dll..."
        PatchingFeature = "Patching {0}..."
        PatchSuccess = "Patch completed successfully for this DLL."
        PatchSkipping = "Skipping {0} (pattern mismatch or unsupported version)"
        
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
        GuideStep1 = "1. Open Google Chrome Beta."
        GuideStep2 = "2. Go to: chrome://extensions"
        GuideStep3 = "3. Enable 'Developer mode' toggle in the top-right corner."
        GuideStep4 = "4. Click the 'Load unpacked' button in the top-left corner."
        GuideStep5 = "5. Paste the copied path by pressing Ctrl + V, then press Enter."
        
        RestoreTitle = "=== RESTORING ORIGINAL CHROME ==="
        FoundBackup = "Found backup file at: {0}"
        RestoreSuccess = "[SUCCESS] Restored original chrome.dll for: {0}"
        RestoreAccessDenied = "[ERROR] Access Denied: You must run this script as Administrator to restore ({0})."
        RestoreFail = "[ERROR] Failed to restore {0}: {1}"
        NoBackupFound = "No backup files (chrome.dll.BAK) were found to restore."
        RestoreAllDone = "ALL DONE. Original Chrome has been restored!"
    }
    "vi" = @{
        Title = "      QUẢN LÝ TIỆN ÍCH MANIFEST V2 CHO CHROME v151"
        Opt1 = " [1] Vá Google Chrome Beta v151 (Kích hoạt lại Manifest V2)"
        Opt2 = " [2] Tải & Cài đặt tự động uBlock Origin MV2 từ GitHub"
        Opt3 = " [3] Khôi phục Google Chrome Beta v151 về nguyên bản"
        Opt4 = " [4] Chuyển đổi ngôn ngữ (English)"
        Opt5 = " [5] Thoát"
        Prompt = "Nhập lựa chọn của bạn (1-5)"
        Invalid = "Lựa chọn không hợp lệ! Vui lòng chọn từ 1 đến 5."
        EnterToReturn = "Nhấn Enter để quay lại menu..."
        
        PatchTitle = "=== ĐANG TIẾN HÀNH VÁ CHROME BETA v151 ==="
        ClosingChrome = "Đang đóng các tiến trình Chrome đang chạy..."
        NoChromeFound = "Không tìm thấy đường dẫn cài đặt Google Chrome Beta hoặc chrome.dll tự động."
        AllDoneChrome = "HOÀN TẤT. Vui lòng khởi động lại Google Chrome Beta!"
        
        PatchFoundDll = "Tìm thấy tệp chrome.dll tại: {0}"
        PatchAccessDenied = "[ERROR] Quyền truy cập bị từ chối: Bạn phải chạy script này với tư cách Quản trị viên để vá Chrome ({0})."
        PatchOpenError = "[ERROR] Không thể mở chrome.dll: {0}"
        PatchReading = "`tĐang đọc file Chrome.dll..."
        PatchAlreadyPatched = "Đã được vá sẵn tính năng: {0}"
        PatchBackupFound = "Tìm thấy tệp sao lưu dll gốc đã có sẵn."
        PatchBackupCreating = "Đang sao lưu tệp dll gốc..."
        PatchingFeature = "Đang vá tính năng: {0}..."
        PatchSuccess = "Đã vá thành công tệp DLL này."
        PatchSkipping = "Bỏ qua {0} (không khớp mẫu nhị phân hoặc phiên bản không hỗ trợ)"
        
        UBlockTitle = "=== TẢI & CÀI ĐẶT UBLOCK ORIGIN MV2 ==="
        CheckingUBlock = "Đang kiểm tra phiên bản uBlock Origin mới nhất trên GitHub..."
        UBlockAPIFail = "Không thể kết nối API GitHub. Đang thử phương pháp dự phòng..."
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
        GuideStep1 = "1. Mở trình duyệt Google Chrome Beta."
        GuideStep2 = "2. Truy cập địa chỉ: chrome://extensions"
        GuideStep3 = "3. Bật 'Chế độ dành cho nhà phát triển' (Developer mode) ở góc trên bên phải."
        GuideStep4 = "4. Nhấp vào nút 'Tải tiện ích đã giải nén' (Load unpacked) ở góc trên bên trái."
        GuideStep5 = "5. Dán đường dẫn đã copy bằng cách nhấn Ctrl + V rồi nhấn Enter."
        
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

    if (-not (Test-Path -LiteralPath $dll)) {
        return
    }

    # Backup the original dll first if not exists
    if (Test-Path -LiteralPath "$dll.BAK") {
        Write-Host $Msg[$Lang].PatchBackupFound -ForegroundColor DarkGray
    } else {
        Write-Host $Msg[$Lang].PatchBackupCreating -ForegroundColor DarkGray
        try {
            Copy-Item -LiteralPath $dll -Destination "$dll.BAK" -Force -ErrorAction Stop
        } catch {
            Write-Host ($Msg[$Lang].PatchAccessDenied -f $dll) -ForegroundColor Red
            return
        }
    }

    # Read bytes
    $bytes = [System.IO.File]::ReadAllBytes($dll)
    $modified = $false

    # Patch 1: UserMayLoad bypass (jg -> jge)
    # Offset: 0x0157BE6A
    # Expected bytes: 7F 78
    if ($bytes[0x0157BE6A] -eq 0x7F -and $bytes[0x0157BE6B] -eq 0x78) {
        Write-Host ($Msg[$Lang].PatchingFeature -f "UserMayLoad Block Policy Bypass") -ForegroundColor Cyan
        $bytes[0x0157BE6A] = 0x7D # jge
        $modified = $true
    } elseif ($bytes[0x0157BE6A] -eq 0x7D -and $bytes[0x0157BE6B] -eq 0x78) {
        Write-Host ($Msg[$Lang].PatchAlreadyPatched -f "UserMayLoad Block Policy Bypass") -ForegroundColor DarkCyan
    } else {
        Write-Host ($Msg[$Lang].PatchSkipping -f "UserMayLoad Block Policy Bypass") -ForegroundColor Yellow
    }

    # Patch 2: Disable reason loop bypass (jnz -> NOP NOP)
    # Offset: 0x0E8798FF
    # Expected bytes: 75 4F
    if ($bytes[0x0E8798FF] -eq 0x75 -and $bytes[0x0E879900] -eq 0x4F) {
        Write-Host ($Msg[$Lang].PatchingFeature -f "Auto-Disable Loop Bypass") -ForegroundColor Cyan
        $bytes[0x0E8798FF] = 0x90 # nop
        $bytes[0x0E879900] = 0x90 # nop
        $modified = $true
    } elseif ($bytes[0x0E8798FF] -eq 0x90 -and $bytes[0x0E879900] -eq 0x90) {
        Write-Host ($Msg[$Lang].PatchAlreadyPatched -f "Auto-Disable Loop Bypass") -ForegroundColor DarkCyan
    } else {
        Write-Host ($Msg[$Lang].PatchSkipping -f "Auto-Disable Loop Bypass") -ForegroundColor Yellow
    }

    # Patch 3: MaybeReEnableExtension bypass (test eax, eax; jz -> NOP NOP jmp near)
    # Offset: 0x0E7F1528
    # Expected bytes: 85 C0 0F 84 8C 00 00 00
    if ($bytes[0x0E7F1528] -eq 0x85 -and $bytes[0x0E7F1529] -eq 0xC0 -and 
        $bytes[0x0E7F152A] -eq 0x0F -and $bytes[0x0E7F152B] -eq 0x84 -and 
        $bytes[0x0E7F152C] -eq 0x8C -and $bytes[0x0E7F152D] -eq 0x00 -and 
        $bytes[0x0E7F152E] -eq 0x00 -and $bytes[0x0E7F152F] -eq 0x00) {
        Write-Host ($Msg[$Lang].PatchingFeature -f "MaybeReEnableExtension Bypass") -ForegroundColor Cyan
        $bytes[0x0E7F1528] = 0x90 # nop
        $bytes[0x0E7F1529] = 0x90 # nop
        $bytes[0x0E7F152A] = 0xE9 # jmp near
        $bytes[0x0E7F152B] = 0x8D
        $bytes[0x0E7F152C] = 0x00;
        $bytes[0x0E7F152D] = 0x00;
        $bytes[0x0E7F152E] = 0x00;
        $bytes[0x0E7F152F] = 0x90 # nop
        $modified = $true
    } elseif ($bytes[0x0E7F152A] -eq 0xE9 -and $bytes[0x0E7F152B] -eq 0x8D) {
        Write-Host ($Msg[$Lang].PatchAlreadyPatched -f "MaybeReEnableExtension Bypass") -ForegroundColor DarkCyan
    } else {
        Write-Host ($Msg[$Lang].PatchSkipping -f "MaybeReEnableExtension Bypass") -ForegroundColor Yellow
    }

    # Patch 4: MaybeReEnableExtension policy check bypass (jg -> jge)
    # Offset: 0x08E7AEFA
    # Expected bytes: 7F 3B
    if ($bytes[0x08E7AEFA] -eq 0x7F -and $bytes[0x08E7AEFB] -eq 0x3B) {
        Write-Host ($Msg[$Lang].PatchingFeature -f "Re-Enable Policy Bypass") -ForegroundColor Cyan
        $bytes[0x08E7AEFA] = 0x7D # jge
        $modified = $true
    } elseif ($bytes[0x08E7AEFA] -eq 0x7D -and $bytes[0x08E7AEFB] -eq 0x3B) {
        Write-Host ($Msg[$Lang].PatchAlreadyPatched -f "Re-Enable Policy Bypass") -ForegroundColor DarkCyan
    } else {
        Write-Host ($Msg[$Lang].PatchSkipping -f "Re-Enable Policy Bypass") -ForegroundColor Yellow
    }

    # Write bytes back
    if ($modified) {
        try {
            [System.IO.File]::WriteAllBytes($dll, $bytes)
            Write-Host $Msg[$Lang].PatchSuccess -ForegroundColor Green
        } catch {
            Write-Host ($Msg[$Lang].PatchAccessDenied -f $dll) -ForegroundColor Red
        }
    } else {
        Write-Host $Msg[$Lang].PatchSuccess -ForegroundColor Green
    }
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
    # Chrome Beta specific paths (We only target Chrome Beta 151)
    $searchDirs += "C:\Program Files\Google\Chrome Beta\Application"
    $searchDirs += "$env:LocalAppData\Google\Chrome Beta\Application"
    
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
        # Filter for Chrome Beta v151.x only
        $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^151\.' }
        foreach ($sd in $subDirs) {
            $pathsToCheck += $sd.FullName
        }
        $pathsToCheck += $dir
        foreach ($path in ($pathsToCheck | Select-Object -Unique)) {
            $dllPath = Join-Path $path "chrome.dll"
            if (Test-Path -LiteralPath $dllPath) {
                # Read the folder's name to ensure we only patch 151
                $parentFolderName = Split-Path -Leaf $path
                if ($parentFolderName -match '^151\.') {
                    $dllsToPatch += $dllPath
                }
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

    $userDataDir = "$env:LocalAppData\Google\Chrome Beta\User Data"
    if (-not (Test-Path $userDataDir)) {
        $userDataDir = "$env:LocalAppData\Google\Chrome\User Data"
    }

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

# ----------------- OPTION 3: RESTORE CHROME -----------------

function Option-RestoreChrome {
    Write-Host "`n$($Msg[$Lang].RestoreTitle)" -ForegroundColor Yellow
    Write-Host $Msg[$Lang].ClosingChrome -ForegroundColor Yellow
    Stop-Process -Name chrome -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    $chromeDirs = Get-ChromeDirs
    $restoredCount = 0
    foreach ($dir in $chromeDirs) {
        $pathsToCheck = @()
        $subDirs = Get-ChildItem -Path $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^151\.' }
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
    Write-Host "               OnlyTris_Dev & AI       " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host $Msg[$Lang].Opt1 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt2 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt3 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt4 -ForegroundColor White
    Write-Host $Msg[$Lang].Opt5 -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Cyan
    $choice = Read-Host $Msg[$Lang].Prompt
    
    switch ($choice) {
        "1" { Option-PatchChrome }
        "2" { Option-InstalluBlock }
        "3" { Option-RestoreChrome }
        "4" { 
            if ($Lang -eq "vi") { $Lang = "en" } else { $Lang = "vi" }
        }
        "5" { break }
        default {
            Write-Host $Msg[$Lang].Invalid -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
} while ($true)
