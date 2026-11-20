using System.Windows.Controls;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class ProductionSettingsPage
{
    private readonly Func<string[]> _installedPrinters;
    private bool _printerTransportReady;
    private bool _useWindowsLabelPrinter;
    private string _lastPrinterCom = string.Empty;
    private string _lastWindowsPrinter = string.Empty;

    private void InitializePrinterTransport()
    {
        _lastPrinterCom = _vm.Settings.Label.PrinterCom ?? string.Empty;
        _lastWindowsPrinter = _vm.Settings.Label.PrinterName ?? string.Empty;
        // Preserve the existing COM-first behavior of old configurations.
        _useWindowsLabelPrinter = string.IsNullOrWhiteSpace(_lastPrinterCom) &&
            !string.IsNullOrWhiteSpace(_lastWindowsPrinter);
        PrinterTransportComboBox.SelectedValue = _useWindowsLabelPrinter ? "WINDOWS" : "COM";
        _printerTransportReady = true;
    }

    private async void PrinterTransportComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_printerTransportReady || IsReleased) return;
        bool windows = PrinterTransportComboBox.SelectedValue as string == "WINDOWS";
        if (windows == _useWindowsLabelPrinter) return;
        _useWindowsLabelPrinter = windows;
        _suppressPrinterPortSelection = true;
        try { PrinterComComboBox.ItemsSource = null; }
        finally { _suppressPrinterPortSelection = false; }
        Interlocked.Increment(ref _printerConnectionGeneration);
        _printerSelectionCts?.Cancel();
        if (windows)
        {
            _lastPrinterCom = _vm.Settings.Label.PrinterCom;
            _vm.Settings.Label.PrinterCom = string.Empty;
            _vm.Settings.Label.PrinterName = _lastWindowsPrinter;
        }
        else
        {
            _lastWindowsPrinter = _vm.Settings.Label.PrinterName;
            _vm.Settings.Label.PrinterName = string.Empty;
            _vm.Settings.Label.PrinterCom = _lastPrinterCom;
        }
        // An explicit transport choice never silently falls back to a raw file.
        _vm.Settings.Label.RawDestination = string.Empty;
        try
        {
            if (_main is not null) await _main.Test.DisconnectLabelPrinterAsync(_pageLifetime.Token);
            if (!IsReleased) await RefreshPortsAsync();
        }
        catch (OperationCanceledException) when (_pageLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Printer transport selection failed: {ex}");
        }
    }
}
