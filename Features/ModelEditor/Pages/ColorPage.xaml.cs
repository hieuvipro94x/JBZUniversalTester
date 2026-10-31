using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JBZUniversalTester.ModelEditor.Dialogs;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.ModelEditor.Pages;

public partial class ColorPage : System.Windows.Controls.UserControl, INotifyPropertyChanged
{
    private readonly ColorStore _store; private string _selectedHex = "#FFFFFF"; private int _selectedIndex = -1;
    public ObservableCollection<WireColor> Colors { get; } = []; public string SelectedHex { get => _selectedHex; set { _selectedHex = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedHex))); } }
    public event PropertyChangedEventHandler? PropertyChanged; public event EventHandler? ColorsChanged;
    public ColorPage() { InitializeComponent(); _store = new ColorStore(); DataContext = this; Reload(); }
    public IReadOnlyList<WireColor> CurrentColors => Colors.Select(c => c.Clone()).ToList();
    public void Reload() { Colors.Clear(); foreach (var c in _store.Load()) Colors.Add(c); _selectedIndex = -1; }
    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Grid.SelectedItem is not WireColor c) { _selectedIndex = -1; return; } _selectedIndex = Grid.SelectedIndex; CodeBox.Text = c.Code; NameBox.Text = c.Name; SelectedHex = c.Value; }
    private void ChooseColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var initial = (Color)ColorConverter.ConvertFromString(SelectedHex);
            var chosen = RgbColorDialog.Show(Window.GetWindow(this), initial);
            if (chosen is { } color)
                SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Không chọn được màu",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void NewColor_Click(object sender, RoutedEventArgs e)
    {
        Grid.SelectedIndex = -1;
        _selectedIndex = -1;
        CodeBox.Clear();
        NameBox.Clear();
        SelectedHex = "#FFFFFF";
        CodeBox.Focus();
    }
    private void Init_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Khôi phục danh sách màu mặc định?", "Khởi tạo màu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; Colors.Clear(); foreach (var c in ColorStore.Defaults) Colors.Add(c.Clone()); _store.Save(Colors); ColorsChanged?.Invoke(this, EventArgs.Empty); }
    private void Save_Click(object sender, RoutedEventArgs e) { var code = CodeBox.Text.Trim(); var name = NameBox.Text.Trim(); if (code.Length == 0 || name.Length == 0) { MessageBox.Show("Hãy nhập mã màu và tên màu.", "Thiếu dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning); return; } if (Colors.Select((c, i) => (c, i)).Any(x => x.i != _selectedIndex && x.c.Code == code)) { MessageBox.Show($"Mã màu {code} đã tồn tại.", "Trùng mã màu", MessageBoxButton.OK, MessageBoxImage.Warning); return; } var value = new WireColor { Code = code, Name = name, Value = SelectedHex }; if (_selectedIndex >= 0 && _selectedIndex < Colors.Count) Colors[_selectedIndex] = value; else Colors.Add(value); _store.Save(Colors); _selectedIndex = -1; Grid.SelectedIndex = -1; ColorsChanged?.Invoke(this, EventArgs.Empty); }
    private void Delete_Click(object sender, RoutedEventArgs e) { var i = Grid.SelectedIndex; if (i < 0) return; Colors.RemoveAt(i); _store.Save(Colors); _selectedIndex = -1; ColorsChanged?.Invoke(this, EventArgs.Empty); }
}
