using System.IO;
using System.Windows;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Views;

public partial class DiscardConfigurationNoticeWindow : Window
{
    public DiscardConfigurationNoticeWindow(ProductModel model)
    {
        if (!model.HasDiscardInterlock) throw new ArgumentException("THT chưa có cặp IO thùng lỗi hợp lệ.", nameof(model));
        InitializeComponent();
        ConfiguredFileText.Text = Path.GetFileName(model.SourcePath);
        ConfiguredIoText.Text = $"IO {model.DiscardContactIo[0]} / {model.DiscardContactIo[1]}";
    }
    private void Continue_Click(object sender, RoutedEventArgs e) => Close();
}
