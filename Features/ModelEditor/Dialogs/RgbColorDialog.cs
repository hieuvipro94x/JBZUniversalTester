using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace JBZUniversalTester.ModelEditor.Dialogs;

public static class RgbColorDialog
{
    private static readonly int[] CustomColors = Enumerable.Repeat(0xFFFFFF, 16).ToArray();

    public static Color? Show(Window? owner, Color initialColor)
    {
        var customColors = Marshal.AllocHGlobal(CustomColors.Length * sizeof(int));
        try
        {
            Marshal.Copy(CustomColors, 0, customColors, CustomColors.Length);
            var options = new ChooseColorOptions
            {
                Size = (uint)Marshal.SizeOf<ChooseColorOptions>(),
                Owner = owner is null ? 0 : new WindowInteropHelper(owner).Handle,
                Color = (uint)(initialColor.R | initialColor.G << 8 | initialColor.B << 16),
                CustomColors = customColors,
                Flags = 0x00000001 | 0x00000002 // RGBINIT | FULLOPEN: bảng màu và ô RGB.
            };
            if (!ChooseColor(ref options))
            {
                var error = CommDlgExtendedError();
                if (error != 0) throw new Win32Exception((int)error, "Không mở được bảng chọn màu.");
                return null;
            }
            Marshal.Copy(customColors, CustomColors, 0, CustomColors.Length);
            return System.Windows.Media.Color.FromRgb((byte)options.Color,
                (byte)(options.Color >> 8), (byte)(options.Color >> 16));
        }
        finally { Marshal.FreeHGlobal(customColors); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColorOptions
    {
        public uint Size;
        public nint Owner;
        public nint Instance;
        public uint Color;
        public nint CustomColors;
        public uint Flags;
        public nint CustomData;
        public nint Hook;
        public nint TemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorOptions options);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();
}
