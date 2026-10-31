using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using JBZUniversalTester.ModelEditor.Helpers;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.ModelEditor.Pages;

public sealed class ConnectorRow : ObservableObject
{
    private int _index; private string _name = ""; private int _pinCount; private int _startIo; private bool _waterproof;
    public int Index { get => _index; set => SetField(ref _index, value); }
    public string Name { get => _name; set => SetField(ref _name, value); }
    public int PinCount { get => _pinCount; set => SetField(ref _pinCount, value); }
    public int StartIo { get => _startIo; set => SetField(ref _startIo, value); }
    private int _leakChannel;
    public int LeakChannel { get => _leakChannel; set => SetField(ref _leakChannel, value); }
    public bool Waterproof { get => _waterproof; set => SetField(ref _waterproof, value); }
}

public partial class ConnectorPage : System.Windows.Controls.UserControl
{
    public ObservableCollection<ConnectorRow> Rows { get; } = [];
    public event EventHandler? Changed;
    private bool _loading;
    public ConnectorPage()
    {
        InitializeComponent();
        DataContext = this;
        // ↑/↓ chuyển dòng và tự vào cột TÊN CONNECTOR nếu đang đứng ở cột khóa.
        DataGridKeyboardEditHelper.Attach(Grid, 1);
        Rows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (ConnectorRow row in e.OldItems) row.PropertyChanged -= RowChanged;
            if (e.NewItems is not null)
                foreach (ConnectorRow row in e.NewItems) row.PropertyChanged += RowChanged;
            if (!_loading)
                Changed?.Invoke(this, EventArgs.Empty);
        };
    }
    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void LoadData(ModelDocument doc)
    {
        _loading = true; Rows.Clear(); var i = 1; foreach (var c in doc.Connectors) Rows.Add(new ConnectorRow { Index = i++, Name = c.Name, PinCount = c.PinCount, StartIo = c.StartIo, Waterproof = c.Waterproof, LeakChannel = c.LeakChannel }); _loading = false;
    }
    public List<Connector> ReadData()
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true); Grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (Rows.Any(r => r.Index <= 0))
            throw new ValidationException("STT connector phải lớn hơn 0.");
        if (Rows.GroupBy(r => r.Index).Any(g => g.Count() > 1))
            throw new ValidationException("STT connector không được trùng nhau.");
        // STT là thứ tự C1, C2... thực tế khi ghi MODEL/THT, không còn chỉ là cột hiển thị.
        var ordered = Rows.OrderBy(r => r.Index).ToList();
        var result = ordered.Select(r => new Connector { Name = r.Name.Trim(), PinCount = r.PinCount, StartIo = r.StartIo, Waterproof = r.Waterproof, LeakChannel = r.LeakChannel }).ToList();
        LeakLayoutService.Validate(result);
        return result;
    }
    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // I/O BẮT ĐẦU là dữ liệu người dùng được phép nhập tay.
        // Không tự tính lại sau khi sửa SỐ PIN hoặc I/O vì việc đó sẽ ghi đè
        // giá trị vừa nhập. Validation khi lưu sẽ kiểm tra giá trị hợp lệ.
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshIndexes()
    {
        if (Rows.Count == 0) return;
        _loading = true;
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].Index = i + 1;
        _loading = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        // WPF DataGrid có thể trả SelectedIndex trỏ vào NewItemPlaceholder
        // hoặc selection cũ. Vì vậy không bao giờ dùng index đó trực tiếp.
        var selectedIndex = Grid.CurrentCell.Item is ConnectorRow selectedRow ? Rows.IndexOf(selectedRow) : -1;

        var insertIndex =
            selectedIndex >= 0 && selectedIndex < Rows.Count
                ? selectedIndex + 1
                : Rows.Count;

        insertIndex = Math.Clamp(insertIndex, 0, Rows.Count);

        var startIo = 1;
        if (insertIndex > 0 && Rows.Count > 0)
        {
            var previousIndex = Math.Min(insertIndex - 1, Rows.Count - 1);
            var previous = Rows[previousIndex];
            startIo = previous.StartIo + Math.Max(1, previous.PinCount);
        }

        var newRow = new ConnectorRow
        {
            Index = insertIndex + 1,
            Name = (insertIndex + 1).ToString(),
            PinCount = 1,
            StartIo = startIo,
            Waterproof = false
        };

        Rows.Insert(insertIndex, newRow);
        RefreshIndexes();

        Grid.CurrentCell = new DataGridCellInfo(newRow, Grid.Columns[1]);
        Grid.SelectedCells.Clear();
        Grid.SelectedCells.Add(Grid.CurrentCell);
        Grid.ScrollIntoView(newRow);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var index = Grid.CurrentCell.Item is ConnectorRow selectedRow ? Rows.IndexOf(selectedRow) : -1;
        if (index < 0 || index >= Rows.Count)
            return;

        Rows.RemoveAt(index);
        RefreshIndexes();

        if (Rows.Count > 0)
            Grid.CurrentCell = new DataGridCellInfo(Rows[Math.Min(index, Rows.Count - 1)], Grid.Columns[1]);
    }
}
