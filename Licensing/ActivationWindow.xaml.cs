using System.Windows;
using JBZ.Licensing;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Licensing;
public partial class ActivationWindow : Window
{
    private readonly ActivationService _service;
    private bool _closed, _busy;
    public ActivationWindow(ActivationService service)
    {
        InitializeComponent(); _service=service;
        IdentityText.Text=$"{AppVersion.DisplayVersion}  •  Nhà phát triển: {LicenseProtocol.Developer}";
        Loaded+=async (_,_)=>await LoadRequestAsync();
        Closed+=(_,_)=>_closed=true;
    }
    private async Task LoadRequestAsync()
    {
        if(_busy)return; _busy=true; RetryButton.IsEnabled=false; ActivateButton.IsEnabled=false;
        StatusText.Text="Đang đọc thông tin đăng ký…";
        try
        {
            var request=await _service.GetRequestAsync(); if(_closed)return;
            RequestText.Text=LicenseProtocol.EncodeRequest(request); CopyButton.IsEnabled=true;
            ActivateButton.IsEnabled=true; StatusText.Text="";
        }
        catch(Exception ex) {if(!_closed)StatusText.Text="Chưa đọc được thông tin đăng ký. "+ex.Message;}
        finally { _busy=false; if(!_closed)RetryButton.IsEnabled=true; }
    }
    private async void Activate_Click(object sender,RoutedEventArgs e)
    {
        if(_busy)return; _busy=true; ActivateButton.IsEnabled=false; RetryButton.IsEnabled=false;
        string code=CodeText.Text; StatusText.Text="Đang xác minh và lưu kích hoạt…";
        try {await _service.ActivateAsync(code); if(!_closed)DialogResult=true;}
        catch(Exception ex) {if(!_closed)StatusText.Text="Kích hoạt chưa thành công. "+ex.Message;}
        finally {_busy=false;if(!_closed){ActivateButton.IsEnabled=true;RetryButton.IsEnabled=true;}}
    }
    private void Copy_Click(object sender,RoutedEventArgs e)
    {try {Clipboard.SetText(RequestText.Text);StatusText.Text="Đã sao chép mã đăng ký.";}catch(Exception){StatusText.Text="Chưa sao chép được. Hãy chọn mã và nhấn Ctrl+C.";}}
    private async void Retry_Click(object sender,RoutedEventArgs e)=>await LoadRequestAsync();
    private void Exit_Click(object sender,RoutedEventArgs e)=>Close();
}
