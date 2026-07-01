# Chrome Manifest V2 Enabler (Chrome MV2 Patcher)

[Tiếng Việt](#tiếng-viet) | [English](#english)

---

### Demo Video / Video chạy thử

https://github.com/onlytrisdev/chrome-enable-mv2/raw/main/export-1782903909911.mp4

---


<a name="tiếng-viet"></a>
## Tiếng Việt

Dự án này cung cấp công cụ tự động chỉnh sửa (patch) tệp tin `chrome.dll` của trình duyệt Google Chrome nhằm kích hoạt lại hỗ trợ Manifest V2. Việc này giúp bạn tiếp tục cài đặt và sử dụng các tiện ích mở rộng sử dụng Manifest V2 (như uBlock Origin bản thường, thay vì bản Lite) trên các phiên bản Chrome mới (bao gồm Chrome 150+) mà không bị Google chặn.

### Tính năng nổi bật
* Tự động tìm kiếm: Tự động nhận diện đường dẫn cài đặt của Google Chrome trên hệ thống Windows.
* An toàn & Sao lưu: Tự động tạo bản sao lưu (chrome.dll.BAK) trước khi thực hiện bất kỳ chỉnh sửa nào.
* Đơn giản: Chỉ với 1 lần chạy quyền Administrator.
* Dễ dàng khôi phục: Đi kèm công cụ khôi phục lại trạng thái ban đầu chỉ trong vài giây.

### Danh sách tệp tin
* patch-chrome-150.bat: Tập lệnh thực hiện vá (patch) chrome.dll để bật hỗ trợ Manifest V2.
* restore-chrome.bat: Tập lệnh khôi phục chrome.dll nguyên bản từ tệp sao lưu.

### Hướng dẫn sử dụng
1. Tải về hoặc sao chép nội dung tệp patch-chrome-150.bat và restore-chrome.bat.
2. Đóng hoàn toàn trình duyệt Google Chrome (tập lệnh sẽ cố gắng tự động đóng các tiến trình Chrome đang chạy).
3. Nhấp chuột phải vào tệp patch-chrome-150.bat và chọn Run as administrator (Chạy dưới quyền quản trị viên).
4. Đợi chương trình chạy xong (khi màn hình hiện thông báo màu xanh ALL DONE. Please restart Google Chrome!).
5. Khởi động lại Google Chrome và kiểm tra/cài đặt lại tiện ích Manifest V2 của bạn (ví dụ: uBlock Origin).

### Cách khôi phục bản gốc (Restore)
Nếu bạn muốn quay về trạng thái mặc định của Chrome:
1. Đóng hoàn toàn trình duyệt Google Chrome.
2. Nhấp chuột phải vào tệp restore-chrome.bat và chọn Run as administrator.
3. Tập lệnh sẽ tự động tìm các tệp sao lưu .BAK và khôi phục lại bản gốc.

---

<a name="english"></a>
## English

This project provides utility scripts to patch Google Chrome's chrome.dll to re-enable Manifest V2 support. This allows you to continue running legacy MV2 extensions (such as uBlock Origin) on newer Google Chrome versions (including Chrome 150+) where they are disabled or blocked by default.

### Features
* Auto-Detection: Automatically locates the installation paths of Google Chrome on Windows.
* Safety First: Automatically creates a backup copy (chrome.dll.BAK) of the original DLL before patching.
* Effortless: One-click execution with Administrator privileges.
* Easy Restoration: Includes a dedicated script to restore the original DLL in seconds.

### Files
* patch-chrome-150.bat: The script to patch chrome.dll and enable Manifest V2.
* restore-chrome.bat: The script to restore the original backup DLL.

### How to Use
1. Download or copy the contents of patch-chrome-150.bat and restore-chrome.bat.
2. Close all running Google Chrome windows (the script will also attempt to kill chrome processes to prevent file locking).
3. Right-click on patch-chrome-150.bat and select Run as administrator.
4. Wait for the process to complete (you will see the green message ALL DONE. Please restart Google Chrome!).
5. Relaunch Google Chrome and re-install/enable your Manifest V2 extensions (e.g., uBlock Origin).

### How to Restore
To revert changes back to the original:
1. Close Google Chrome.
2. Right-click on restore-chrome.bat and select Run as administrator.
3. The script will automatically restore the original chrome.dll from the .BAK file.

---

### Disclaimer / Tuyên bố miễn trừ trách nhiệm
* Vietnamese: Công cụ này can thiệp vào tệp nhị phân của trình duyệt. Dù tập lệnh đã được thiết kế an toàn và có tính năng tự động sao lưu, hãy tự chịu trách nhiệm đối với mọi rủi ro có thể phát sinh khi sử dụng.
* English: This tool patches the browser's binary files. Although it is designed with safety features (automatic backup), use it at your own risk.
