using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace JBZUniversalTester.Converters
{
    /// <summary>
    /// Chuyển mã màu dây trong file THT thành Brush hiển thị trên DataGrid.
    /// Hỗ trợ màu đơn: B, W, R, G, L, Y, Br, Or, P, Gr...
    /// và màu kép/sọc: W/B, W-B, R/Y, G+W...
    /// </summary>
    public sealed class WireColorToBrushConverter : IValueConverter
    {
        private static readonly Dictionary<string, Color> ColorMap =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "B", Color.FromRgb(20, 20, 20) },
                { "BK", Color.FromRgb(20, 20, 20) },
                { "BLACK", Color.FromRgb(20, 20, 20) },

                { "W", Colors.White },
                { "WH", Colors.White },
                { "WHITE", Colors.White },

                { "R", Color.FromRgb(211, 47, 47) },
                { "RED", Color.FromRgb(211, 47, 47) },

                { "G", Color.FromRgb(46, 125, 50) },
                { "GN", Color.FromRgb(46, 125, 50) },
                { "GREEN", Color.FromRgb(46, 125, 50) },

                // Quy ước THT: L = Blue.
                { "L", Color.FromRgb(25, 118, 210) },
                { "BL", Color.FromRgb(25, 118, 210) },
                { "BLU", Color.FromRgb(25, 118, 210) },
                { "BLUE", Color.FromRgb(25, 118, 210) },

                { "Y", Color.FromRgb(253, 216, 53) },
                { "YL", Color.FromRgb(253, 216, 53) },
                { "YELLOW", Color.FromRgb(253, 216, 53) },

                { "BR", Color.FromRgb(121, 85, 72) },
                { "BN", Color.FromRgb(121, 85, 72) },
                { "BROWN", Color.FromRgb(121, 85, 72) },

                { "OR", Color.FromRgb(245, 124, 0) },
                { "O", Color.FromRgb(245, 124, 0) },
                { "ORANGE", Color.FromRgb(245, 124, 0) },

                { "P", Color.FromRgb(236, 64, 122) },
                { "PK", Color.FromRgb(236, 64, 122) },
                { "PINK", Color.FromRgb(236, 64, 122) },

                { "GR", Color.FromRgb(158, 158, 158) },
                { "GY", Color.FromRgb(158, 158, 158) },
                { "GRAY", Color.FromRgb(158, 158, 158) },
                { "GREY", Color.FromRgb(158, 158, 158) },

                { "V", Color.FromRgb(123, 31, 162) },
                { "VI", Color.FromRgb(123, 31, 162) },
                { "VIOLET", Color.FromRgb(123, 31, 162) },

                { "LG", Color.FromRgb(139, 195, 74) },
                { "LIGHTGREEN", Color.FromRgb(139, 195, 74) },

                { "SB", Color.FromRgb(129, 212, 250) },
                { "SKYBLUE", Color.FromRgb(129, 212, 250) },

                { "T", Color.FromRgb(210, 180, 140) },
                { "TAN", Color.FromRgb(210, 180, 140) }
            };

        private static readonly Dictionary<string, string> VietnameseColorNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "B", "Đen" }, { "BK", "Đen" }, { "BLACK", "Đen" },
                { "W", "Trắng" }, { "WH", "Trắng" }, { "WHITE", "Trắng" },
                { "R", "Đỏ" }, { "RED", "Đỏ" },
                { "G", "Xanh lá" }, { "GN", "Xanh lá" }, { "GREEN", "Xanh lá" },
                { "L", "Xanh dương" }, { "BL", "Xanh dương" }, { "BLU", "Xanh dương" }, { "BLUE", "Xanh dương" },
                { "Y", "Vàng" }, { "YL", "Vàng" }, { "YELLOW", "Vàng" },
                { "BR", "Nâu" }, { "BN", "Nâu" }, { "BROWN", "Nâu" },
                { "OR", "Cam" }, { "O", "Cam" }, { "ORANGE", "Cam" },
                { "P", "Hồng" }, { "PK", "Hồng" }, { "PINK", "Hồng" },
                { "GR", "Xám" }, { "GY", "Xám" }, { "GRAY", "Xám" }, { "GREY", "Xám" },
                { "V", "Tím" }, { "VI", "Tím" }, { "VIOLET", "Tím" },
                { "LG", "Xanh lá nhạt" }, { "LIGHTGREEN", "Xanh lá nhạt" },
                { "SB", "Xanh da trời" }, { "SKYBLUE", "Xanh da trời" },
                { "T", "Nâu nhạt" }, { "TAN", "Nâu nhạt" }
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ToBrush(value?.ToString());
        }

        /// <summary>
        /// Chuyển mã màu dây thành Brush mà không cần XAML khởi tạo custom converter.
        /// </summary>
        public static Brush ToBrush(string? value)
        {
            string code = value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
                return Brushes.Transparent;

            if (TryGetColor(code, out Color singleColor))
                return CreateSolidBrush(singleColor);

            string[] parts = Regex
                .Split(code, @"[\/+,;|\-\s]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (parts.Length >= 2 &&
                TryGetColor(parts[0], out Color baseColor) &&
                TryGetColor(parts[1], out Color stripeColor))
            {
                return CreateStripedBrush(baseColor, stripeColor);
            }

            return CreateSolidBrush(Color.FromRgb(224, 224, 224));
        }


        /// <summary>
        /// Tên màu tiếng Việt dùng cho thanh trạng thái đầu dò. Với dây sọc,
        /// trả về dạng "Trắng/Đen" theo đúng thứ tự mã trong THT.
        /// </summary>
        public static string ToVietnameseName(string? value)
        {
            string code = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            string[] parts = Regex
                .Split(code, @"[\/+,;|\-\s]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (parts.Length == 0)
                return code;

            string[] names = parts
                .Take(2)
                .Select(part =>
                {
                    string normalized = part.Trim().Replace(".", string.Empty).ToUpperInvariant();
                    return VietnameseColorNames.TryGetValue(normalized, out string? name)
                        ? name
                        : part.Trim();
                })
                .ToArray();

            return string.Join("/", names);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool TryGetColor(string code, out Color color)
        {
            string normalized = code
                .Trim()
                .Replace(".", string.Empty)
                .ToUpperInvariant();

            return ColorMap.TryGetValue(normalized, out color);
        }

        private static SolidColorBrush CreateSolidBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateStripedBrush(Color baseColor, Color stripeColor)
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };

            brush.GradientStops.Add(new GradientStop(baseColor, 0.00));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.62));
            brush.GradientStops.Add(new GradientStop(stripeColor, 0.62));
            brush.GradientStops.Add(new GradientStop(stripeColor, 0.82));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.82));
            brush.GradientStops.Add(new GradientStop(baseColor, 1.00));
            brush.Freeze();

            return brush;
        }
    }
}