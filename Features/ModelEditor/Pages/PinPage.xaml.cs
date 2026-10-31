using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JBZUniversalTester.ModelEditor.Dialogs;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Pages;

public sealed class PinRow : ObservableObject
{
    private int _io; private string _testNo = ""; private int _mainIo = -1; private string _coupling = "";
    public Pin Original { get; init; } = new();
    public int Io { get => _io; set => SetField(ref _io, value); }
    public string Connector => Original.Connector; public int PinNo => Original.PinNo;
    public string TestNo { get => _testNo; set => SetField(ref _testNo, value); }
    public int MainIo { get => _mainIo; set { if (SetField(ref _mainIo, value)) Raise(nameof(Role)); } }
    public string Role => MainIo == -1 ? "NGUỒN" : "ĐÍCH";
    public string CouplingText { get => _coupling; set => SetField(ref _coupling, value); }
}
public partial class PinPage : System.Windows.Controls.UserControl
{
    public ObservableCollection<PinRow> Rows { get; } = []; public event EventHandler? Changed; public event EventHandler? ConnectionsChanged; public event EventHandler? IoChanged; private bool _loading, _synchronizing;
    public PinPage()
    {
        InitializeComponent();
        DataContext = this;

        // PIN chỉ hiển thị đúng số dòng Pin thật.
        // Không cho WPF tạo NewItemPlaceholder ở cuối MainGrid.
        MainGrid.CanUserAddRows = false;
        MainGrid.CanUserDeleteRows = false;
        MainGrid.CanUserReorderColumns = false;
        MainGrid.CanUserResizeColumns = false;
        MainGrid.CanUserSortColumns = false;

    }
    public void LoadData(IEnumerable<Pin> pins) { _loading = true; Rows.Clear(); foreach (var p in pins.OrderBy(p => p.Io)) Rows.Add(new PinRow { Original = p.Clone(), Io = p.Io, TestNo = p.TestNo, MainIo = p.MainIo, CouplingText = string.Join('/', p.Coupling) }); MainGrid.UnselectAll(); _loading = false; }
    public List<Pin> ReadData() { MainGrid.CommitEdit(DataGridEditingUnit.Cell, true); MainGrid.CommitEdit(DataGridEditingUnit.Row, true); var list = new List<Pin>(); foreach (var r in Rows) { var p = r.Original.Clone(); p.Io = r.Io; p.TestNo = r.TestNo.Trim(); p.MainIo = r.MainIo == p.Io ? -1 : r.MainIo; p.Coupling = ParseCoupling(r.CouplingText); list.Add(p); } return list; }
    private static List<int> ParseCoupling(string text) { var result = new List<int>(); foreach (var s in text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { if (!int.TryParse(s, out var v)) throw new ValidationException($"I/O nối không phải số nguyên: {s}"); result.Add(v); } return result; }
    private void MainGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_loading) return;
        Changed?.Invoke(this, EventArgs.Empty);
        var column = e.Column.DisplayIndex;
        if (column is 4 or 6) ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        if (column == 0) IoChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Row.Item is PinRow row)
            {
                if (column == 0) SynchronizeIoChange(row);
                else if (column is 4 or 6) SynchronizeConnections(row, column);
            }
        });
    }
    private void MainGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (MainGrid.CurrentColumn?.DisplayIndex != 6 || MainGrid.SelectedItem is not PinRow row) return; var available = Rows.Where(r => r.Io != row.Io).Select(r => r.Io).OrderBy(x => x).ToList(); if (available.Count == 0) return; var dlg = new InputDialog("Thêm kết nối", "I/O đích:", available[0].ToString()) { Owner = Window.GetWindow(this) }; if (dlg.ShowDialog() == true && int.TryParse(dlg.Value, out var chosen) && available.Contains(chosen)) { var list = ParseCoupling(row.CouplingText); if (!list.Contains(chosen)) { list.Add(chosen); row.CouplingText = string.Join('/', list); SynchronizeConnections(row, 6); Changed?.Invoke(this, EventArgs.Empty); ConnectionsChanged?.Invoke(this, EventArgs.Empty); } } }


    private void SynchronizeIoChange(PinRow edited)
    {
        var oldIo = edited.Original.Io;
        var newIo = edited.Io;
        if (oldIo == newIo) return;

        // Chỉ đồng bộ tham chiếu khi số mới đã hợp lệ và duy nhất. Nếu người dùng
        // đang nhập dở/trùng số, giữ dữ liệu hiện tại và để validation báo khi lưu.
        if (newIo <= 0 || Rows.Any(row => !ReferenceEquals(row, edited) && row.Io == newIo))
            return;

        foreach (var row in Rows)
        {
            if (row.MainIo == oldIo)
                row.MainIo = newIo;

            var coupling = ParseCoupling(row.CouplingText)
                .Select(io => io == oldIo ? newIo : io)
                .Distinct()
                .ToList();
            row.CouplingText = string.Join('/', coupling);
        }

        edited.Original.Io = newIo;
    }

    private void SynchronizeConnections(PinRow edited, int column)
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            if (column == 4)
            {
                foreach (var row in Rows)
                {
                    var coupling = ParseCoupling(row.CouplingText).Where(io => io != edited.Io).ToList();
                    row.CouplingText = string.Join('/', coupling);
                }

                var source = Rows.FirstOrDefault(row => row.Io == edited.MainIo && row.Io != edited.Io);
                if (source is null) return;
                source.MainIo = -1;
                var values = ParseCoupling(source.CouplingText);
                if (!values.Contains(edited.Io)) values.Add(edited.Io);
                source.CouplingText = string.Join('/', values.Distinct());
            }
            else
            {
                var targets = ParseCoupling(edited.CouplingText).Where(io => io != edited.Io).Distinct().ToList();
                edited.MainIo = -1;
                edited.CouplingText = string.Join('/', targets);
                foreach (var row in Rows.Where(row => row.Io != edited.Io))
                {
                    if (targets.Contains(row.Io))
                    {
                        if (row.MainIo != -1 && row.MainIo != edited.Io)
                        {
                            var oldSource = Rows.FirstOrDefault(candidate => candidate.Io == row.MainIo);
                            if (oldSource is not null)
                                oldSource.CouplingText = string.Join('/', ParseCoupling(oldSource.CouplingText).Where(io => io != row.Io));
                        }
                        row.MainIo = edited.Io;
                        row.CouplingText = "";
                    }
                    else if (row.MainIo == edited.Io)
                    {
                        row.MainIo = -1;
                    }
                }
            }
        }
        catch (ValidationException)
        {
            // Giữ nguyên nội dung đang nhập; validation khi chuyển trang/lưu sẽ hiển thị lỗi rõ ràng.
        }
        finally { _synchronizing = false; }
    }
}
