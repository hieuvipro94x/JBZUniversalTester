using System.Collections.ObjectModel;
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
    private string _line = "", _add = "", _thickness = "", _color = "";
    private bool _isWireUnconnected;
    private IReadOnlyDictionary<string, WireColor> _map =
        new Dictionary<string, WireColor>(StringComparer.Ordinal);

    public Pin Original { get; init; } = new();
    public int Io { get => _io; set => SetField(ref _io, value); }
    public string Connector => Original.Connector;
    public int PinNo => Original.PinNo;

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
    public ObservableCollection<LineRow> Rows { get; } = [];
    public ObservableCollection<string> ColorCodes { get; } = [];
    public event EventHandler? Changed;
    public event EventHandler? ConnectionsChanged;
    public bool HasStructuralChanges { get; private set; }

    private Dictionary<string, WireColor> _map = new(StringComparer.Ordinal);
    private bool _loading, _applyingAutomaticColors;
    private LineRow? _selectedRow;

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
    }

    public void SetColors(IEnumerable<WireColor> colors)
    {
        var list = colors.Select(c => c.Clone()).ToList();
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
        _loading = true;
        try
        {
            HasStructuralChanges = false;
            _selectedRow = null;
            Rows.Clear();
            foreach (var p in pins.OrderBy(p => p.Io))
            {
                var row = new LineRow
                {
                    Original = p.Clone(),
                    Io = p.Io,
                    Line = p.Line,
                    Add = p.Add,
                    Thickness = p.Thickness,
                    Color = p.Color
                };
                row.SetMap(_map);
                row.PropertyChanged += (_, e) =>
                {
                    if (_loading || _applyingAutomaticColors || e.PropertyName is not
                        (nameof(LineRow.Io) or nameof(LineRow.Line) or nameof(LineRow.Add) or nameof(LineRow.Thickness) or nameof(LineRow.Color)))
                        return;

                    if (e.PropertyName is nameof(LineRow.Io) or nameof(LineRow.Line) or nameof(LineRow.Add))
                        RefreshConnectionIndicators();
                    if (e.PropertyName == nameof(LineRow.Line))
                        ApplyAutomaticColors(row, true);

                    Changed?.Invoke(this, EventArgs.Empty);
                    if (e.PropertyName is nameof(LineRow.Io) or nameof(LineRow.Line) or nameof(LineRow.Add))
                        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                };
                Rows.Add(row);
            }

            RefreshConnectionIndicators();
        }
        finally
        {
            _loading = false;
        }
    }

    public List<Pin> ReadData()
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);

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
        ModelLogic.InsertPinAfter(pins, index);
        LoadData(pins);
        HasStructuralChanges = true;
        Grid.CurrentCell = new DataGridCellInfo(Rows[index + 1], Grid.Columns[3]);
        Grid.ScrollIntoView(Rows[index + 1]);
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
