using System.IO;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;
public partial class ProductionSettingsPage
{
    private Task _labelPartsLoadTask = Task.CompletedTask;
    private int _labelPartsGeneration;
    private bool _labelPartsLoading;
    private Task _bcwSummaryTask = Task.CompletedTask;
    private sealed record LabelPartChoice(int Index, string Signature, string Display);
    private static string LabelPartSignature(ProductModel m) =>
        string.Join("\u001f", m.PartNumber, m.ProductName, m.Eco, m.Nco, m.Alc);

    private async Task LoadLabelPrintPartsAsync(string path)
    {
        int generation = ++_labelPartsGeneration;
        _labelPartsLoading = true;
        string? previous = (LabelPrintPartComboBox.SelectedItem as LabelPartChoice)?.Signature;
        LabelPrintPartComboBox.ItemsSource = null;
        LabelPrintPartComboBox.IsEnabled = false;
        try
        {
            var choices = await Task.Run(() => new ThtModelParser().LoadAll(path)
                .Select((m, index) => new LabelPartChoice(index, LabelPartSignature(m),
                    $"{m.PartNumber}  •  ECO: {m.Eco}  •  ALC: {m.Alc}  •  {m.ProductName}")).ToArray(), _pageLifetime.Token);
            if (IsReleased || generation != _labelPartsGeneration) return;
            LabelPrintPartComboBox.ItemsSource = choices;
            LabelPrintPartComboBox.SelectedItem = choices.FirstOrDefault(c => c.Signature == previous) ??
                choices.FirstOrDefault(c => c.Display.StartsWith((_main?.Test.PartNumber ?? "") + "  •", StringComparison.Ordinal)) ?? choices.FirstOrDefault();
            LabelPrintPartComboBox.IsEnabled = Volatile.Read(ref _batchPrintInProgress) == 0;
        }
        catch (OperationCanceledException) when (_pageLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Read label THT part rows failed: {ex}");
            if (!IsReleased && generation == _labelPartsGeneration) LabelPrintPartComboBox.ToolTip = ex.Message;
        }
        finally
        {
            if (generation == _labelPartsGeneration)
            {
                _labelPartsLoading = false;
                if (!IsReleased) UpdateBcwPanel();
            }
        }
    }

    private void LabelPrintPartComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LabelPrintPartComboBox.SelectedItem is LabelPartChoice choice)
            LabelPrintPartComboBox.ToolTip = choice.Display;
        if (!_labelPartsLoading) UpdateBcwPanel();
    }

    private static ProductModel LoadSelectedLabelModel(string path, LabelPartChoice? choice)
    {
        var models = new ThtModelParser().LoadAll(path);
        if (choice is null)
        {
            if (models.Count == 1) return models[0];
            throw new InvalidDataException("Chọn dòng dữ liệu sản phẩm trong file THT trước khi in tem.");
        }
        if (choice.Index < 0 || choice.Index >= models.Count || LabelPartSignature(models[choice.Index]) != choice.Signature)
            throw new InvalidDataException("Dữ liệu THT đã thay đổi. Bấm QUÉT, chọn lại dòng sản phẩm rồi in.");
        return models[choice.Index];
    }
}
