# MV2 RAM Launcher cho Google Chrome

`mv2ctl` mở lại Manifest V2 bằng cách vá các gate đã được xác minh trong RAM của tiến trình Chrome. Công cụ không sửa `chrome.dll` trên đĩa.

Khi Chrome từng lưu lý do vô hiệu hóa MV2 (`8388608`) trong profile, lệnh `launch` sẽ tự gỡ đúng lý do đó trước khi mở trình duyệt, giữ nguyên mọi lý do vô hiệu hóa khác, rồi ký lại `Secure Preferences` bằng cơ chế MAC tương thích với Chrome. Một bản sao lưu một lần được tạo tại `Secure Preferences.mv2ctl.bak`.

Phiên bản `3.7.0` dùng lại phiên Chrome đã vá: mở launcher lần nữa sẽ mở thêm cửa sổ Chrome, không hiện giao diện patcher. Hỗ trợ Google Chrome x64 `155.0.8059.40` và rule dành cho Chrome 151 đến 154 vẫn được giữ; layout lạ sẽ bị dừng an toàn nếu không vượt qua đầy đủ semantic signature riêng của rule.

## GUI WinUI 3 (khuyên dùng)

Chạy trực tiếp file self-contained:

```powershell
.\artifacts\win-x64-gui\ChromeMv2Launcher.exe
```

Ứng dụng tự phân tích Chrome khi mở. Khi trạng thái chuyển sang **Sẵn sàng**, đóng mọi cửa sổ Chrome và bấm **Mở Chrome với Manifest V2**. Nút launch tự khóa khi Chrome đang chạy và tự bật lại sau khi Chrome đóng.

Nút **Tạo lối tắt một chạm** tạo shortcut Desktop tự phân tích rồi mở Chrome với patch RAM. Ứng dụng tự kiểm tra GitHub Release khi mở; nếu có phiên bản mới, nút **Cài bản mới** sẽ tải ZIP GUI, xác minh SHA-256, giải nén vào thư mục phiên bản riêng trong `%LOCALAPPDATA%\ChromeMv2Launcher\versions`, cập nhật shortcut rồi mở ứng dụng mới. Khi Chrome chưa được hỗ trợ, có thể bấm **Mở Chrome bình thường**.

GUI tự đóng ngay khi mở Chrome thành công, dù dùng nút launch, shortcut hay mở Chrome bình thường. Chrome tiếp tục chạy độc lập. Nếu phân tích hoặc launch gặp lỗi, GUI vẫn mở để hiển thị thông báo. Có thể truyền tham số Chrome sau `--`, ví dụ `ChromeMv2Launcher.exe --auto-launch -- --user-data-dir=E:\Chrome-MV2-Profile`.

Khi Chrome đã mở, cả shortcut một chạm lẫn file EXE đều kiểm tra toàn bộ byte patch trong tiến trình browser gốc trước khi hiện GUI. Chỉ phiên đã vá đầy đủ và cùng thư mục dữ liệu mới được dùng lại. Công cụ phân tích đúng `chrome.dll` mà phiên đó đang nạp, kể cả bản cũ còn chạy sau khi Chrome cập nhật trên đĩa. Phiên chưa vá, vá thiếu, không đọc được hoặc layout không hỗ trợ sẽ không được dùng lại hay sửa đổi. URL và tham số profile được chuyển tiếp dù có hoặc không có dấu phân cách `--`. Dùng `ChromeMv2Launcher.exe --show-ui` để mở phần cài đặt/cập nhật; luồng mở thẳng Chrome không chạy kiểm tra cập nhật của GUI.

GUI hỗ trợ `Tiếng Việt`, `English` và `简体中文`. Lần chạy đầu ứng dụng tự chọn theo ngôn ngữ Windows, sau đó ghi nhớ lựa chọn tại `%LOCALAPPDATA%\ChromeMv2Launcher\language.txt`.

Bản GitHub Release được đóng gói dạng ZIP self-contained. Sau khi giải nén, chạy `ChromeMv2Launcher.exe`; không cần cài .NET hoặc Windows App Runtime riêng.

## CLI

Đóng hoàn toàn Chrome, kể cả tiến trình chạy nền, sau đó chạy:

```powershell
.\artifacts\win-x64-self-contained\mv2ctl.exe analyze
.\artifacts\win-x64-self-contained\mv2ctl.exe launch
```

Chrome phải được mở qua lệnh `launch` ở mỗi phiên mới vì bản vá RAM biến mất khi trình duyệt thoát. Extension MV2 chỉ cần nạp một lần; các lần sau `launch` sẽ giữ extension đã cài và ngăn Chrome thêm lại lý do `unsupported manifest version`.

CLI vẫn yêu cầu đóng Chrome trước khi khởi tạo phiên vá mới. GUI có thể mở thêm cửa sổ trong phiên đang chạy nếu xác minh được toàn bộ patch RAM và đúng thư mục dữ liệu; phiên chưa xác minh sẽ bị từ chối. Nếu phiên bản Chrome mới không khớp duy nhất với toàn bộ semantic signature, công cụ cũng dừng an toàn và không ghi gì.

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

Sau khi build gói phát hành, chạy `.\scripts\test-gui-launch.ps1` để kiểm tra shortcut, nút launch, mở Chrome bình thường, dùng lại phiên đã vá mà không hiện GUI, chuyển tiếp URL, mở GUI bằng `--show-ui`, và từ chối phiên chưa vá/khác thư mục dữ liệu. Test dùng profile tạm và xác minh Chrome vẫn chạy sau khi launcher thoát.

## Nạp extension MV2

1. Mở Chrome bằng `mv2ctl.exe launch`.
2. Truy cập `chrome://extensions`.
3. Bật Developer mode.
4. Chọn **Load unpacked** và chọn thư mục extension MV2.

Để thử extension mẫu, chọn thư mục `E:\Project\ManifestV2\test-extension`.

## Lưu ý

- Chỉ hỗ trợ Google Chrome x64 trên Windows.
- Bản build hiện tại được xác nhận trên Chrome `155.0.8059.40`; rule Chrome 151 đến 154 vẫn được giữ lại.
- Công cụ có thể tiếp tục chạy sau update nếu semantic signatures vẫn khớp duy nhất; nếu không, analyzer sẽ fail-closed và cần bổ sung rule mới.
- `mv2ctl probe` dò các vị trí có khả năng liên quan bằng heuristic rộng hơn, chỉ dùng để chẩn đoán; không tự patch từ kết quả probe.
- Seed MAC của profile được tự tìm trong `resources.pak` bằng cách đối chiếu các MAC hiện có; không hard-code seed hoặc offset theo phiên bản.
- Chỉ entry extension có lý do MV2 mới được sửa. Các lý do disable khác được giữ nguyên.
- `mv2ctl.exe` chưa được ký code-signing nên Windows SmartScreen có thể cảnh báo đối với file tự build.
