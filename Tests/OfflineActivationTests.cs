using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZ.Licensing;
using JBZUniversalTester.Licensing;
namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
 private static void TestOfflineActivation()
 {
  string root=Path.Combine(Path.GetTempPath(),"JBZ-LicenseTest-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try
  {
   using var signer=ECDsa.Create(ECCurve.NamedCurves.nistP256);string pub=Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());int calls=0;
   var service=new ActivationService(root,()=>{Interlocked.Increment(ref calls);Thread.Sleep(120);return "TEST-WINDOWS|TEST-DISK";},pub);
   Assert(!service.IsActivatedAsync().GetAwaiter().GetResult(),"First use requires activation");
   var requests=Enumerable.Range(0,10).Select(_=>service.GetRequestAsync()).ToArray();Task.WaitAll(requests);
   var request=requests[0].Result;Assert(calls==1&&requests.All(t=>t.Result==request),"Concurrent requests share one worker and installation nonce");
   Assert(LicenseProtocol.DecodeRequest(LicenseProtocol.EncodeRequest(request))==request,"Registration copies losslessly");
   string Sign(RegistrationRequest r){var grant=new LicenseGrant(1,LicenseProtocol.Product,Guid.NewGuid().ToString("N"),r.InstallationId,r.MachineHash,r.ComputerName,"TEST","S1",DateTimeOffset.UtcNow,LicenseProtocol.Developer);string body=LicenseProtocol.Encode(JsonSerializer.SerializeToUtf8Bytes(grant));return "JBZL1."+body+"."+LicenseProtocol.Encode(signer.SignData(Encoding.ASCII.GetBytes("JBZL1."+body),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));}
   string code=Sign(request);service.ActivateAsync(code).GetAwaiter().GetResult();
   Assert(service.IsActivatedAsync().GetAwaiter().GetResult(),"Valid signed activation persists");
   Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"activation.dat"))).Contains("JBZL1"),"Receipt does not store plaintext activation");
   var reopened=new ActivationService(root,()=>"TEST-WINDOWS|TEST-DISK",pub);Assert(reopened.IsActivatedAsync().GetAwaiter().GetResult(),"Version/folder change reuses activation");
   var replacedDisk=new ActivationService(root,()=>"TEST-WINDOWS|REPLACED-DISK",pub);Assert(!replacedDisk.IsActivatedAsync().GetAwaiter().GetResult(),"Disk change requires new activation");
   bool rejected=false;try{service.ActivateAsync(code[..^8]+"AAAAAAAA").GetAwaiter().GetResult();}catch(CryptographicException){rejected=true;}
   Assert(rejected&&service.IsActivatedAsync().GetAwaiter().GetResult(),"Invalid input cannot overwrite valid receipt");
   File.WriteAllText(Path.Combine(root,"installation.dat"),"INVALID-DPAPI");
   var reinstall=new ActivationService(root,()=>"TEST-WINDOWS|TEST-DISK",pub);Assert(!reinstall.IsActivatedAsync().GetAwaiter().GetResult()&&reinstall.GetRequestAsync().Result.InstallationId!=request.InstallationId,"Unrecoverable installation nonce invalidates previous code");
   File.WriteAllBytes(Path.Combine(root,"activation.dat"),new byte[70000]);Assert(!reinstall.IsActivatedAsync().GetAwaiter().GetResult(),"Oversized receipt rejected");
  }
  finally{Directory.Delete(root,true);}
 }
 private static void RenderActivationPreview(string path)
 {
  OnSettingsDispatcher(async()=>
  {
   var window=new ActivationWindow(new ActivationService(Path.Combine(Path.GetTempPath(),"JBZ-Preview-Unused"),()=>"PREVIEW"));
   var content=(FrameworkElement)window.Content;window.Content=null;content.Resources=window.Resources;
   ((Panel)content).Background=new SolidColorBrush(Color.FromRgb(243,246,250));
   System.Windows.Documents.TextElement.SetForeground(content,new SolidColorBrush(Color.FromRgb(22,44,70)));
   System.Windows.Documents.TextElement.SetFontFamily(content,new FontFamily("Segoe UI"));
   ((TextBox)window.FindName("RequestText")).Text=LicenseProtocol.EncodeRequest(new(1,LicenseProtocol.Product,Guid.NewGuid().ToString("N"),new string('A',64),"JBZ-STATION-01"));
   using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Activation preview"){Width=650,Height=630,WindowStyle=unchecked((int)0x80000000)});var canvas=new Border{Background=new SolidColorBrush(Color.FromRgb(243,246,250)),Child=content};source.RootVisual=canvas;
   await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
   canvas.Measure(new Size(650,630));canvas.Arrange(new Rect(0,0,650,630));canvas.UpdateLayout();
   var bitmap=new RenderTargetBitmap(650,630,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
  });
 }
}
