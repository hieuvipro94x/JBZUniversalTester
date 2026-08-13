using System;
using System.Collections.Concurrent;
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
                { "B", Color.FromRgb(0x10, 0x10, 0x10) },
                { "BK", Color.FromRgb(0x10, 0x10, 0x10) },
                { "BLACK", Color.FromRgb(0x10, 0x10, 0x10) },

                { "W", Colors.White },
                { "WH", Colors.White },
                { "WHITE", Colors.White },

                { "R", Color.FromRgb(0xED, 0x00, 0x00) },
                { "RED", Color.FromRgb(0xED, 0x00, 0x00) },

                { "G", Color.FromRgb(0x00, 0xD0, 0x00) },
                { "GN", Color.FromRgb(0x00, 0xD0, 0x00) },
                { "GREEN", Color.FromRgb(0x00, 0xD0, 0x00) },

                // Quy ước THT: L = Blue.
                { "L", Color.FromRgb(0x00, 0x77, 0xFF) },
                { "BL", Color.FromRgb(0x00, 0x77, 0xFF) },
                { "BLU", Color.FromRgb(0x00, 0x77, 0xFF) },
                { "BLUE", Color.FromRgb(0x00, 0x77, 0xFF) },

                { "Y", Color.FromRgb(0xFF, 0xFF, 0x00) },
                { "YL", Color.FromRgb(0xFF, 0xFF, 0x00) },
                { "YELLOW", Color.FromRgb(0xFF, 0xFF, 0x00) },

                { "BR", Color.FromRgb(0x8A, 0x43, 0x00) },
                { "BN", Color.FromRgb(0x8A, 0x43, 0x00) },
                { "BROWN", Color.FromRgb(0x8A, 0x43, 0x00) },

                { "OR", Color.FromRgb(0xFF, 0x99, 0x00) },
                { "O", Color.FromRgb(0xFF, 0x99, 0x00) },
                { "ORANGE", Color.FromRgb(0xFF, 0x99, 0x00) },

                { "P", Color.FromRgb(0xFF, 0x2C, 0xA5) },
                { "PK", Color.FromRgb(0xFF, 0x2C, 0xA5) },
                { "PINK", Color.FromRgb(0xFF, 0x2C, 0xA5) },

                { "GR", Color.FromRgb(0x80, 0x80, 0x80) },
                { "GY", Color.FromRgb(0x80, 0x80, 0x80) },
                { "GRAY", Color.FromRgb(0x80, 0x80, 0x80) },
                { "GREY", Color.FromRgb(0x80, 0x80, 0x80) },

                { "V", Color.FromRgb(0x7F, 0x00, 0xBB) },
                { "VI", Color.FromRgb(0x7F, 0x00, 0xBB) },
                { "VIOLET", Color.FromRgb(0x7F, 0x00, 0xBB) },

                { "LG", Color.FromRgb(0x77, 0xDD, 0x77) },
                { "LIGHTGREEN", Color.FromRgb(0x77, 0xDD, 0x77) },

                { "SB", Color.FromRgb(0x66, 0xCC, 0xFF) },
                { "SKY", Color.FromRgb(0x66, 0xCC, 0xFF) },
                { "SKYBLUE", Color.FromRgb(0x66, 0xCC, 0xFF) },

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

        private static readonly ConcurrentDictionary<string, Brush> BrushCache =
            new(StringComparer.OrdinalIgnoreCase);

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
            string cacheKey = string.IsNullOrWhiteSpace(code)
                ? "<empty>"
                : code.ToUpperInvariant();

            return BrushCache.GetOrAdd(cacheKey, _ => CreateBrush(code));
        }

        private static Brush CreateBrush(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Brushes.Transparent;

            if (TryGetColor(code, out Color singleColor))
                return CreateSolidBrush(singleColor);

            string[] parts = Regex
                .Split(code, @"[\/+,;|\-\s]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (parts.Length >= 1 && TryGetColor(parts[0], out Color baseColor))
                return CreateSolidBrush(baseColor);

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

        public static IReadOnlyList<string> Tokenize(string? value)
        {
            string code = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
                return Array.Empty<string>();

            return Regex
                .Split(code, @"[\/+,;|\-\s]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().Replace(".", string.Empty))
                .Take(4)
                .ToArray();
        }

        public static Brush ToTokenBrush(string? value, int index)
        {
            IReadOnlyList<string> tokens = Tokenize(value);
            if (index < 0 || index >= tokens.Count)
                return ToBrush(string.Empty);

            return TryGetColor(tokens[index], out Color color)
                ? ToBrush(tokens[index])
                : ToBrush(string.Empty);
        }

        public static string ToDisplayCode(string? value) => (value ?? string.Empty).Trim();

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
