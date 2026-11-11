using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JBZUniversalTester.ModelEditor.Helpers;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.ModelEditor.Pages;

public sealed class LineRow : ObservableObject
{
    private int _io;
    private int _pinNo;
    private string _line = "", _add = "", _thickness = "", _color = "";
    private bool _isWireUnconnected;
    private IReadOnlyDictionary<string, WireColor> _map =
        new Dictionary<string, WireColor>(StringComparer.Ordinal);

    public Pin Original { get; set; } = new();
    public int Io { get => _io; set => SetField(ref _io, value); }
    public string Connector => Original.Connector;
    public int PinNo { get => _pinNo; set => SetField(ref _pinNo, value); }

    public string Line { get => _line; set => SetField(ref _line, value); }
    public string Add { get => _add; set => SetField(ref _add, value); }
    public string Thickness { get => _thickness; set => SetField(ref _thickness, value); }
    public string Color
    {
        get => _color;
        set
        {
            if (SetField(ref _color, value))
                RefreshColorPreview();
        }
    }

    // true => TÊN DÂY có dữ liệu nhưng chưa tạo được network với chân khác.
    // XAML dùng giá trị này để hiển thị chữ đỏ; khi có kết nối sẽ về đen.
    public bool IsWireUnconnected
    {
        get => _isWireUnconnected;
        private set => SetField(ref _isWireUnconnected, value);
    }

    private readonly string?[] _preview = new string?[2];
    private readonly string?[] _tips = new string?[2];
    public string? Preview1 => _preview[0];
    public string? Preview2 => _preview[1];
    public string? PreviewTip1 => _tips[0];
    public string? PreviewTip2 => _tips[1];

    public void SetMap(IReadOnlyDictionary<string, WireColor> map)
    {
        _map = map;
        RefreshColorPreview();
    }

    public void SetConnectionState(bool connected)
    {
        IsWireUnconnected = !string.IsNullOrWhiteSpace(Line) && !connected;
    }

    private void RefreshColorPreview()
    {
        Array.Clear(_preview, 0, _preview.Length);
        Array.Clear(_tips, 0, _tips.Length);

        var parts = WireColorInput.Normalize(_color ?? "", _map.Keys)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(_preview.Length)
            .ToArray();

        for (var i = 0; i < parts.Length; i++)
        {
            var code = parts[i];
            if (_map.TryGetValue(code, out var c))
            {
                _preview[i] = c.Value;
                _tips[i] = $"{code} - {c.Name} ({c.Value})";
            }
            else
            {
                _tips[i] = $"Không có đúng mã màu '{code}' trong CÀI ĐẶT MÀU (phân biệt hoa/thường)";
            }
        }

        Raise(nameof(Preview1));
        Raise(nameof(Preview2));
        Raise(nameof(PreviewTip1));
        Raise(nameof(PreviewTip2));
    }
}

public partial class LinePage : UserControl
{
    private readonly RowCollection _rows = new();
    public ObservableCollection<LineRow> Rows => _rows;
    private sealed class RowCollection : ObservableCollection<LineRow>
    {
        public void ReplaceAll(IReadOnlyList<LineRow> rows)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var row in rows) Items.Add(row);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }
    public ObservableCollection<string> ColorCodes { get; } = [];
    public event EventHandler? Changed;
    public event EventHandler? ConnectionsChanged;
    public bool HasStructuralChanges { get; private set; }

    private Dictionary<string, WireColor> _map = new(StringComparer.Ordinal);
    private bool _loading, _applyingAutomaticColors;
    private LineRow? _selectedRow;
    private string _lastSearchQuery = "";
    private readonly System.Windows.Threading.DispatcherTimer _wireSearchTimer = new(
        System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(180) };

    private void WireSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _wireSearchTimer.Stop();
        _lastSearchQuery = "";
        if (WireSearchStatus is not null) WireSearchStatus.Text = "";
        if (!_loading && !string.IsNullOrWhiteSpace(WireSearchBox.Text)) _wireSearchTimer.Start();
    }

    private void WireSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindWire_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClearWireSearch_Click(sender, e); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up)
        {
            // Leaving the search box with an arrow is an explicit navigation
            // request. Flush the current query before moving in the real grid.
            if (_wireSearchTimer.IsEnabled)
            {
                _wireSearchTimer.Stop();
                FindWire(advance: false);
            }
            if (Grid.CurrentCell.IsValid)
                e.Handled = DataGridKeyboardEditHelper.MoveAndEdit(Grid, e.Key, 3);
        }
    }

    private void ClearWireSearch_Click(object sender, RoutedEventArgs e)
    {
        WireSearchBox.Clear();
        WireSearchBox.Focus();
    }

    private void FindWire_Click(object sender, RoutedEventArgs e)
    {
        _wireSearchTimer.Stop();
        FindWire(advance: true);
    }

    private void WireSearchTimer_Tick(object? sender, EventArgs e)
    {
        _wireSearchTimer.Stop();
        if (!_loading) FindWire(advance: false);
    }

    private void FindWire(bool advance)
    {
        string query = WireSearchBox.Text.Trim();
        if (query.Length == 0) { WireSearchStatus.Text = "Nhập từ khóa"; return; }
        if (!Grid.CommitEdit(DataGridEditingUnit.Cell, true) || !Grid.CommitEdit(DataGridEditingUnit.Row, true))
        { WireSearchStatus.Text = "Kiểm tra ô đang sửa"; return; }
        var matches = Rows.Where(row => MatchesWireSearch(row, query)).ToList();
        if (matches.Count == 0) { WireSearchStatus.Text = "Không tìm thấy"; _lastSearchQuery = query; return; }
        int current = advance && string.Equals(query, _lastSearchQuery, StringComparison.OrdinalIgnoreCase) && _selectedRow is not null
            ? matches.IndexOf(_selectedRow) : -1;
        int next = (current + 1) % matches.Count;
        var target = matches[next];
        bool keepSearchFocus = WireSearchBox.IsKeyboardFocusWithin;
        int selectionStart = WireSearchBox.SelectionStart, selectionLength = WireSearchBox.SelectionLength;
        Grid.SelectedCells.Clear();
        Grid.CurrentCell = new DataGridCellInfo(target, Grid.Columns[0]);
        Grid.SelectedCells.Add(Grid.CurrentCell);
        Grid.ScrollIntoView(target, Grid.Columns[0]);
        _selectedRow = target;
        _lastSearchQuery = query;
        WireSearchStatus.Text = $"{next + 1}/{matches.Count} kết quả";
        if (keepSearchFocus)
        {
            WireSearchBox.Focus();
            WireSearchBox.Select(selectionStart, selectionLength);
        }
    }

    private static bool MatchesWireSearch(LineRow row, string query)
    {
        if (int.TryParse(query, out int io)) return row.Io == io;
        if (query.StartsWith("IO", StringComparison.OrdinalIgnoreCase) && int.TryParse(query[2..].Trim(), out io))
            return row.Io == io;
        if (query.StartsWith("PN", StringComparison.OrdinalIgnoreCase) && int.TryParse(query[2..].Trim(), out int pin))
            return row.PinNo == pin;
        if (query.StartsWith("CN", StringComparison.OrdinalIgnoreCase))
            return row.Connector.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                ("CN" + row.Connector).Equals(query, StringComparison.OrdinalIgnoreCase);
        return row.Connector.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Line.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Add.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public LinePage()
    {
        InitializeComponent();
        DataContext = this;

        Grid.CanUserAddRows = false;
        Grid.CanUserDeleteRows = false;
        Grid.CanUserReorderColumns = false;
        Grid.CanUserResizeColumns = false;
        Grid.CanUserSortColumns = false;
        DataGridKeyboardEditHelper.Attach(Grid, 3);
        _wireSearchTimer.Tick += WireSearchTimer_Tick;
        Unloaded += (_, _) => _wireSearchTimer.Stop();
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(WireSearchBox.Text) &&
                !string.Equals(_lastSearchQuery, WireSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                _wireSearchTimer.Start();
        };
    }

    public void SetColors(IEnumerable<WireColor> colors)
    {
        var list = colors.Select(c => c.Clone()).ToList();
        if (ColorCodes.SequenceEqual(list.Select(c => c.Code)) && list.All(c =>
                _map.TryGetValue(c.Code, out var existing) && existing.Name == c.Name && existing.Value == c.Value))
            return;
        _map = list
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .GroupBy(c => c.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Support Or with older palettes that used O for orange.
        if (!_map.ContainsKey("Or") && _map.TryGetValue("O", out var orange))
        {
            var alias = orange.Clone();
            alias.Code = "Or";
            _map.Add(alias.Code, alias);
        }

        ColorCodes.Clear();
        foreach (var c in list)
            ColorCodes.Add(c.Code);
        foreach (var row in Rows)
            row.SetMap(_map);
    }

    public void LoadData(IEnumerable<Pin> pins)
    {
        Pin[] ordered = EditorDisplayOrder.Pins(pins).ToArray();
        _loading = true;
        try
        {
            HasStructuralChanges = false;
            // Preserve row containers, selection and scroll after Save/tab return.
            // Rebase Original too: later IO remapping must use the saved topology.
            if (Rows.Count == ordered.Length && Rows.Zip(ordered).All(pair =>
                    pair.First.Io == pair.Second.Io && pair.First.Connector == pair.Second.Connector &&
                    pair.First.PinNo == pair.Second.PinNo))
            {
                for (int i = 0; i < ordered.Length; i++)
                {
                    var row = Rows[i]; var pin = ordered[i];
                    row.Original = pin.Clone();
                    row.PinNo = pin.PinNo;
                    row.Line = pin.Line; row.Add = pin.Add;
                    row.Thickness = pin.Thickness; row.Color = pin.Color;
                }
                RefreshConnectionIndicators();
                return;
            }
            var selectedRow = Grid.CurrentCell.Item as LineRow ?? _selectedRow;
            var selectedColumn = Grid.CurrentCell.Column;
            _selectedRow = null;
            _lastSearchQuery = "";
            _wireSearchTimer.Stop();
            WireSearchStatus.Text = "";
            var bySource = Rows.Where(row => row.Original.SourceThtRowIndex >= 0)
                .GroupBy(row => (row.Original.SourceThtRowIndex, row.Io, row.Connector))
                .ToDictionary(group => group.Key, group => new Queue<LineRow>(group));
            var byIdentity = Rows.GroupBy(row => (row.Io, row.Connector, row.PinNo))
                .ToDictionary(group => group.Key, group => new Queue<LineRow>(group));
            var reused = new HashSet<LineRow>();
            LineRow? Take(Queue<LineRow>? queue)
            {
                while (queue is { Count: > 0 })
                {
                    var row = queue.Dequeue();
                    if (reused.Add(row)) return row;
                }
                return null;
            }
            var newRows = new List<LineRow>(ordered.Length);
            foreach (var p in ordered)
            {
                bySource.TryGetValue((p.SourceThtRowIndex, p.Io, p.Connector), out var sourceRows);
                var retained = Take(sourceRows);
                if (retained is null)
                {
                    byIdentity.TryGetValue((p.Io, p.Connector, p.PinNo), out var identityRows);
                    retained = Take(identityRows);
                }
                if (retained is not null)
                {
                    retained.Original = p.Clone(); retained.PinNo = p.PinNo;
                    retained.Line = p.Line; retained.Add = p.Add;
                    retained.Thickness = p.Thickness; retained.Color = p.Color;
                    newRows.Add(retained);
                    continue;
                }
                var row = new LineRow
                {
                    Original = p.Clone(),
                    Io = p.Io,
                    PinNo = p.PinNo,
                    Line = p.Line,
                    Add = p.Add,
                    Thickness = p.Thickness,
                    Color = p.Color
                };
                row.SetMap(_map);
                row.PropertyChanged += (_, e) =>
                {
                    if (_loading || _applyingAutomaticColors || e.PropertyName is not
                        (nameof(LineRow.Io) or nameof(LineRow.PinNo) or nameof(LineRow.Line) or nameof(LineRow.Add) or nameof(LineRow.Thickness) or nameof(LineRow.Color)))
                        return;

                    if (e.PropertyName is nameof(LineRow.Io) or nameof(LineRow.Line) or nameof(LineRow.Add))
                        RefreshConnectionIndicators();
                    if (e.PropertyName == nameof(LineRow.Line))
                        ApplyAutomaticColors(row, true);

                    Changed?.Invoke(this, EventArgs.Empty);
                    if (e.PropertyName is nameof(LineRow.Io) or nameof(LineRow.Line) or nameof(LineRow.Add))
                        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                };
                newRows.Add(row);
            }
            _rows.ReplaceAll(newRows);
            if (selectedRow is not null && reused.Contains(selectedRow))
            {
                _selectedRow = selectedRow;
                Grid.CurrentCell = new DataGridCellInfo(selectedRow, selectedColumn ?? Grid.Columns[3]);
                Grid.SelectedCells.Clear(); Grid.SelectedCells.Add(Grid.CurrentCell);
                if (Grid.IsLoaded) Grid.ScrollIntoView(selectedRow, Grid.CurrentCell.Column);
            }
            RefreshConnectionIndicators();
        }
        finally
        {
            _loading = false;
        }
        if (!string.IsNullOrWhiteSpace(WireSearchBox.Text)) _wireSearchTimer.Start();
    }

    public List<Pin> ReadData()
    {
        if (!Grid.CommitEdit(DataGridEditingUnit.Cell, true) || !Grid.CommitEdit(DataGridEditingUnit.Row, true))
            throw new ValidationException("Kiểm tra ô đang sửa trong bảng Dây. I/O và PIN phải là số nguyên hợp lệ.");

        foreach (var row in Rows)
            row.Color = WireColorInput.Normalize(row.Color, _map.Keys);
        if (Rows.Count > 0)
            ApplyAutomaticColors(Rows[0], false);

        // Nếu I/O được đổi bằng tay, mọi MainIo/Coupling cũ phải đi theo I/O mới
        // để quan hệ dây không bị đứt chỉ vì đổi số kênh.
        var ioMap = Rows
            .GroupBy(row => row.Original.Io)
            .ToDictionary(group => group.Key, group => group.Last().Io);

        return Rows.Select(row =>
        {
            var pin = row.Original.Clone();
            pin.Io = row.Io;
            pin.PinNo = row.PinNo;
            pin.Line = row.Line.Trim();
            pin.Add = row.Add.Trim();
            pin.Thickness = row.Thickness.Trim();
            pin.Color = row.Color.Trim();
            if (pin.MainIo != -1 && ioMap.TryGetValue(pin.MainIo, out var mainIo))
                pin.MainIo = mainIo;
            pin.Coupling = pin.Coupling
                .Select(io => ioMap.TryGetValue(io, out var mapped) ? mapped : io)
                .Distinct()
                .ToList();
            return pin;
        }).ToList();
    }

    private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // AO/aN rows have no physical PIN number in the native HarnProg table.
        if (e.Column == Grid.Columns[2] && e.Row.Item is LineRow row &&
            row.Connector.Equals("CLIP", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            WireSearchStatus.Text = "AO/aN: chỉnh địa chỉ tại cột I/O";
        }
    }

    private void CommitColor(ComboBox combo)
    {
        if (_loading || combo.DataContext is not LineRow row) return;
        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        row.Color = WireColorInput.Normalize(combo.Text, _map.Keys);
        ApplyAutomaticColors(row, false);
        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
    }

    private void Color_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox combo && !combo.IsKeyboardFocusWithin)
            CommitColor(combo);
    }

    private void Color_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox combo)
            CommitColor(combo);
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_loading)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Grid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (Grid.SelectedCells.FirstOrDefault().Item is LineRow row)
            _selectedRow = row;
    }

    private void Grid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (Grid.CurrentCell.Item is LineRow row)
            _selectedRow = row;
    }

    private int SelectedRowIndex()
    {
        var row = Grid.SelectedCells.FirstOrDefault().Item as LineRow
            ?? Grid.CurrentCell.Item as LineRow
            ?? Grid.CurrentItem as LineRow
            ?? _selectedRow;
        return row is null ? -1 : Rows.IndexOf(row);
    }

    private void RefreshConnectionIndicators()
    {
        // Tính theo từng dòng để I/O trùng không làm toàn bộ tên dây chuyển đỏ.
        var pins = Rows.Select((row, index) => new Pin
        {
            Io = index + 1,
            Connector = row.Connector,
            PinNo = row.PinNo,
            Line = row.Line,
            Add = row.Add
        }).ToList();

        var status = ModelLogic.BuildWireConnectionStatus(pins);
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].SetConnectionState(status[i + 1]);
    }

    private void InsertRow_Click(object sender, RoutedEventArgs e)
    {
        var index = SelectedRowIndex();
        if (index < 0)
        {
            MessageBox.Show("Hãy chọn một ô trong dòng cần chèn.", "Chưa chọn dòng", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var pins = ReadData();
        try { ModelLogic.InsertPinAfter(pins, index); }
        catch (ValidationException ex)
        {
            MessageBox.Show(ex.Message, "Không thể thêm dòng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var added = pins[index + 1];
        LoadData(pins);
        HasStructuralChanges = true;
        var addedRow = Rows.First(row => row.Io == added.Io && row.PinNo == added.PinNo && row.Connector == added.Connector);
        Grid.CurrentCell = new DataGridCellInfo(addedRow, Grid.Columns[3]);
        Grid.ScrollIntoView(addedRow);
        Changed?.Invoke(this, EventArgs.Empty);
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var index = SelectedRowIndex();
        if (index < 0)
        {
            MessageBox.Show("Hãy chọn một ô trong dòng cần xóa.", "Chưa chọn dòng", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var pins = ReadData();
        try { ModelLogic.DeletePinAt(pins, index); }
        catch (ValidationException ex)
        {
            MessageBox.Show(ex.Message, "Không thể xóa dòng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoadData(pins);
        HasStructuralChanges = true;
        if (Rows.Count > 0)
        {
            var next = Rows[Math.Min(index, Rows.Count - 1)];
            Grid.CurrentCell = new DataGridCellInfo(next, Grid.Columns[3]);
            Grid.ScrollIntoView(next);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAutomaticColors(LineRow changedRow, bool lineChanged)
    {
        if (_loading || _applyingAutomaticColors)
            return;

        _applyingAutomaticColors = true;
        try
        {
            var sourcePins = Rows.Select(row => new Pin
            {
                Io = row.Io,
                Connector = row.Connector,
                Line = row.Line,
                Color = row.Color
            }).ToList();
            ModelLogic.ApplyAutomaticWireColors(sourcePins, Rows.IndexOf(changedRow), lineChanged);
            for (var i = 0; i < Rows.Count; i++)
                Rows[i].Color = sourcePins[i].Color;
        }
        finally
        {
            _applyingAutomaticColors = false;
        }
    }
}
