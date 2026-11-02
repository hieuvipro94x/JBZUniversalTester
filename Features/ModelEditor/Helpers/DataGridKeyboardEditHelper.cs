using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JBZUniversalTester.ModelEditor.Helpers;

public static class DataGridKeyboardEditHelper
{
    private sealed class InputState
    {
        public object? Item;
        public DataGridColumn? Column;
        public string? OriginalText;
        public bool DirectTextEdit;
        public void Reset() { Item = null; Column = null; OriginalText = null; DirectTextEdit = false; }
    }
    private static readonly ConditionalWeakTable<DataGrid, InputState> States = new();

    public static void Attach(DataGrid grid, int preferredEditableDisplayIndex)
    {
        if (States.TryGetValue(grid, out _)) return;
        var state = new InputState();
        States.Add(grid, state);
        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var origin = e.OriginalSource as DependencyObject;
            var cell = FindAncestor<DataGridCell>(origin);
            if (cell is null || !ReferenceEquals(FindAncestor<DataGrid>(cell), grid) ||
                FindAncestor<ButtonBase>(origin) is not null) return;
            if (e.ClickCount >= 2 && !cell.IsReadOnly)
            {
                SelectCell(grid, cell.DataContext, cell.Column);
                BeginInput(grid, state, direct: true);
                e.Handled = true;
                return;
            }
            if (!CommitInput(grid, state)) { e.Handled = true; return; }
            SelectCell(grid, cell.DataContext, cell.Column);
            cell.Focus();
            e.Handled = true;
        };
        grid.AddHandler(TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler((_, e) =>
            {
                if (string.IsNullOrEmpty(e.Text) || e.Text.All(char.IsControl) || grid.IsReadOnly ||
                    grid.CurrentCell.Column is null || grid.CurrentCell.Column.IsReadOnly) return;
                var cell = GetRealizedCell(grid, grid.CurrentCell.Item, grid.CurrentCell.Column);
                if (cell?.IsEditing == true) return; // Native TextBox input appends/edits normally.
                var editor = BeginInput(grid, state, direct: false);
                if (editor is null) return;
                editor.SelectAll();
                editor.SelectedText = e.Text;
                editor.CaretIndex = editor.Text.Length;
                UpdateEditorSource(editor);
                e.Handled = true;
            }), true);
        grid.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            var origin = e.OriginalSource as DependencyObject;
            var combo = FindAncestor<ComboBox>(origin) ?? FindAncestor<ComboBox>(Keyboard.FocusedElement as DependencyObject);
            if (combo?.IsDropDownOpen == true && e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape)
                return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
            if (e.Key == Key.F2)
            {
                e.Handled = BeginInput(grid, state, direct: true) is not null;
                return;
            }
            if (e.Key == Key.Escape)
            {
                if (state.OriginalText is not null && state.Item is not null && state.Column is not null &&
                    IsCurrentCell(grid, state.Item, state.Column))
                {
                    var editor = GetInputEditor(grid, state.Item, state.Column);
                    if (editor is not null) { editor.Text = state.OriginalText; UpdateEditorSource(editor); }
                }
                grid.CancelEdit(DataGridEditingUnit.Cell);
                grid.CancelEdit(DataGridEditingUnit.Row);
                state.Reset();
                FocusSelection(grid);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Delete or Key.Back)
            {
                var current = grid.CurrentCell;
                if (!current.IsValid || GetRealizedCell(grid, current.Item, current.Column)?.IsEditing == true)
                    return; // Native Delete/Backspace while typing.
                var editor = BeginInput(grid, state, direct: false);
                if (editor is null) return;
                editor.Clear(); UpdateEditorSource(editor);
                if (e.Key == Key.Delete && CommitInput(grid, state)) FocusSelection(grid);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                e.Handled = MoveTab(grid, state, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = MoveAndEdit(grid, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Key.Up : Key.Down,
                    preferredEditableDisplayIndex);
                return;
            }
            if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right)) return;
            if (state.DirectTextEdit && state.Item is not null && state.Column is not null &&
                IsCurrentCell(grid, state.Item, state.Column) && GetRealizedCell(grid, state.Item, state.Column)?.IsEditing == true)
                return; // F2/double-click deliberately edits characters, like Excel.
            e.Handled = MoveAndEdit(grid, e.Key, preferredEditableDisplayIndex);
        }), true);
    }

    private static TextBox? BeginInput(DataGrid grid, InputState state, bool direct)
    {
        var current = grid.CurrentCell;
        if (!current.IsValid || grid.IsReadOnly || current.Column.IsReadOnly) return null;
        bool alreadyEditing = GetRealizedCell(grid, current.Item, current.Column)?.IsEditing == true;
        if (!alreadyEditing && !grid.BeginEdit()) return null;
        var editor = GetInputEditor(grid, current.Item, current.Column);
        if (editor is null) return null;
        if (!alreadyEditing || !ReferenceEquals(state.Item, current.Item) || !ReferenceEquals(state.Column, current.Column))
        {
            state.Item = current.Item; state.Column = current.Column; state.OriginalText = editor.Text;
        }
        state.DirectTextEdit = direct;
        editor.Focus();
        if (direct) editor.CaretIndex = editor.Text.Length;
        else editor.SelectAll();
        return editor;
    }

    private static TextBox? GetInputEditor(DataGrid grid, object item, DataGridColumn column)
    {
        var cell = GetCell(grid, item, column);
        if (cell is null) return null;
        column.GetCellContent(item)?.ApplyTemplate();
        var combo = FindVisualChild<ComboBox>(cell);
        combo?.ApplyTemplate();
        return FindVisualChild<TextBox>(cell);
    }

    private static void UpdateEditorSource(TextBox editor)
    {
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        FindAncestor<ComboBox>(editor)?.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
    }

    private static bool CommitInput(DataGrid grid, InputState? state)
    {
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        state?.Reset();
        return true;
    }

    private static void SelectCell(DataGrid grid, object item, DataGridColumn column)
    {
        grid.CurrentCell = new DataGridCellInfo(item, column);
        if (grid.SelectionUnit == DataGridSelectionUnit.Cell)
        {
            grid.SelectedCells.Clear(); grid.SelectedCells.Add(grid.CurrentCell);
        }
        else grid.SelectedItem = item;
    }

    private static void FocusSelection(DataGrid grid)
    {
        var target = grid.CurrentCell;
        if (!target.IsValid) return;
        grid.ScrollIntoView(target.Item, target.Column);
        var cell = GetRealizedCell(grid, target.Item, target.Column);
        if (cell is not null) cell.Focus();
        else grid.Dispatcher.BeginInvoke(() =>
        {
            if (IsCurrentCell(grid, target.Item, target.Column)) GetCell(grid, target.Item, target.Column)?.Focus();
        }, DispatcherPriority.Input);
    }

    internal static bool MoveAndEdit(DataGrid grid, Key key, int preferredEditableDisplayIndex)
    {
        if (grid.Items.Count == 0 || grid.Columns.Count == 0) return false;
        var current = grid.CurrentCell;
        int row = current.IsValid ? grid.Items.IndexOf(current.Item) : Math.Max(0, grid.SelectedIndex);
        int column = current.Column?.DisplayIndex ?? preferredEditableDisplayIndex;
        States.TryGetValue(grid, out var state);
        if (!CommitInput(grid, state)) return true;
        if (key == Key.Up) row--; else if (key == Key.Down) row++;
        else if (key == Key.Left) column--; else if (key == Key.Right) column++;
        if (row < 0 || row >= grid.Items.Count) return true;
        int step = key == Key.Left ? -1 : 1;
        while (column >= 0 && column < grid.Columns.Count)
        {
            var candidate = grid.Columns.FirstOrDefault(c => c.DisplayIndex == column && c.Visibility == Visibility.Visible);
            if (candidate is not null)
            {
                SelectCell(grid, grid.Items[row], candidate); FocusSelection(grid); return true;
            }
            column += step;
        }
        return true;
    }

    private static bool MoveTab(DataGrid grid, InputState state, int direction)
    {
        var current = grid.CurrentCell;
        if (!current.IsValid || grid.Items.Count == 0 || grid.Columns.Count == 0) return false;
        if (!CommitInput(grid, state)) return true;
        int flat = grid.Items.IndexOf(current.Item) * grid.Columns.Count + current.Column.DisplayIndex;
        for (flat += direction; flat >= 0 && flat < grid.Items.Count * grid.Columns.Count; flat += direction)
        {
            int columnIndex = flat % grid.Columns.Count;
            var column = grid.Columns.FirstOrDefault(c => c.DisplayIndex == columnIndex &&
                c.Visibility == Visibility.Visible && !c.IsReadOnly && !grid.IsReadOnly);
            if (column is null) continue;
            SelectCell(grid, grid.Items[flat / grid.Columns.Count], column); FocusSelection(grid); return true;
        }
        return false;
    }

    private static bool IsCurrentCell(DataGrid grid, object item, DataGridColumn column) =>
        ReferenceEquals(grid.CurrentCell.Item, item) && ReferenceEquals(grid.CurrentCell.Column, column);

    private static DataGridCell? GetCell(
        DataGrid grid,
        object item,
        DataGridColumn column)
    {
        var realized = GetRealizedCell(grid, item, column);
        if (realized is not null) return realized;
        grid.ScrollIntoView(item, column);
        grid.UpdateLayout();

        return GetRealizedCell(grid, item, column);
    }

    private static DataGridCell? GetRealizedCell(DataGrid grid, object item, DataGridColumn column)
    {

        var row = grid.ItemContainerGenerator.ContainerFromItem(item)
                  as DataGridRow;
        if (row is null) return null;

        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        if (presenter is null) return null;

        var columnIndex = grid.Columns.IndexOf(column);
        if (columnIndex < 0) return null;

        return presenter.ItemContainerGenerator
            .ContainerFromIndex(columnIndex) as DataGridCell;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0;
             i < VisualTreeHelper.GetChildrenCount(parent);
             i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typed)
                return typed;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
                return typed;

            child = child is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(child)
                : child is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(child);
        }

        return null;
    }
}
