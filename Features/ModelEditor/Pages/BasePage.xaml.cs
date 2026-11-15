using System.Windows.Controls;
using System.Windows;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Pages;

public partial class BasePage : System.Windows.Controls.UserControl
{
    public event EventHandler? Changed;
    private readonly (TextBox Box, TextBlock Hint)[] _hintFields;
    private readonly HashSet<TextBox> _suppressedHints = [];
    private bool _loading, _showHints;
    public BasePage()
    {
        InitializeComponent();
        ModeBox.ItemsSource = new[] { "Sử dụng mã hàng nội bộ", "Sử dụng mã hàng khách hàng", "Mã khách hàng theo định dạng mã nội bộ", "Mã nội bộ theo định dạng mã khách hàng" };
        _hintFields = [(ModelBox, ModelHint), (NumberBox, NumberHint), (NameBox, NameHint),
            (KindBox, KindHint), (EoBox, EoHint), (CustomerBox, CustomerHint)];
        foreach (var (box, hint) in _hintFields)
        {
            void HideEmptyHint()
            {
                if (box.Text.Length == 0) _suppressedHints.Add(box);
                UpdateHint(box, hint);
            }
            box.GotKeyboardFocus += (_, _) => HideEmptyHint();
            box.PreviewMouseLeftButtonDown += (_, _) => HideEmptyHint();
            box.LostKeyboardFocus += (_, _) => { _suppressedHints.Remove(box); UpdateHint(box, hint); };
            box.TextChanged += (_, _) =>
            {
                if (_loading) return;
                _suppressedHints.Remove(box); UpdateHint(box, hint);
                Changed?.Invoke(this, EventArgs.Empty);
            };
        }
        ModeBox.SelectionChanged += (_, __) => { if (!_loading) Changed?.Invoke(this, EventArgs.Empty); };
    }
    private void UpdateHint(TextBox box, TextBlock hint) => hint.Visibility =
        _showHints && box.Text.Length == 0 && !_suppressedHints.Contains(box) ? Visibility.Visible : Visibility.Collapsed;

    public void LoadData(BaseInfo d, bool showHints = false)
    {
        _loading = true; _showHints = showHints; _suppressedHints.Clear();
        try
        {
            ModelBox.Text = d.Model; NumberBox.Text = d.Number; NameBox.Text = d.Name;
            KindBox.Text = d.Kind; EoBox.Text = d.EoNo; CustomerBox.Text = d.Customer;
            ModeBox.SelectedIndex = Math.Clamp(d.UseCustomer, 0, 3);
        }
        finally
        {
            _loading = false;
            foreach (var (box, hint) in _hintFields) UpdateHint(box, hint);
        }
    }
    public BaseInfo ReadData() => new() { Model = ModelBox.Text.Trim(), Number = NumberBox.Text.Trim(), Name = NameBox.Text.Trim(), Kind = KindBox.Text.Trim(), EoNo = EoBox.Text.Trim(), Customer = CustomerBox.Text.Trim(), UseCustomer = Math.Max(0, ModeBox.SelectedIndex) };
}
