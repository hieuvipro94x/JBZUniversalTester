using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace JBZUniversalTester.Views;

/// <summary>
/// Sizes and centers one owner-bound native OpenFileDialog in the owner's monitor work area.
///
/// Behavior:
/// - User cannot resize or maximize the dialog.
/// - User can still move the dialog by dragging the title bar.
/// - Preferred fixed size is 555 x 416 DIP.
/// - On a smaller monitor / larger Windows DPI scaling, only the dimension that
///   does not fit is reduced.
/// - IMPORTANT: Windows is allowed to re-layout the native dialog controls first.
///   The resize/maximize styles are removed only AFTER the final size has been applied.
/// </summary>
internal sealed class FixedPositionOpenFileDialogGuard : IDisposable
{
    private const double OriginalDialogWidthDip = 555;
    private const double OriginalDialogHeightDip = 416;
    private const double SafeMarginDip = 8;

    private const int WhCbt = 5;
    private const int HcbtActivate = 5;

    private const int GwlStyle = -16;
    private const int DwmwaExtendedFrameBounds = 9;

    private const uint GwOwner = 4;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private const int WsThickFrame = 0x00040000;
    private const int WsMaximizeBox = 0x00010000;

    private readonly IntPtr _ownerHandle;
    private readonly Dispatcher _dispatcher;
    private readonly double _ownerDpiScaleX;
    private readonly double _ownerDpiScaleY;
    private readonly HookProc _hookCallback;

    private IntPtr _hookHandle;

    public FixedPositionOpenFileDialogGuard(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _ownerHandle = new WindowInteropHelper(owner).Handle;
        _dispatcher = owner.Dispatcher;

        DpiScale dpi = VisualTreeHelper.GetDpi(owner);
        _ownerDpiScaleX = dpi.DpiScaleX;
        _ownerDpiScaleY = dpi.DpiScaleY;

        _hookCallback = HookCallback;

        if (_ownerHandle != IntPtr.Zero)
        {
            _hookHandle = SetWindowsHookEx(
                WhCbt,
                _hookCallback,
                IntPtr.Zero,
                GetCurrentThreadId());
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcbtActivate &&
            IsOwnedCommonDialog(wParam))
        {
            ReleaseCreationHook();

            // QUAN TRỌNG:
            // Chưa khóa resize tại đây.
            //
            // Để WS_THICKFRAME tồn tại trong lúc SetWindowPos thay đổi kích thước,
            // Windows Common Dialog sẽ nhận WM_SIZE và tự re-layout:
            // - danh sách file
            // - ô tên file
            // - nút Mở
            // - nút Hủy
            // - các control phía dưới/phía phải.
            FitAndCenterWhileResizable(wParam);

            // Shell thường còn restore/layout thêm một lần ngay sau Activate.
            // Chờ đến ApplicationIdle, áp kích thước lần cuối rồi mới khóa.
            _dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () =>
                {
                    if (!IsWindow(wParam))
                        return;

                    // Lần cuối cùng cho Windows re-layout ở trạng thái resizable.
                    FitAndCenterWhileResizable(wParam);

                    // Bây giờ layout đã đúng -> khóa kích thước.
                    LockDialogSize(wParam);

                    // Việc bỏ WS_THICKFRAME làm thay đổi non-client frame một vài pixel.
                    // Chỉ căn giữa lại, TUYỆT ĐỐI không đổi size sau khi đã khóa.
                    CenterCurrentSizeInOwnerMonitorWorkArea(wParam);
                });
        }

        return CallNextHookEx(
            _hookHandle,
            code,
            wParam,
            lParam);
    }

    private bool IsOwnedCommonDialog(IntPtr handle)
    {
        var className = new StringBuilder(64);

        return GetClassName(
                   handle,
                   className,
                   className.Capacity) > 0 &&
               className.ToString().Equals(
                   "#32770",
                   StringComparison.Ordinal) &&
               GetWindow(handle, GwOwner) == _ownerHandle;
    }

    /// <summary>
    /// Apply the preferred fixed dialog size while the dialog is still resizable.
    /// This is intentional: native Common Dialog uses WM_SIZE to reposition
    /// Open/Cancel and the other child controls.
    /// </summary>
    private void FitAndCenterWhileResizable(IntPtr dialogHandle)
    {
        IntPtr monitor = MonitorFromWindow(
            _ownerHandle,
            MonitorDefaultToNearest);

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo) ||
            !GetWindowRect(dialogHandle, out Rect windowRect))
        {
            return;
        }

        GetDialogDpiScale(
            dialogHandle,
            out double dpiScaleX,
            out double dpiScaleY);

        Rect frameRect = GetVisibleFrameRect(
            dialogHandle,
            windowRect);

        int hiddenFrameLeft =
            Math.Max(0, frameRect.Left - windowRect.Left);

        int hiddenFrameTop =
            Math.Max(0, frameRect.Top - windowRect.Top);

        int hiddenFrameRight =
            Math.Max(0, windowRect.Right - frameRect.Right);

        int hiddenFrameBottom =
            Math.Max(0, windowRect.Bottom - frameRect.Bottom);

        int preferredVisibleWidth = Math.Max(
            1,
            (int)Math.Round(
                OriginalDialogWidthDip * dpiScaleX));

        int preferredVisibleHeight = Math.Max(
            1,
            (int)Math.Round(
                OriginalDialogHeightDip * dpiScaleY));

        int marginX = Math.Max(
            0,
            (int)Math.Round(
                SafeMarginDip * dpiScaleX));

        int marginY = Math.Max(
            0,
            (int)Math.Round(
                SafeMarginDip * dpiScaleY));

        int maxVisibleWidth = Math.Max(
            1,
            monitorInfo.WorkArea.Width - (marginX * 2));

        int maxVisibleHeight = Math.Max(
            1,
            monitorInfo.WorkArea.Height - (marginY * 2));

        // KHÔNG scale đồng thời Width + Height.
        //
        // Ví dụ:
        // màn hình chỉ thiếu chiều cao -> chỉ giảm Height.
        // Width vẫn giữ nguyên để các label/nút không bị ép ngang.
        //
        // Dialog không bao giờ lớn hơn 555 x 416 DIP,
        // nhưng nếu màn hình nhỏ thì mỗi chiều tự giới hạn theo WorkArea.
        int visibleWidth = Math.Min(
            preferredVisibleWidth,
            maxVisibleWidth);

        int visibleHeight = Math.Min(
            preferredVisibleHeight,
            maxVisibleHeight);

        int outerWidth =
            visibleWidth +
            hiddenFrameLeft +
            hiddenFrameRight;

        int outerHeight =
            visibleHeight +
            hiddenFrameTop +
            hiddenFrameBottom;

        outerWidth = Math.Min(
            monitorInfo.WorkArea.Width,
            Math.Max(1, outerWidth));

        outerHeight = Math.Min(
            monitorInfo.WorkArea.Height,
            Math.Max(1, outerHeight));

        int visibleX =
            monitorInfo.WorkArea.Left +
            ((monitorInfo.WorkArea.Width - visibleWidth) / 2);

        int visibleY =
            monitorInfo.WorkArea.Top +
            ((monitorInfo.WorkArea.Height - visibleHeight) / 2);

        int x = visibleX - hiddenFrameLeft;
        int y = visibleY - hiddenFrameTop;

        // Vì WS_THICKFRAME vẫn còn ở thời điểm này,
        // native dialog có cơ hội xử lý WM_SIZE và di chuyển các child controls.
        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            x,
            y,
            outerWidth,
            outerHeight,
            SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>
    /// After the layout is stable, remove resizing/maximize functionality.
    /// </summary>
    private static void LockDialogSize(IntPtr dialogHandle)
    {
        int style = GetWindowLong(
            dialogHandle,
            GwlStyle);

        int fixedSizeStyle =
            style &
            ~WsThickFrame &
            ~WsMaximizeBox;

        if (fixedSizeStyle == style)
            return;

        SetWindowLong(
            dialogHandle,
            GwlStyle,
            fixedSizeStyle);

        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
    }

    /// <summary>
    /// Re-center the existing size without resizing it.
    /// Used only after WS_THICKFRAME has been removed.
    /// </summary>
    private void CenterCurrentSizeInOwnerMonitorWorkArea(
        IntPtr dialogHandle)
    {
        IntPtr monitor = MonitorFromWindow(
            _ownerHandle,
            MonitorDefaultToNearest);

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo) ||
            !GetWindowRect(dialogHandle, out Rect windowRect))
        {
            return;
        }

        Rect frameRect = GetVisibleFrameRect(
            dialogHandle,
            windowRect);

        int visibleWidth = Math.Max(
            1,
            frameRect.Width);

        int visibleHeight = Math.Max(
            1,
            frameRect.Height);

        int x =
            monitorInfo.WorkArea.Left +
            ((monitorInfo.WorkArea.Width - visibleWidth) / 2) -
            (frameRect.Left - windowRect.Left);

        int y =
            monitorInfo.WorkArea.Top +
            ((monitorInfo.WorkArea.Height - visibleHeight) / 2) -
            (frameRect.Top - windowRect.Top);

        // Không thay đổi size sau khi đã LockDialogSize.
        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            SwpNoSize |
            SwpNoZOrder |
            SwpNoActivate);
    }

    private void GetDialogDpiScale(
        IntPtr dialogHandle,
        out double dpiScaleX,
        out double dpiScaleY)
    {
        uint dpi = GetDpiForWindow(dialogHandle);

        if (dpi > 0)
        {
            // Common Dialog dùng DPI vuông trên Windows hiện đại.
            dpiScaleX = dpi / 96.0;
            dpiScaleY = dpi / 96.0;
            return;
        }

        dpiScaleX = _ownerDpiScaleX;
        dpiScaleY = _ownerDpiScaleY;
    }

    private static Rect GetVisibleFrameRect(
        IntPtr dialogHandle,
        Rect windowRect)
    {
        if (DwmGetWindowAttribute(
                dialogHandle,
                DwmwaExtendedFrameBounds,
                out Rect measuredFrameRect,
                Marshal.SizeOf<Rect>()) == 0)
        {
            return measuredFrameRect;
        }

        return windowRect;
    }

    private void ReleaseCreationHook()
    {
        if (_hookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseCreationHook();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;
    }

    private delegate IntPtr HookProc(
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hook,
        HookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr handle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(
        IntPtr handle,
        uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(
        IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr handle,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr handle,
        out Rect rect);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern int GetWindowLong(
        IntPtr handle,
        int index);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern int SetWindowLong(
        IntPtr handle,
        int index,
        int newValue);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(
        IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr handle,
        int attribute,
        out Rect value,
        int valueSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
