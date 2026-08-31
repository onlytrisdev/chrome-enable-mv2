# MV2 RAM Launcher cho Google Chrome

`mv2ctl` mở lại Manifest V2 bằng cách vá các gate đã được xác minh trong RAM của tiến trình Chrome. Công cụ không sửa `chrome.dll` trên đĩa.

Khi Chrome từng lưu lý do vô hiệu hóa MV2 (`8388608`) trong profile, lệnh `launch` sẽ tự gỡ đúng lý do đó trước khi mở trình duyệt, giữ nguyên mọi lý do vô hiệu hóa khác, rồi ký lại `Secure Preferences` bằng cơ chế MAC tương thích với Chrome. Một bản sao lưu một lần được tạo tại `Secure Preferences.mv2ctl.bak`.

Phiên bản `3.2.0` đã được phát triển và kiểm thử end-to-end trên Google Chrome x64 `152.0.7977.65`. Rule dành cho Chrome 151 vẫn được giữ để tương thích với bản cũ; layout lạ sẽ bị dừng an toàn nếu không vượt qua đầy đủ semantic signature riêng của rule.

## Dự án liên quan

Repo này vẫn là launcher Manifest V2 ổn định và có phạm vi riêng. Nếu bạn muốn
thử thêm chặn quảng cáo native theo kiểu EasyList/ABP, xem
[chrome-native-adblock](https://github.com/onlytrisdev/chrome-native-adblock).
Network hook của dự án đó phụ thuộc chính xác vào build Chrome, vì vậy hãy đọc
phần tương thích beta trước khi dùng.

## GUI WinUI 3 (khuyên dùng)

Chạy trực tiếp file self-contained:

```powershell
.\artifacts\win-x64-gui\ChromeMv2Launcher.exe
```

Ứng dụng tự phân tích Chrome khi mở. Khi trạng thái chuyển sang **Sẵn sàng**, đóng mọi cửa sổ Chrome và bấm **Mở Chrome với Manifest V2**. Nút launch tự khóa khi Chrome đang chạy và tự bật lại sau khi Chrome đóng.

GUI hỗ trợ `Tiếng Việt`, `English` và `简体中文`. Lần chạy đầu ứng dụng tự chọn theo ngôn ngữ Windows, sau đó ghi nhớ lựa chọn tại `%LOCALAPPDATA%\ChromeMv2Launcher\language.txt`.

Bản GitHub Release được đóng gói dạng ZIP self-contained. Sau khi giải nén, chạy `ChromeMv2Launcher.exe`; không cần cài .NET hoặc Windows App Runtime riêng.

## CLI

Đóng hoàn toàn Chrome, kể cả tiến trình chạy nền, sau đó chạy:

```powershell
.\artifacts\win-x64-self-contained\mv2ctl.exe analyze
.\artifacts\win-x64-self-contained\mv2ctl.exe launch
```

Chrome phải được mở qua lệnh `launch` ở mỗi phiên mới vì bản vá RAM biến mất khi trình duyệt thoát. Extension MV2 chỉ cần nạp một lần; các lần sau `launch` sẽ giữ extension đã cài và ngăn Chrome thêm lại lý do `unsupported manifest version`.

Nếu Chrome đang chạy, công cụ sẽ từ chối thay vì vô tình mở một cửa sổ trong tiến trình chưa được vá. Nếu phiên bản Chrome mới không khớp duy nhất với toàn bộ semantic signature, công cụ cũng dừng an toàn và không ghi gì.

## Kiểm thử

```powershell
dotnet test .\ManifestV2.slnx -c Release
.\artifacts\win-x64-self-contained\mv2ctl.exe smoke-test
.\artifacts\win-x64-self-contained\mv2ctl.exe functional-ui-test --extension .\test-extension
```

Build trọn bộ artifact phát hành:

```powershell
.\scripts\build-release.ps1
```

`functional-ui-test` tạo profile tạm, dùng luồng **Load unpacked** thật của `chrome://extensions`, chọn extension MV2 mẫu và yêu cầu đúng extension ID xuất hiện ở trạng thái enabled với persistent background page đang chạy. Sau đó test đóng Chrome, mở lại đúng profile qua RAM patcher và xác nhận background page MV2 tự chạy trở lại. Test tự đóng cây tiến trình, xóa profile tạm và khôi phục nội dung clipboard.

## Nạp extension MV2

1. Mở Chrome bằng `mv2ctl.exe launch`.
2. Truy cập `chrome://extensions`.
3. Bật Developer mode.
4. Chọn **Load unpacked** và chọn thư mục extension MV2.

Để thử extension mẫu, chọn thư mục `E:\Project\ManifestV2\test-extension`.

## Lưu ý

- Chỉ hỗ trợ Google Chrome x64 trên Windows.
- Bản build hiện tại được xác nhận trên Chrome `152.0.7977.65`; rule Chrome 151 vẫn được giữ lại.
- Công cụ có thể tiếp tục chạy sau update nếu semantic signatures vẫn khớp duy nhất; nếu không, analyzer sẽ fail-closed và cần bổ sung rule mới.
- Seed MAC của profile được tự tìm trong `resources.pak` bằng cách đối chiếu các MAC hiện có; không hard-code seed hoặc offset theo phiên bản.
- Chỉ entry extension có lý do MV2 mới được sửa. Các lý do disable khác được giữ nguyên.
- `mv2ctl.exe` chưa được ký code-signing nên Windows SmartScreen có thể cảnh báo đối với file tự build.
