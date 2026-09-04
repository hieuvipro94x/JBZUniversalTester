using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace JBZUniversalTester.Views;

/// <summary>
/// Sizes and centers one owner-bound native OpenFileDialog in the owner's monitor work area.
/// The operator can move the dialog, but its compact original size remains fixed.
/// </summary>
internal sealed class FixedPositionOpenFileDialogGuard : IDisposable
{
    private const double OriginalDialogWidthDip = 555;
    private const double OriginalDialogHeightDip = 416;
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
    private readonly double _dpiScaleX;
    private readonly double _dpiScaleY;
    private readonly HookProc _hookCallback;
    private IntPtr _hookHandle;

    public FixedPositionOpenFileDialogGuard(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ownerHandle = new WindowInteropHelper(owner).Handle;
        _dispatcher = owner.Dispatcher;
        DpiScale dpi = VisualTreeHelper.GetDpi(owner);
        _dpiScaleX = dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY;
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
            // Windows Shell có thể khôi phục vị trí dialog đã lưu ngay trong lúc
            // activate. Căn ngay để tránh nháy, rồi căn lại một lần sau khi Shell
            // hoàn tất layout. Sau đó không giữ hook/subclass nên title vẫn kéo được.
            ReleaseCreationHook();
            LockDialogSize(wParam);
            CenterInOwnerMonitorWorkArea(wParam);
            _dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () =>
                {
                    if (IsWindow(wParam))
                    {
                        LockDialogSize(wParam);
                        CenterInOwnerMonitorWorkArea(wParam);
                    }
                });
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private bool IsOwnedCommonDialog(IntPtr handle)
    {
        var className = new StringBuilder(64);
        return GetClassName(handle, className, className.Capacity) > 0 &&
               className.ToString().Equals("#32770", StringComparison.Ordinal) &&
               GetWindow(handle, GwOwner) == _ownerHandle;
    }

    private void CenterInOwnerMonitorWorkArea(IntPtr dialogHandle)
    {
        IntPtr monitor = MonitorFromWindow(_ownerHandle, MonitorDefaultToNearest);
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

        int visibleWidth = Math.Min(
            monitorInfo.WorkArea.Width,
            Math.Max(1, (int)Math.Round(OriginalDialogWidthDip * _dpiScaleX)));
        int visibleHeight = Math.Min(
            monitorInfo.WorkArea.Height,
            Math.Max(1, (int)Math.Round(OriginalDialogHeightDip * _dpiScaleY)));

        Rect frameRect = windowRect;
        if (DwmGetWindowAttribute(
                dialogHandle,
                DwmwaExtendedFrameBounds,
                out Rect measuredFrameRect,
                Marshal.SizeOf<Rect>()) == 0)
        {
            frameRect = measuredFrameRect;
        }

        int hiddenFrameWidth = Math.Max(0, windowRect.Width - frameRect.Width);
        int hiddenFrameHeight = Math.Max(0, windowRect.Height - frameRect.Height);
        int width = Math.Min(monitorInfo.WorkArea.Width, visibleWidth + hiddenFrameWidth);
        int height = Math.Min(monitorInfo.WorkArea.Height, visibleHeight + hiddenFrameHeight);
        int x = monitorInfo.WorkArea.Left + ((monitorInfo.WorkArea.Width - visibleWidth) / 2) -
                (frameRect.Left - windowRect.Left);
        int y = monitorInfo.WorkArea.Top + ((monitorInfo.WorkArea.Height - visibleHeight) / 2) -
                (frameRect.Top - windowRect.Top);

        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SwpNoZOrder | SwpNoActivate);
    }

    private static void LockDialogSize(IntPtr dialogHandle)
    {
        int style = GetWindowLong(dialogHandle, GwlStyle);
        int fixedSizeStyle = style & ~WsThickFrame & ~WsMaximizeBox;
        if (fixedSizeStyle == style)
            return;

        SetWindowLong(dialogHandle, GwlStyle, fixedSizeStyle);
        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
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

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int newValue);

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
