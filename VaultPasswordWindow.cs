using System.Windows;
using System.Windows.Controls;
namespace JBZLicenseIssuer;
public sealed class VaultPasswordWindow:Window
{
 private readonly PasswordBox _password=new(){Height=38,Padding=new Thickness(8)};
 private readonly PasswordBox _confirm=new(){Height=38,Padding=new Thickness(8)};
 public string Value=>_password.Password;
 public VaultPasswordWindow(bool exporting)
 {
  Title=exporting?"Bảo vệ bộ cấp phép":"Mở bộ cấp phép";Width=470;Height=exporting?300:230;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
  panel.Children.Add(new TextBlock{Text=exporting?"Đặt mật khẩu ít nhất 12 ký tự. Giữ mật khẩu riêng với tệp xuất.":"Nhập mật khẩu bảo vệ bộ cấp phép.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});panel.Children.Add(_password);
  if(exporting){panel.Children.Add(new TextBlock{Text="Nhập lại mật khẩu",Margin=new Thickness(0,10,0,5)});panel.Children.Add(_confirm);}
  var button=new Button{Content="XÁC NHẬN",Height=38,Margin=new Thickness(0,18,0,0),IsDefault=true};panel.Children.Add(button);
  button.Click+=(_,_)=>{if(exporting&&(_password.Password.Length<12||_password.Password!=_confirm.Password)){MessageBox.Show(this,"Mật khẩu cần ít nhất 12 ký tự và nhập lại giống nhau.");return;}DialogResult=true;};
  Loaded+=(_,_)=>_password.Focus();
 }
}
