using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

internal static class SettingsSecurityDialogs
{
    internal static readonly (SettingsPermission Permission, string Name)[] Groups =
    [ (SettingsPermission.Io, "IO / Production"), (SettingsPermission.Relay, "Relay / Bảo trì"),
      (SettingsPermission.Leak, "Test Leak"), (SettingsPermission.Resistance, "Điện trở"),
      (SettingsPermission.Label, "In tem") ];

    private static string OperatorError(Exception error)
    {
        string message = error.Message;
        if (message is "Mật khẩu hiện tại không đúng." or "Hai lần nhập mật khẩu mới không khớp." or
            "Mật khẩu mới phải có từ 1 đến 128 ký tự và không được chỉ chứa khoảng trắng." or
            "Chỉ Admin được đổi mật khẩu." or "Chỉ Admin được phân quyền.")
            return message.Replace("Admin", "quản trị");
        AsyncFileLogService.Current.Error($"Settings access operation failed: {error}");
        return "Không thể cập nhật thông tin truy cập. Vui lòng thử lại.";
    }

    public static SettingsAccessSession? Login(Window owner, SettingsAccessService service)
    {
        var (window, panel, error) = Create(owner, "QUYỀN TRUY CẬP CÀI ĐẶT");
        panel.Children.Add(Label("Nhập mật khẩu để truy cập cài đặt với quyền quản trị."));
        PasswordBox password = Password(panel, "Mật khẩu");
        SettingsAccessSession? result = null;
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        Button admin = Button("ĐĂNG NHẬP", true);
        Button operatorButton = Button("NGƯỜI VẬN HÀNH");
        admin.IsDefault = true;
        actions.Children.Add(admin); actions.Children.Add(operatorButton);
        panel.Children.Add(error); panel.Children.Add(actions);
        admin.Click += async (_, _) =>
        {
            string supplied = password.Password;
            admin.IsEnabled = operatorButton.IsEnabled = false;
            error.Text = "Đang kiểm tra...";
            try
            {
                bool valid = await service.VerifyAdminAsync(supplied);
                if (!window.IsLoaded) return;
                if (!valid) { error.Text = "Mật khẩu không đúng."; password.SelectAll(); password.Focus(); return; }
                result = SettingsAccessSession.Administrator;
                window.DialogResult = true;
            }
            catch (Exception ex) { if (window.IsLoaded) error.Text = OperatorError(ex); }
            finally { admin.IsEnabled = operatorButton.IsEnabled = true; }
        };
        operatorButton.Click += (_, _) => { result = service.OperatorSession; window.DialogResult = true; };
        window.Loaded += (_, _) => password.Focus();
        window.ShowDialog();
        password.Clear();
        return result;
    }

    public static void ChangePassword(Window owner, SettingsAccessService service, SettingsAccessSession session)
    {
        if (!session.IsAdmin) throw new UnauthorizedAccessException("Chỉ Admin được đổi mật khẩu.");
        var (window, panel, error) = Create(owner, "ĐỔI MẬT KHẨU QUẢN TRỊ");
        PasswordBox current = Password(panel, "Mật khẩu hiện tại");
        PasswordBox next = Password(panel, "Mật khẩu mới");
        PasswordBox confirmation = Password(panel, "Nhập lại mật khẩu mới");
        Button save = Button("ĐỔI MẬT KHẨU", true);
        save.IsDefault = true;
        panel.Children.Add(error); panel.Children.Add(save);
        save.Click += async (_, _) =>
        {
            string oldValue = current.Password, newValue = next.Password, repeatedValue = confirmation.Password;
            save.IsEnabled = false;
            try
            {
                await Task.Run(() => service.ChangePassword(session, oldValue, newValue, repeatedValue));
                if (window.IsLoaded) window.DialogResult = true;
            }
            catch (Exception ex) { if (window.IsLoaded) error.Text = OperatorError(ex); }
            finally { save.IsEnabled = true; }
        };
        window.Loaded += (_, _) => current.Focus();
        window.ShowDialog();
        current.Clear(); next.Clear(); confirmation.Clear();
    }

    public static void Permissions(Window owner, SettingsAccessService service, SettingsAccessSession session)
    {
        if (!session.IsAdmin) throw new UnauthorizedAccessException("Chỉ Admin được phân quyền.");
        var (window, panel, error) = Create(owner, "PHÂN QUYỀN NGƯỜI VẬN HÀNH");
        panel.Children.Add(Label("Chọn nhóm được phép chỉnh và thao tác. Nhóm bỏ chọn chỉ được xem.\nQuản trị luôn có toàn quyền."));
        SettingsAccessSession current = service.OperatorSession;
        var boxes = Groups.Select(group => (group.Permission, Box: new CheckBox
        {
            Content = group.Name, IsChecked = current.Allows(group.Permission),
            FontSize = 16, Margin = new Thickness(0, 9, 0, 5)
        })).ToArray();
        foreach (var box in boxes) panel.Children.Add(box.Box);
        Button save = Button("LƯU PHÂN QUYỀN", true);
        panel.Children.Add(error); panel.Children.Add(save);
        save.Click += async (_, _) =>
        {
            SettingsPermission permissions = boxes.Where(box => box.Box.IsChecked == true)
                .Aggregate(SettingsPermission.None, (value, box) => value | box.Permission);
            save.IsEnabled = false;
            try
            {
                await Task.Run(() => service.SetOperatorPermissions(session, permissions));
                if (window.IsLoaded) window.DialogResult = true;
            }
            catch (Exception ex) { if (window.IsLoaded) error.Text = OperatorError(ex); }
            finally { save.IsEnabled = true; }
        };
        window.ShowDialog();
    }

    private static (Window Window, StackPanel Panel, TextBlock Error) Create(Window owner, string title)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(35, 68, 155)), Margin = new Thickness(0, 0, 0, 12) });
        var window = new Window { Title = title, Owner = owner, Width = 490, SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.Close(); };
        return (window, panel, new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 4) });
    }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        FontSize = 14, Margin = new Thickness(0, 6, 0, 5), Foreground = Brushes.DarkSlateGray };
    private static PasswordBox Password(StackPanel panel, string label)
    {
        panel.Children.Add(Label(label));
        var box = new PasswordBox { PasswordChar = '●', Height = 36, FontSize = 18, Padding = new Thickness(7, 3, 7, 3), MaxLength = 128 };
        panel.Children.Add(box);
        return box;
    }
    private static Button Button(string label, bool primary = false) => new() { Content = label,
        Height = 38, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 8, 8, 0),
        FontSize = 13, FontWeight = FontWeights.SemiBold,
        Background = primary ? new SolidColorBrush(Color.FromRgb(15, 118, 110)) : Brushes.WhiteSmoke,
        Foreground = primary ? Brushes.White : Brushes.DarkSlateGray };
}
