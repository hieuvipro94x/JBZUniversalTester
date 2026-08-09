# Chuyển giao diện `gui.py` sang WPF

`Views/MainWindow.xaml` hiện chứa trực tiếp hai `DataTemplate` cho:

- `HomeViewModel`: tương ứng `MainMenuWindow` trong Tkinter.
- `TestViewModel`: tương ứng giao diện `MainWindow.build_ui()` trong Tkinter.

Các ViewModel, dịch vụ FTDI D2XX, Keysight VISA, parser `.tht` và TestEngine không bị thay đổi.

## Phím tắt

- `F5`: bắt đầu kiểm tra / bắt đầu quét.
- `Esc`: từ màn hình test quay về menu.
- `F11`: chuyển trạng thái cửa sổ Maximized/Normal để bảo trì.

Hai file `HomeView.xaml` và `TestView.xaml` cũ vẫn được giữ lại để tham khảo nhưng không còn được nạp bởi `MainWindow.xaml`.
