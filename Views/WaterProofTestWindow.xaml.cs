using System.ComponentModel;
using System.Windows;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.Views;

public partial class WaterProofTestWindow : Window
{
    private bool _allowClose;

    public WaterProofTestWindow(WaterProofTestViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void CloseOnce()
    {
        if (_allowClose)
            return;
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
