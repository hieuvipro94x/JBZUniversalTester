using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace JBZUniversalTester.Views;

/// <summary>
/// Centers the standard Windows file dialog on its WPF owner and keeps the
/// top-level dialog at that position while it is open. If Windows changes the
/// common-dialog implementation and the hook cannot attach, the native dialog
/// remains fully usable; only the position lock is unavailable.
/// </summary>
internal sealed class StandardFileDialogPositionGuard : IDisposable
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int GwlWndProc = -4;
    private const uint GwOwner = 4;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint WmNcDestroy = 0x0082;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly IntPtr _ownerHandle;
    private readonly HookProc _hookProc;
    private readonly WindowProc _windowProc;
    private IntPtr _hookHandle;
    private IntPtr _dialogHandle;
    private IntPtr _previousWindowProc;
    private int _lockedX;
    private int _lockedY;
    private bool _positionLocked;

    public StandardFileDialogPositionGuard(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ownerHandle = new WindowInteropHelper(owner).Handle;
        _hookProc = HookCallback;
        _windowProc = DialogWindowProc;

        if (_ownerHandle != IntPtr.Zero)
        {
            _hookHandle = SetWindowsHookEx(
                WhCbt,
                _hookProc,
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
        CenterOnOwner(dialogHandle);

        IntPtr callback = Marshal.GetFunctionPointerForDelegate(_windowProc);
        _previousWindowProc = SetWindowLongPtr(dialogHandle, GwlWndProc, callback);
        if (_previousWindowProc == IntPtr.Zero)
        {
            _dialogHandle = IntPtr.Zero;
            _positionLocked = false;
        }
    }

    private void CenterOnOwner(IntPtr dialogHandle)
    {
        if (!GetWindowRect(_ownerHandle, out Rect owner) ||
            !GetWindowRect(dialogHandle, out Rect dialog))
        {
            return;
        }

        int width = dialog.Right - dialog.Left;
        int height = dialog.Bottom - dialog.Top;
        int centeredX = owner.Left + ((owner.Right - owner.Left - width) / 2);
        int centeredY = owner.Top + ((owner.Bottom - owner.Top - height) / 2);

        int minX = (int)SystemParameters.VirtualScreenLeft;
        int minY = (int)SystemParameters.VirtualScreenTop;
        int maxX = minX + (int)SystemParameters.VirtualScreenWidth - width;
        int maxY = minY + (int)SystemParameters.VirtualScreenHeight - height;

        _lockedX = Math.Clamp(centeredX, minX, Math.Max(minX, maxX));
        _lockedY = Math.Clamp(centeredY, minY, Math.Max(minY, maxY));
        _positionLocked = true;

        SetWindowPos(
            dialogHandle,
            IntPtr.Zero,
            _lockedX,
            _lockedY,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private IntPtr DialogWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        IntPtr previous = _previousWindowProc;

        if (message == WmWindowPosChanging && _positionLocked && lParam != IntPtr.Zero)
        {
            WindowPosition position = Marshal.PtrToStructure<WindowPosition>(lParam);
            if ((position.Flags & SwpNoMove) == 0)
            {
                position.X = _lockedX;
                position.Y = _lockedY;
                Marshal.StructureToPtr(position, lParam, false);
            }
        }
        else if (message == WmNcDestroy)
        {
            RestoreDialogWindowProc();
        }

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
        _positionLocked = false;
    }

    public void Dispose()
    {
        RestoreDialogWindowProc();
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
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
