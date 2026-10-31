using System.Windows.Controls;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Pages;

public partial class BasePage : System.Windows.Controls.UserControl
{
    public event EventHandler? Changed;
    public BasePage()
    {
        InitializeComponent();
        ModeBox.ItemsSource = new[] { "Sử dụng mã hàng nội bộ", "Sử dụng mã hàng khách hàng", "Mã khách hàng theo định dạng mã nội bộ", "Mã nội bộ theo định dạng mã khách hàng" };
        foreach (var box in new[] { ModelBox, NumberBox, NameBox, KindBox, EoBox, CustomerBox }) box.TextChanged += (_, __) => Changed?.Invoke(this, EventArgs.Empty);
        ModeBox.SelectionChanged += (_, __) => Changed?.Invoke(this, EventArgs.Empty);
    }
    public void LoadData(BaseInfo d) { ModelBox.Text = d.Model; NumberBox.Text = d.Number; NameBox.Text = d.Name; KindBox.Text = d.Kind; EoBox.Text = d.EoNo; CustomerBox.Text = d.Customer; ModeBox.SelectedIndex = Math.Clamp(d.UseCustomer, 0, 3); }
    public BaseInfo ReadData() => new() { Model = ModelBox.Text.Trim(), Number = NumberBox.Text.Trim(), Name = NameBox.Text.Trim(), Kind = KindBox.Text.Trim(), EoNo = EoBox.Text.Trim(), Customer = CustomerBox.Text.Trim(), UseCustomer = Math.Max(0, ModeBox.SelectedIndex) };
}
