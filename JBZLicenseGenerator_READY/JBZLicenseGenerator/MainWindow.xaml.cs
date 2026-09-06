using System.Windows;
using System.Windows.Controls;
using JBZLicenseGenerator.Services;
using System.IO;

namespace JBZLicenseGenerator;

public partial class MainWindow : Window
{
    private readonly LicenseKeyStore _keyStore = new();
    private readonly LicenseGeneratorService _generator;

    public MainWindow()
    {
        InitializeComponent();
        _generator = new LicenseGeneratorService(_keyStore);
        RefreshKeyUi();
        RefreshNormalizedId();
    }

    private void RefreshKeyUi()
    {
        bool ready = _keyStore.HasPrivateKey && _keyStore.HasPublicKey;

        KeyStatusText.Text = ready
            ? "SẴN SÀNG - PRIVATE/PUBLIC KEY đã tồn tại."
            : "CHƯA CÓ KHÓA - hãy khởi tạo một lần trước khi cấp license.";

        KeyFingerprintText.Text =
            $"PUBLIC KEY FINGERPRINT: {_keyStore.GetPublicKeyFingerprint()}";

        PublicKeyTextBox.Text = _keyStore.ReadPublicKeyPem();
        GenerateButton.IsEnabled = ready;
        CreateKeyButton.Content = ready ? "TẠO LẠI KHÓA" : "KHỞI TẠO KHÓA";
    }

    private void CreateKey_Click(object sender, RoutedEventArgs e)
    {
        bool existing = _keyStore.HasPrivateKey || _keyStore.HasPublicKey;

        string warning = existing
            ? "ĐANG CÓ BỘ KHÓA CẤP PHÉP.\n\n" +
              "Nếu tạo lại khóa, tất cả mã/license đã cấp bằng khóa cũ sẽ KHÔNG còn xác minh được " +
              "sau khi ứng dụng chuyển sang public key mới.\n\n" +
              "Chỉ tiếp tục nếu bạn thật sự muốn thay bộ khóa."
            : "Tool sẽ tạo bộ ECDSA P-256 PRIVATE/PUBLIC KEY.\n\n" +
              "PRIVATE KEY phải được sao lưu và chỉ bạn giữ. Không gửi PRIVATE KEY sang máy sản xuất.";

        MessageBoxResult result = MessageBox.Show(
            this,
            warning + "\n\nTiếp tục?",
            "JBZ LICENSE KEY",
            MessageBoxButton.YesNo,
            existing ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            _keyStore.CreateNewKeyPair();
            RefreshKeyUi();
            StatusText.Text =
                "Đã tạo bộ khóa. Hãy mở thư mục khóa và sao lưu file jbz-license-private.pem ở nơi an toàn.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "Không thể tạo khóa",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenKeyFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _keyStore.OpenKeyFolder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không thể mở thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegistrationIdTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshNormalizedId();

    private void RefreshNormalizedId()
    {
        string normalized = RegistrationId.Normalize(RegistrationIdTextBox?.Text);
        NormalizedIdText.Text = string.IsNullOrEmpty(normalized)
            ? string.Empty
            : $"ID chuẩn: {RegistrationId.FormatForDisplay(normalized)}";
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string normalized = RegistrationId.Normalize(RegistrationIdTextBox.Text);
            if (!RegistrationId.IsValid(normalized))
            {
                MessageBox.Show(
                    this,
                    "ID đăng ký phải có từ 8 đến 64 ký tự chữ/số.",
                    "ID không hợp lệ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string activationCode =
                _generator.CreatePerpetualActivationCode(normalized);

            if (!_generator.VerifyActivationCode(
                    activationCode,
                    normalized,
                    out string verifyMessage))
            {
                throw new InvalidOperationException(
                    "Tool vừa tạo một mã nhưng tự xác minh thất bại: " + verifyMessage);
            }

            ActivationCodeTextBox.Text = activationCode;
            StatusText.Text =
                $"Đã tạo license VĨNH VIỄN cho ID {RegistrationId.FormatForDisplay(normalized)}. " +
                "Mã đã được tự xác minh bằng PUBLIC KEY.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "Không thể tạo mã đăng ký",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyActivationCode_Click(object sender, RoutedEventArgs e)
    {
        string code = ActivationCodeTextBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
            return;

        Clipboard.SetText(code);
        StatusText.Text = "Đã copy MÃ ĐĂNG KÝ.";
    }

    private void CopyPublicKey_Click(object sender, RoutedEventArgs e)
    {
        string publicKey = PublicKeyTextBox.Text;
        if (string.IsNullOrWhiteSpace(publicKey))
            return;

        Clipboard.SetText(publicKey);
        StatusText.Text = "Đã copy PUBLIC KEY.";
    }
}
