using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using JBZ.Licensing;
namespace JBZLicenseIssuer;
internal static class IssuerTests
{
 public static void Render(string path)
 {
  var window=new MainWindow();var content=(System.Windows.FrameworkElement)window.Content;window.Content=null;content.Resources=window.Resources;
  ((System.Windows.Controls.DataGrid)window.FindName("HistoryGrid")).ItemsSource=new[]{new Issuance("preview","JBZ-STATION-01","JBZ Việt Nam","Line 01","","","",DateTimeOffset.Now)};
  ((System.Windows.Controls.TextBlock)window.FindName("StatusText")).Text="Danh sách cấp mã được lưu mã hóa trên máy quản trị. Xác nhận kích hoạt được cập nhật thủ công.";
  ((System.Windows.Controls.Panel)content).Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242,245,250));
  System.Windows.Documents.TextElement.SetForeground(content,new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25,49,76)));
  System.Windows.Documents.TextElement.SetFontFamily(content,new System.Windows.Media.FontFamily("Segoe UI"));
  var canvas=new System.Windows.Controls.Border{Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242,245,250)),Child=content};
  using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Issuer preview"){Width=1060,Height=770,WindowStyle=unchecked((int)0x80000000)});source.RootVisual=canvas;
  canvas.Measure(new System.Windows.Size(1060,770));canvas.Arrange(new System.Windows.Rect(0,0,1060,770));canvas.UpdateLayout();
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1060,770,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(canvas);
  var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
 }
 public static void Run(string output)
 {
  using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);string pub=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
  var request=new RegistrationRequest(1,LicenseProtocol.Product,Guid.NewGuid().ToString("N"),new string('A',64),"TEST-PC");
  string req=LicenseProtocol.EncodeRequest(request);
  Check(LicenseProtocol.DecodeRequest(req)==request,"request roundtrip");
  string code=IssuerStore.Sign(request,key,"Test unit","Station 01",out var grant);
  Check(LicenseProtocol.Verify(code,request,pub)==grant,"valid signature");
  Reject(()=>LicenseProtocol.Verify(code,request with{InstallationId=Guid.NewGuid().ToString("N")},pub),"installation binding");
  Reject(()=>LicenseProtocol.Verify(code,request with{MachineHash=new string('B',64)},pub),"disk/windows binding");
  using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);
  Reject(()=>LicenseProtocol.Verify(code,request,Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())),"wrong signing key");
  string[] parts=code.Split('.');byte[] body=LicenseProtocol.Decode(parts[1]);body[10]^=1;
  Reject(()=>LicenseProtocol.Verify(parts[0]+"."+LicenseProtocol.Encode(body)+"."+parts[2],request,pub),"tamper rejection");
  Reject(()=>LicenseProtocol.DecodeRequest(req[..^1]+"X"),"request checksum");
  Reject(()=>LicenseProtocol.Verify(new string('x',9000),request,pub),"bounded input");
  byte[] encrypted=IssuerStore.Encrypt(Encoding.UTF8.GetBytes("private-test-payload"),"test-password-2026");
  Check(Encoding.UTF8.GetString(IssuerStore.Decrypt(encrypted,"test-password-2026"))=="private-test-payload","encrypted workspace roundtrip");
  Reject(()=>IssuerStore.Decrypt(encrypted,"wrong-password-2026"),"incorrect workspace password");
  encrypted[^1]^=1;Reject(()=>IssuerStore.Decrypt(encrypted,"test-password-2026"),"workspace authentication");
  string excel=Path.ChangeExtension(output,".xlsx");
  ExcelExport.Save(excel,new[]{new Issuance(grant.LicenseId,"TEST-PC","=HYPERLINK(\"example\")","Station","한국어 / Tiếng Việt",req,code,grant.IssuedAtUtc)});
  using(var zip=ZipFile.OpenRead(excel))foreach(var entry in zip.Entries){using var stream=entry.Open();XDocument.Load(stream);}
  using(var zip=ZipFile.OpenRead(excel)){using var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var xml=XDocument.Load(stream);Check(!xml.Descendants().Any(e=>e.Name.LocalName=="f"),"Excel strings cannot become formulas");}
  File.WriteAllText(output,"PASS: 13 issuer/protocol/encryption/XLSX checks. No production key or vault used.");
 }
 private static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 private static void Reject(Action action,string name){try{action();}catch(Exception ex)when(ex is CryptographicException or FormatException or System.Text.Json.JsonException){return;}throw new Exception("Accepted "+name);}
}
