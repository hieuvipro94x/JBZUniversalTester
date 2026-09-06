using System.Windows;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

/// <summary>JBZ_REGISTRATION_WINDOW_2026-09-06</summary>
public partial class RegistrationWindow : Window
{
    private readonly string _registrationId;
    private readonly LicenseVerificationService _licenses;

    public RegistrationWindow(string registrationId, LicenseVerificationService licenses)
    {
        InitializeComponent();
        _registrationId = MachineFingerprintService.Normalize(registrationId);
        _licenses = licenses ?? throw new ArgumentNullException(nameof(licenses));
        RegistrationIdTextBox.Text = MachineFingerprintService.FormatForDisplay(_registrationId);
        ActivationCodeTextBox.Focus();
    }

    private void CopyId_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RegistrationIdTextBox.Text);
            StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            StatusText.Text = "ĐÃ SAO CHÉP ID ĐĂNG KÝ";
        }
        catch
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            StatusText.Text = "CHƯA SAO CHÉP ĐƯỢC ID. VUI LÒNG THỬ LẠI.";
        }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        if (!_licenses.ActivateAndSave(ActivationCodeTextBox.Text, _registrationId))
        {
            StatusText.Text = "MÃ ĐĂNG KÝ KHÔNG HỢP LỆ";
            ActivationCodeTextBox.SelectAll();
            ActivationCodeTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
