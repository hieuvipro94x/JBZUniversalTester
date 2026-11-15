using System.ComponentModel;
using System.Windows;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Views;

public partial class StartupWindow : Window
{
    private bool _completed;

    public StartupWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.DisplayVersion;
        // Keep the design's aspect ratio on small/high-DPI work areas. The
        // Viewbox scales the entire layout instead of squeezing fixed columns.
        double scale = Math.Min(1, Math.Min(
            SystemParameters.WorkArea.Width / Width,
            SystemParameters.WorkArea.Height / Height));
        Width *= scale;
        Height *= scale;
    }

    public void Report(int percent, string stage)
    {
        Dispatcher.VerifyAccess();
        StartupProgressBar.Value = Math.Max(StartupProgressBar.Value, Math.Clamp(percent, 0, 100));
        ProgressText.Text = $"{StartupProgressBar.Value:0}%";
        StageText.Text = stage;
    }

    public void Complete()
    {
        _completed = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // App owns the startup lifetime and closes this window on success/error.
        if (!_completed) e.Cancel = true;
        base.OnClosing(e);
    }
}
