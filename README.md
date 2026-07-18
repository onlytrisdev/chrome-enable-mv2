# Chrome Manifest V2 Enabler (Chrome MV2 Patcher)

[Tiếng Việt](#tiếng-viet) | [English](#english)

---

### Demo Video / Video chạy thử

[How to use (Tiếng Việt / English)](how-to-use.mp4)

---

<a name="tiếng-viet"></a>
## Tiếng Việt

Dự án này cung cấp công cụ tự động chỉnh sửa (patch) tệp tin `chrome.dll` của trình duyệt Google Chrome nhằm kích hoạt lại hỗ trợ Manifest V2. Việc này giúp bạn tiếp tục cài đặt và sử dụng các tiện ích mở rộng sử dụng Manifest V2 (như uBlock Origin bản thường, thay vì bản Lite) trên các phiên bản Chrome mới (bao gồm Chrome 150+) mà không bị Google chặn.

### Tính năng nổi bật
* **Quản lý dạng Menu**: Tích hợp 6 trong 1 (Vá Chrome, Tải/Cài uBlock Origin, Tải Extension bất kỳ từ Store, Khôi phục bản gốc, Chuyển ngôn ngữ) vào một tập lệnh duy nhất.
* **Đa ngôn ngữ**: Mặc định hiển thị tiếng Anh, hỗ trợ chuyển đổi sang tiếng Việt linh hoạt qua menu.
* **Tải & Giải nén mọi Extension từ Chrome Web Store**: Dán link Store hoặc nhập ID tiện ích để tự động tải và giải nén (bóc tách CRX Header tự động).
* **Tự động bật Developer Mode**: Sửa cấu hình Preferences để kích hoạt Chế độ nhà phát triển cho tất cả các Profile Chrome.
* **Sao chép đường dẫn tự động**: Copy đường dẫn thư mục giải nén vào Clipboard giúp cài đặt nhanh bằng 1 phím tắt.
* **Độ tương thích cao**: Hoạt động tốt trên Windows cũ, cả bản cài đặt chính thức lẫn bản Chromium tùy biến/Portable.
* **An sau & Sao lưu**: Tự động tạo bản sao lưu (`chrome.dll.BAK`) trước khi thực hiện bất kỳ chỉnh sửa nào.

### Danh sách tệp tin
* [patch-chrome-150.bat](file:///e:/xampp/htdocs/chrome-enable-mv2/patch-chrome-150.bat): Tập lệnh dành cho Chrome v150 trở xuống.
* [patch-chrome-151.bat](file:///e:/xampp/htdocs/chrome-enable-mv2/patch-chrome-151.bat): Tập lệnh dành cho Chrome Beta v151 (chứa giải pháp vá chính sách và logic nạp MV2 mới).

### Hướng dẫn sử dụng `patch-chrome-151.bat` / `patch-chrome-150.bat`
1. Nhấp chuột phải vào tệp **patch-chrome-151.bat** (hoặc **patch-chrome-150.bat** tùy thuộc phiên bản Chrome của bạn) và chọn **Run as administrator** (Chạy dưới quyền quản trị viên).
2. Lựa chọn ngôn ngữ: Mặc định là tiếng Anh. Nhấn **`5`** và **`Enter`** để chuyển sang tiếng Việt.
3. Trình quản lý hiển thị menu với các lựa chọn:

#### Lựa chọn 1: Vá Google Chrome (Manifest V2 Enabler)
* Nhấn `1` và `Enter`. Tập lệnh tự động vá `chrome.dll` để mở khóa Manifest V2.

#### Lựa chọn 2: Tải & Cài đặt uBlock Origin MV2 từ GitHub
* Nhấn `2` và `Enter`. Tập lệnh sẽ tải bản uBlock Origin MV2 mới nhất từ GitHub releases, tự động giải nén, bật Developer mode cho tất cả các Profile và copy đường dẫn giải nén vào clipboard.

#### Lựa chọn 3: Tải & Giải nén Extension bất kỳ từ Chrome Web Store
* Nhấn `3` và `Enter`. Dán đường dẫn Chrome Web Store của tiện ích mở rộng (hoặc nhập ID của tiện ích gồm 32 ký tự). Tập lệnh sẽ tải tệp `.crx`, bóc tách header của CRX2/CRX3 để giải nén thành mã nguồn và copy đường dẫn vào clipboard.

#### Lựa chọn 4: Khôi phục Google Chrome về nguyên bản
* Nhấn `4` và `Enter`. Tập lệnh khôi phục tệp `chrome.dll` ban đầu từ tệp `.BAK`.

#### Lựa chọn 5: Chuyển đổi ngôn ngữ
* Nhấn `5` và `Enter` để đổi giữa tiếng Anh (English) và tiếng Việt.

---

### Các bước nạp Tiện ích đã giải nén vào Chrome:
1. Mở trình duyệt Google Chrome và truy cập địa chỉ: `chrome://extensions`
2. Bật công tắc **Chế độ dành cho nhà phát triển** (Developer mode) ở góc trên bên phải.
3. Nhấp vào nút **Tải tiện ích đã giải nén** (Load unpacked) ở góc trên bên trái.
4. Hộp thoại chọn thư mục hiện ra, bạn chỉ cần nhấn **`Ctrl + V`** và nhấn **`Enter`** (để dán đường dẫn đã được script copy tự động) -> Tiện ích sẽ được nạp vĩnh viễn!

---

<a name="english"></a>
## English

This project provides a unified interactive tool to patch Google Chrome's `chrome.dll` to re-enable Manifest V2 support, download extensions from the Chrome Web Store or GitHub, and configure them automatically.

### Key Features
* **Menu-Driven**: All-in-one script (Patch, Download/Install uBlock, Download Store CRX, Restore, Language Switcher) inside a single bat file.
* **Multi-Language Support**: English by default, with Vietnamese translation toggle.
* **Download & Unpack Any Extension from Web Store**: Paste a Store link or ID to download the `.crx` file, strip the CRX2/CRX3 header, and extract it as unpacked source code.
* **Auto Developer Mode Toggle**: Configures Chrome's `Preferences` JSON files to enable Developer Mode for all profiles automatically.
* **Auto Clipboard Copy**: Copies the unpacked folder path to your clipboard for quick installation.
* **High Compatibility**: Tested across various Windows and PowerShell versions, supporting both official Chrome and custom Chromium/Portable builds.
* **Safety First**: Automatically creates a backup copy (`chrome.dll.BAK`) before patching.

### Files
* [patch-chrome-150.bat](file:///e:/xampp/htdocs/chrome-enable-mv2/patch-chrome-150.bat): Patch script for Chrome v150 and below.
* [patch-chrome-151.bat](file:///e:/xampp/htdocs/chrome-enable-mv2/patch-chrome-151.bat): Patch script for Chrome Beta v151.

### How to Use `patch-chrome-151.bat` / `patch-chrome-150.bat`
1. Right-click on **patch-chrome-151.bat** (or **patch-chrome-150.bat** depending on your Chrome version) and select **Run as administrator**.
2. Language Switch: The script starts in English by default. Press **`5`** and **`Enter`** to switch to Vietnamese.
3. Select one of the menu options:

#### Option 1: Patch Google Chrome
* Press `1` and `Enter` to patch `chrome.dll` and enable Manifest V2.

#### Option 2: Download & Install uBlock Origin MV2
* Press `2` and `Enter`. The script will download the latest MV2 zip release from GitHub, extract it, enable Developer Mode, and copy the folder path to your clipboard.

#### Option 3: Download & Extract Extension from Chrome Web Store
* Press `3` and `Enter`. Paste the Chrome Web Store extension URL or enter the 32-character ID. The script will download the `.crx` package, strip the binary signature header, extract it, and copy the folder path to your clipboard.

#### Option 4: Restore Google Chrome
* Press `4` and `Enter` to restore the original `chrome.dll` from backup.

#### Option 5: Switch Language
* Press `5` and `Enter` to toggle between English and Vietnamese.

---

### How to Load Unpacked Extensions into Chrome:
1. Open Google Chrome and go to: `chrome://extensions`
2. Enable the **Developer mode** toggle in the top-right corner.
3. Click the **Load unpacked** button in the top-left corner.
4. When the folder picker dialog appears, press **`Ctrl + V`** to paste the path copied by the script, then press **`Enter`** to load the extension.

---

### Disclaimer / Tuyên bố miễn trừ trách nhiệm
* Vietnamese: Công cụ này can thiệp vào tệp nhị phân của trình duyệt. Dù tập lệnh đã được thiết kế an toàn và có tính năng tự động sao lưu, hãy tự chịu trách nhiệm đối với mọi rủi ro có thể phát sinh khi sử dụng.
* English: This tool patches the browser's binary files. Although it is designed with safety features (automatic backup), use it at your own risk.
