using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace JBZUniversalTester.Views;

/// <summary>
/// Centers one owner-bound native OpenFileDialog in the owner's monitor work area
/// and prevents only whole-window move operations while that dialog is open.
/// </summary>
internal sealed class FixedPositionOpenFileDialogGuard : IDisposable
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int GwlWndProc = -4;
    private const uint GwOwner = 4;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmSysCommand = 0x0112;
    private const uint WmNcDestroy = 0x0082;
    private const nuint HtCaption = 2;
    private const nuint ScMove = 0xF010;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MfByCommand = 0x00000000;
    private const uint MfGrayed = 0x00000001;

    private readonly IntPtr _ownerHandle;
    private readonly HookProc _hookCallback;
    private readonly WindowProc _dialogWindowProc;
    private IntPtr _hookHandle;
    private IntPtr _dialogHandle;
    private IntPtr _previousWindowProc;

    public FixedPositionOpenFileDialogGuard(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ownerHandle = new WindowInteropHelper(owner).Handle;
        _hookCallback = HookCallback;
        _dialogWindowProc = DialogWindowProc;

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
            _dialogHandle == IntPtr.Zero &&
            IsOwnedCommonDialog(wParam))
        {
            Attach(wParam);
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

    private void Attach(IntPtr dialogHandle)
    {
        _dialogHandle = dialogHandle;
        ReleaseCreationHook();
        CenterInOwnerMonitorWorkArea(dialogHandle);

        IntPtr callback = Marshal.GetFunctionPointerForDelegate(_dialogWindowProc);
        _previousWindowProc = SetWindowLongPtr(dialogHandle, GwlWndProc, callback);
        if (_previousWindowProc == IntPtr.Zero)
        {
            _dialogHandle = IntPtr.Zero;
            return;
        }

        IntPtr systemMenu = GetSystemMenu(dialogHandle, false);
        if (systemMenu != IntPtr.Zero)
        {
            EnableMenuItem(systemMenu, (uint)ScMove, MfByCommand | MfGrayed);
            DrawMenuBar(dialogHandle);
        }
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
            !GetWindowRect(dialogHandle, out Rect dialogRect))
        {
            return;
        }

        int width = dialogRect.Right - dialogRect.Left;
        int height = dialogRect.Bottom - dialogRect.Top;
        int x = monitorInfo.WorkArea.Left + ((monitorInfo.WorkArea.Width - width) / 2);
        int y = monitorInfo.WorkArea.Top + ((monitorInfo.WorkArea.Height - height) / 2);

        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private IntPtr DialogWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        IntPtr previous = _previousWindowProc;

        if (message == WmSysCommand && ((nuint)wParam & 0xFFF0u) == ScMove)
            return IntPtr.Zero;

        if (message == WmNcLButtonDown && (nuint)wParam == HtCaption)
            return IntPtr.Zero;

        if (message == WmNcDestroy)
            RestoreDialogWindowProc();

        return previous == IntPtr.Zero
            ? DefWindowProc(handle, message, wParam, lParam)
            : CallWindowProc(previous, handle, message, wParam, lParam);
    }

    private void RestoreDialogWindowProc()
    {
        if (_dialogHandle != IntPtr.Zero && _previousWindowProc != IntPtr.Zero)
            SetWindowLongPtr(_dialogHandle, GwlWndProc, _previousWindowProc);

        _previousWindowProc = IntPtr.Zero;
        _dialogHandle = IntPtr.Zero;
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
        RestoreDialogWindowProc();
    }

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(handle, index, value)
            : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

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
    private delegate IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

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
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr handle, bool revert);

    [DllImport("user32.dll")]
    private static extern uint EnableMenuItem(IntPtr menu, uint item, uint enable);

    [DllImport("user32.dll")]
    private static extern bool DrawMenuBar(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previous,
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
