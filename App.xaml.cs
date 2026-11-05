using System.IO;
using System.Windows;
namespace JBZLicenseIssuer;
public partial class App:Application
{
 private Mutex? _mutex;
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  _mutex=new Mutex(true,@"Local\JBZLicenseIssuer",out bool owner);
  if(!owner){MessageBox.Show("Tool cấp key đang chạy.");Shutdown();return;}
  if(e.Args is ["--self-test",var output])
  {
   try{IssuerTests.Run(output);Shutdown(0);}catch(Exception ex){File.WriteAllText(output,ex.ToString());Shutdown(1);}return;
  }
  if(e.Args is ["--preview",var image]){IssuerTests.Render(image);Shutdown(0);return;}
  MainWindow=new MainWindow();ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();
 }
 protected override void OnExit(ExitEventArgs e){_mutex?.Dispose();base.OnExit(e);}
}
