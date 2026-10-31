using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JBZUniversalTester.ModelEditor.Helpers;

public static class DataGridKeyboardEditHelper
{
    public static void Attach(DataGrid grid, int preferredEditableDisplayIndex)
    {
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Delete or Key.Back &&
                FindAncestor<TextBox>(Keyboard.FocusedElement as DependencyObject) is null &&
                grid.CurrentCell.Column is DataGridTextColumn { IsReadOnly: false } textColumn &&
                grid.CurrentCell.Item is { } textItem)
            {
                if (grid.BeginEdit())
                {
                    grid.Dispatcher.BeginInvoke(() =>
                    {
                        FocusEditor(grid, textItem, textColumn);
                        if (FindAncestor<TextBox>(Keyboard.FocusedElement as DependencyObject) is { } editor)
                            editor.Clear();
                    }, DispatcherPriority.Input);
                    e.Handled = true;
                }
                return;
            }
            if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right))
                return;

            var focused = Keyboard.FocusedElement as DependencyObject;
            var combo = FindAncestor<ComboBox>(focused);

            // Nếu ComboBox màu đang xổ xuống thì ↑/↓ vẫn dùng để chọn màu.
            if (combo is not null &&
                combo.IsDropDownOpen &&
                e.Key is Key.Up or Key.Down)
                return;

            if (MoveAndEdit(grid, e.Key, preferredEditableDisplayIndex))
                e.Handled = true;
        };
    }

    private static bool MoveAndEdit(
        DataGrid grid,
        Key key,
        int preferredEditableDisplayIndex)
    {
        if (grid.Items.Count == 0 || grid.Columns.Count == 0)
            return false;

        var currentItem = grid.CurrentItem;
        var currentColumn = grid.CurrentCell.Column;

        var rowIndex = currentItem is null
            ? -1
            : grid.Items.IndexOf(currentItem);

        if (rowIndex < 0)
        {
            rowIndex = grid.SelectedIndex;
            if (rowIndex < 0 || rowIndex >= grid.Items.Count)
                rowIndex = 0;
        }

        var displayIndex = currentColumn?.DisplayIndex
                           ?? preferredEditableDisplayIndex;

        // Chốt dữ liệu vừa nhập trước khi điều hướng.
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);

        var targetRow = rowIndex;
        var targetDisplayIndex = displayIndex;

        switch (key)
        {
            case Key.Up:
                targetRow--;
                break;
            case Key.Down:
                targetRow++;
                break;
            case Key.Left:
                targetDisplayIndex--;
                break;
            case Key.Right:
                targetDisplayIndex++;
                break;
        }

        if (key is Key.Left or Key.Right)
        {
            var step = key == Key.Left ? -1 : 1;
            while (targetDisplayIndex >= 0 && targetDisplayIndex < grid.Columns.Count)
            {
                var candidate = grid.Columns.FirstOrDefault(column => column.DisplayIndex == targetDisplayIndex);
                if (candidate is { IsReadOnly: false }) break;
                targetDisplayIndex += step;
            }
        }

        if (targetRow < 0 ||
            targetRow >= grid.Items.Count ||
            targetDisplayIndex < 0 ||
            targetDisplayIndex >= grid.Columns.Count)
            return false;

        var targetItem = grid.Items[targetRow];
        var targetColumn = grid.Columns
            .FirstOrDefault(c => c.DisplayIndex == targetDisplayIndex);

        if (targetColumn is null)
            return false;

        grid.CurrentCell = new DataGridCellInfo(targetItem, targetColumn);
        if (grid.SelectionUnit == DataGridSelectionUnit.Cell)
        {
            grid.SelectedCells.Clear();
            grid.SelectedCells.Add(grid.CurrentCell);
        }
        grid.ScrollIntoView(targetItem, targetColumn);
        grid.Focus();

        grid.Dispatcher.BeginInvoke(
            () =>
            {
                if (!targetColumn.IsReadOnly)
                {
                    grid.BeginEdit();
                    grid.Dispatcher.BeginInvoke(
                        () => FocusEditor(grid, targetItem, targetColumn),
                        DispatcherPriority.Input);
                }
                else
                {
                    GetCell(grid, targetItem, targetColumn)?.Focus();
                }
            },
            DispatcherPriority.Input);

        return true;
    }

    private static void FocusEditor(
        DataGrid grid,
        object item,
        DataGridColumn column)
    {
        var cell = GetCell(grid, item, column);
        if (cell is null) return;

        var textBox = FindVisualChild<TextBox>(cell);
        if (textBox is not null)
        {
            textBox.Focus();
            textBox.SelectAll();
            return;
        }

        var combo = FindVisualChild<ComboBox>(cell);
        if (combo is not null)
        {
            combo.Focus();

            if (combo.IsEditable)
            {
                combo.ApplyTemplate();
                if (combo.Template.FindName(
                        "PART_EditableTextBox",
                        combo) is TextBox editor)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            }
        }
    }

    private static DataGridCell? GetCell(
        DataGrid grid,
        object item,
        DataGridColumn column)
    {
        grid.ScrollIntoView(item, column);
        grid.UpdateLayout();

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

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
