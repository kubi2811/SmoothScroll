# Browser Smooth Scroll

Ứng dụng giúp cuộn chuột mượt mà cho mọi phần mềm trên Windows.  
*A Windows tray app that provides smooth mouse wheel scrolling for any application.*

![App Icon](https://raw.githubusercontent.com/ComingSoon/icon.png)

## ✨ Tính năng chính / Features
- 🚀 **Mượt mà**: Sử dụng thuật toán đà quán tính (physics-based inertia) như trên MacOS/Mobile.
- ⚡ **Hiệu năng cao**: Tần số cập nhật cao (~60Hz - 125Hz) cho trải nghiệm siêu mượt.
- 🛡️ **Tương thích**: Hỗ trợ chặn cụ thể từng ứng dụng (Blacklist) hoặc chỉ chạy trên danh sách cho phép (Allowlist).
- 🖱️ **Tùy chỉnh**: Điều chỉnh tốc độ, độ trễ và độ nảy theo ý thích.
- 🌐 **Tiết kiệm tài nguyên**: Chạy ngầm dưới khay hệ thống (System Tray), rất nhẹ.

---

- 🚀 **Smooth Physics**: Implementing inertia-based scrolling similar to MacOS/Mobile.
- ⚡ **High Performance**: High update rate for ultra-smooth experience on high-refresh displays.
- 🛡️ **Compatibility**: Supports Per-App Blocking (Blacklist) or Allowlist modes.
- 🖱️ **Customizable**: Adjust step size, animation duration, and acceleration.
- 🌐 **Lightweight**: Runs quietly in the System Tray.

## 📥 Cài đặt / Installation

1. Tải về file `.zip` mới nhất tại mục **Releases**.
2. Giải nén và chạy file `BrowserSmoothScroll.exe`.
3. Ứng dụng sẽ xuất hiện ở khay hệ thống (góc dưới bên phải màn hình).
4. **Lưu ý**: Có thể cần cài đặt [.NET Script Runtime](https://dotnet.microsoft.com/download) nếu máy chưa có.

*1. Download the latest `.zip` from **Releases**.*  
*2. Extract and run `BrowserSmoothScroll.exe`.*  
*3. The app will appear in the system tray (bottom right corner).*  
*4. **Note**: You might need to install the .NET Runtime if prompted.*

## ⚙️ Hướng dẫn sử dụng / Usage

1. **Chuột phải** vào icon con chuột ở khay hệ thống (System Tray).
2. Chọn **Settings** để mở bảng cấu hình.
   - ✅ **Enabled**: Bật/Tắt ứng dụng.
   - ✅ **Auto start on login**: Tự khởi động cùng Windows (Khuyên dùng).
   - ✅ **Enable for all apps**: Bật cho tất cả ứng dụng.
   - 🚫 **Block App...**: Nếu một ứng dụng bị lỗi khi cuộn, hãy mở ứng dụng đó lên, sau đó click chuột phải vào icon tray và chọn "Block [Tên App]".

**Mẹo quan trọng**:  
Tắt tính năng "Smooth Scrolling" mặc định của trình duyệt để trải nghiệm tốt nhất (tránh bị cuộn 2 lần).
- Chrome: `chrome://flags/#smooth-scrolling` -> **Disabled**
- Edge: `edge://flags/#smooth-scrolling` -> **Disabled**

---

1. **Right-click** the mouse icon in the System Tray.
2. Select **Settings** to configure.
   - ✅ **Enabled**: Toggle the app on/off.
   - ✅ **Auto start on login**: Start with Windows (Recommended).
   - ✅ **Enable for all apps**: Apply smoothness globally.
   - 🚫 **Block App...**: If an app behaves weirdly, open it, then right-click the tray icon and select "Block [App Name]".

**Important Tip**:  
Disable the browser's native "Smooth Scrolling" flag for the best experience (avoids double-smoothing).
- Chrome: `chrome://flags/#smooth-scrolling` -> **Disabled**
- Edge: `edge://flags/#smooth-scrolling` -> **Disabled**
