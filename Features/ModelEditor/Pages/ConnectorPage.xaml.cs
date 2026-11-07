using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private bool _updatingStartIos;
    private readonly HashSet<ConnectorRow> _automaticStarts = [];
    public ConnectorPage()
    {
        InitializeComponent();
        DataContext = this;
        // ↑/↓ chuyển dòng và tự vào cột TÊN CONNECTOR nếu đang đứng ở cột khóa.
        DataGridKeyboardEditHelper.Attach(Grid, 1);
        Rows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (ConnectorRow row in e.OldItems)
                {
                    row.PropertyChanged -= RowChanged;
                    _automaticStarts.Remove(row);
                }
            if (e.NewItems is not null)
                foreach (ConnectorRow row in e.NewItems) row.PropertyChanged += RowChanged;
            DeleteConnectorButton.IsEnabled = Rows.Count > 0;
            if (!_loading)
                Changed?.Invoke(this, EventArgs.Empty);
        };
    }
    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || _updatingStartIos) return;
        if (sender is ConnectorRow row && e.PropertyName == nameof(ConnectorRow.StartIo))
            _automaticStarts.Remove(row);
        if (e.PropertyName is nameof(ConnectorRow.PinCount) or nameof(ConnectorRow.StartIo))
            UpdateAutomaticStartIos();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void LoadData(ModelDocument doc)
    {
        _loading = true;
        DeleteConnectorButton.IsEnabled = false;
        try
        {
            foreach (var row in Rows) row.PropertyChanged -= RowChanged;
            _automaticStarts.Clear();
            Rows.Clear();
            long nextIo = 1;
            foreach (var connector in doc.Connectors)
            {
                var row = new ConnectorRow
                {
                    Index = Rows.Count + 1,
                    Name = connector.Name,
                    PinCount = connector.PinCount,
                    StartIo = connector.StartIo,
                    Waterproof = connector.Waterproof,
                    LeakChannel = connector.LeakChannel
                };
                // Consecutive addresses follow the preceding connector. A gap
                // is an explicit anchor, including reserved IO ranges and CLIP.
                if (row.StartIo <= 0 || row.StartIo == nextIo)
                    _automaticStarts.Add(row);
                Rows.Add(row);
                nextIo = (long)row.StartIo + Math.Max(1, row.PinCount);
            }
        }
        finally { _loading = false; }
    }
    private void UpdateAutomaticStartIos()
    {
        _updatingStartIos = true;
        try
        {
            long nextIo = 1;
            foreach (var row in Rows.OrderBy(row => row.Index))
            {
                if (_automaticStarts.Contains(row))
                    row.StartIo = nextIo is > 0 and <= int.MaxValue ? (int)nextIo : 0;
                nextIo = (long)row.StartIo + Math.Max(1, row.PinCount);
            }
        }
        finally { _updatingStartIos = false; }
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
        // Legacy THT metadata is retained, but Leak is configured in Settings.
        // Hidden legacy flags without a channel must not block topology edits.
        return result;
    }
    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Numeric bindings update immediately while typing. RowChanged handles
        // automatic addresses; an explicit StartIo edit establishes an anchor.
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshIndexes()
    {
        if (Rows.Count == 0) return;
        _loading = true;
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].Index = i + 1;
        _loading = false;
        UpdateAutomaticStartIos();
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
            var nextIo = (long)previous.StartIo + Math.Max(1, previous.PinCount);
            startIo = nextIo is > 0 and <= int.MaxValue ? (int)nextIo : 0;
        }

        var newRow = new ConnectorRow
        {
            Index = insertIndex + 1,
            Name = (insertIndex + 1).ToString(),
            PinCount = 1,
            StartIo = startIo,
            Waterproof = false
        };

        _automaticStarts.Add(newRow);
        Rows.Insert(insertIndex, newRow);
        RefreshIndexes();

        Grid.CurrentCell = new DataGridCellInfo(newRow, Grid.Columns[1]);
        Grid.SelectedCells.Clear();
        Grid.SelectedCells.Add(Grid.CurrentCell);
        Grid.ScrollIntoView(newRow);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Rows.Count == 0) return;
        var index = Rows.Count - 1;
        var last = Rows[index];

        // Discard edits only on the row being removed. Edits to another row
        // must commit successfully before the final connector can be deleted.
        if (Grid.Items is IEditableCollectionView view && view.IsEditingItem &&
            !ReferenceEquals(view.CurrentEditItem, last))
        {
            if (!Grid.CommitEdit(DataGridEditingUnit.Cell, true) || !Grid.CommitEdit(DataGridEditingUnit.Row, true))
                return;
        }
        else
        {
            Grid.CancelEdit(DataGridEditingUnit.Cell);
            Grid.CancelEdit(DataGridEditingUnit.Row);
        }
        Rows.RemoveAt(index);
        RefreshIndexes();

        Grid.SelectedCells.Clear();
        if (Rows.Count > 0)
        {
            var next = Rows[^1];
            Grid.CurrentCell = new DataGridCellInfo(next, Grid.Columns[1]);
            Grid.SelectedCells.Add(Grid.CurrentCell);
            Grid.ScrollIntoView(next);
        }
        DeleteConnectorButton.IsEnabled = Rows.Count > 0;
    }
}
