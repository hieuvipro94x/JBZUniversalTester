using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace JBZLicenseIssuer;
public partial class MainWindow:Window
{
 private IssuerStore? _store;
 private bool _busy;
 public MainWindow(){InitializeComponent();Loaded+=async(_,_)=>await Run(async()=>{_store=await Task.Run(IssuerStore.Open);Refresh();});}
 private void Refresh(){HistoryGrid.ItemsSource=_store?.Records.OrderByDescending(r=>r.IssuedAtUtc).ToList();IssueButton.IsEnabled=_store is not null;StatusText.Text=_store is null?"Chưa có bộ cấp phép. Nhập bộ cấp phép của quản trị viên để sử dụng.":$"Đã nạp {_store.Records.Count} hồ sơ. Trạng thái kích hoạt do quản trị viên xác nhận thủ công; tool không nhận tín hiệu từ máy khách.";}
 private async Task Run(Func<Task> action)
 {
  if(_busy)return;_busy=true;IsEnabled=false;
  try{await action();}catch(Exception ex){StatusText.Text="Chưa hoàn tất: "+ex.Message;}finally{_busy=false;IsEnabled=true;}
 }
 private async void Issue_Click(object sender,RoutedEventArgs e)
 {
  string request=RequestBox.Text,customer=CustomerBox.Text,station=StationBox.Text,notes=NotesBox.Text;
  await Run(async()=>{if(_store is null)throw new InvalidOperationException("Hãy nhập bộ cấp phép trước.");var record=await Task.Run(()=>_store.Issue(request,customer,station,notes));Refresh();ActivationBox.Text=record.ActivationCode;StatusText.Text="Đã cấp và lưu mã cho máy "+record.Computer+". Sao chép toàn bộ mã gửi cho người vận hành.";});
 }
 private void Copy_Click(object sender,RoutedEventArgs e){try{if(!string.IsNullOrWhiteSpace(ActivationBox.Text)){Clipboard.SetText(ActivationBox.Text);StatusText.Text="Đã sao chép mã kích hoạt.";}}catch(Exception ex){StatusText.Text="Chưa sao chép được: "+ex.Message;}}
 private void History_SelectionChanged(object sender,SelectionChangedEventArgs e){if(HistoryGrid.SelectedItem is Issuance r){ActivationBox.Text=r.ActivationCode;RequestBox.Text=r.RequestCode;CustomerBox.Text=r.Customer;StationBox.Text=r.Station;NotesBox.Text=r.Notes;}}
 private async void Confirm_Click(object sender,RoutedEventArgs e){if(_store is null||HistoryGrid.SelectedItem is not Issuance r)return;await Run(async()=>{await Task.Run(()=>_store.Confirm(r.LicenseId));Refresh();});}
 private async void Excel_Click(object sender,RoutedEventArgs e)
 {
  if(_store is null)return;var dialog=new SaveFileDialog{Filter="Excel (*.xlsx)|*.xlsx",FileName="Danh-sach-cap-phep-"+DateTime.Today.ToString("yyyyMMdd")+".xlsx"};if(dialog.ShowDialog(this)!=true)return;
  var rows=_store.Records.ToArray();await Run(async()=>{await Task.Run(()=>ExcelExport.Save(dialog.FileName,rows));StatusText.Text="Đã xuất Excel: "+dialog.FileName;});
 }
 private async void Backup_Click(object sender,RoutedEventArgs e)
 {
  if(_store is null){StatusText.Text="Chưa có bộ cấp phép để xuất.";return;}
  var dialog=new SaveFileDialog{Filter="Bộ cấp phép không mật khẩu (*.jbzvault)|*.jbzvault",FileName="JBZ-cap-phep-"+DateTime.Today.ToString("yyyyMMdd")+"-khong-mat-khau.jbzvault"};if(dialog.ShowDialog(this)!=true)return;
  await Run(async()=>{await Task.Run(()=>_store.ExportWorkspace(dialog.FileName));StatusText.Text="Đã xuất bộ cấp phép không mật khẩu. File chứa khóa cấp mã; giữ riêng để nhập trên máy quản trị còn lại.";});
 }
 private async void Import_Click(object sender,RoutedEventArgs e)
 {
  var dialog=new OpenFileDialog{Filter="Bộ cấp phép (*.jbzvault)|*.jbzvault"};if(dialog.ShowDialog(this)!=true)return;
  await Run(async()=>{_store=await Task.Run(()=>IssuerStore.ImportWorkspace(dialog.FileName));Refresh();});
 }
}
