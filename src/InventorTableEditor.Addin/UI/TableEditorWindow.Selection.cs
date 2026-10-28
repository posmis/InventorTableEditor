using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>Окно редактора: выделение ячеек мышью, перетаскивание границ, клавиши окна.</summary>
internal sealed partial class TableEditorWindow
{
    // ---------- Выделение ячеек ----------

    /// <summary>Щелчок по ячейке начинает выделение; Shift+щелчок — прямоугольник от текущей ячейки, как в Excel.</summary>
    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_grid);
        if (BorderAt(point) is { } border)
        {
            // Правая граница меняет ширину только этого столбца, нижняя — высоту только этой строки
            // (от фактической высоты: строка могла вырасти под текст).
            _resize = border.Row
                ? (true, border.Index, point.Y, RowHeight(border.Index))
                : (false, border.Index, point.X, Model.ColumnWidths[border.Index]);
            _resizeRecorded = false;
            _grid.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (CellAt(point) is not { } cell)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && Model.AnchorOf(cell.Row, cell.Column) != (_session.Row, _session.Column))
        {
            _session.ExtendSelection(cell.Row, cell.Column, fromCurrent: true);
            ShowSelection();
            // Курсор остаётся в начальной ячейке, текст в ячейке под мышью не выделяется.
            e.Handled = true;
            return;
        }

        _session.BeginSelection(cell.Row, cell.Column);
        ShowSelection();
        _draggingRange = true;
    }

    /// <summary>Протягивание мышью за пределы начальной ячейки выделяет прямоугольник ячеек.</summary>
    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_grid);
        if (_resize is { } resize)
        {
            Resize(resize, point);
            e.Handled = true;
            return;
        }

        // У границы — стрелка размера поверх курсора ячейки.
        var border = e.LeftButton != MouseButtonState.Pressed ? BorderAt(point) : null;
        _grid.Cursor = border == null ? null : border.Value.Row ? Cursors.SizeNS : Cursors.SizeWE;
        _grid.ForceCursor = border != null;

        if (_draggingRange && e.LeftButton == MouseButtonState.Pressed && CellAt(e.GetPosition(_grid)) is { } cell
            && _session.ExtendSelection(cell.Row, cell.Column))
            ShowSelection();
    }

    private void OnGridMouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingRange = false;
        if (_resize == null)
            return;

        _grid.ReleaseMouseCapture();
        _resize = null;
        UpdateSizeBoxes();
        FocusActiveEditor();
        e.Handled = true;
    }

    /// <summary>Размер по сдвигу мыши: шаг 1 мм; таблица растёт или сужается справа или снизу.</summary>
    private void Resize((bool Row, int Index, double Start, double StartSize) resize, Point point)
    {
        var delta = ((resize.Row ? point.Y : point.X) - resize.Start) / PxPerMm;
        var size = MmParser.SnapToMillimetre(resize.StartSize, delta);
        var current = resize.Row ? Model.RowHeights[resize.Index] : Model.ColumnWidths[resize.Index];
        if (Math.Abs(size - current) < 1e-9)
            return;

        if (!_resizeRecorded)
        {
            // Одно перетаскивание — один шаг отмены.
            _session.Record();
            _resizeRecorded = true;
        }

        if (resize.Row)
        {
            _session.SetRowHeight(resize.Index, size, record: false);
        }
        else
        {
            _session.SetColumnWidth(resize.Index, size, record: false);
            _grid.ColumnDefinitions[resize.Index].Width = new GridLength(size * PxPerMm);
        }

        RefreshLayout();
    }

    /// <summary>Фактическая высота строки (с учётом текста), мм.</summary>
    private double RowHeight(int row) =>
        _layout != null ? _layout.RowEdges[row + 1] - _layout.RowEdges[row] : Model.RowHeights[row];

    /// <summary>
    /// Граница под точкой (координаты сетки без масштаба): правая граница столбца или нижняя граница строки.
    /// Внутренние границы объединённых ячеек не рисуются — за них не потянуть. null — не у границы.
    /// </summary>
    private (bool Row, int Index)? BorderAt(Point point)
    {
        var grip = BorderGrip / _zoomTransform.ScaleX;
        var columns = _grid.ColumnDefinitions.Select(c => c.ActualWidth).ToList();
        var rows = _grid.RowDefinitions.Select(r => r.ActualHeight).ToList();
        var row = Index(point.Y, rows, grip);
        var column = Index(point.X, columns, grip);

        var x = 0.0;
        for (var c = 0; c < columns.Count; c++)
        {
            x += columns[c];
            if (Math.Abs(point.X - x) <= grip && row >= 0 && Visible(row, c, row, c + 1, c == columns.Count - 1))
                return (false, c);
        }

        var y = 0.0;
        for (var r = 0; r < rows.Count; r++)
        {
            y += rows[r];
            if (Math.Abs(point.Y - y) <= grip && column >= 0 && Visible(r, column, r + 1, column, r == rows.Count - 1))
                return (true, r);
        }

        return null;

        // Граница между двумя клетками видна, если они в разных ячейках (или это край таблицы).
        bool Visible(int r1, int c1, int r2, int c2, bool outerEdge) =>
            outerEdge || Model.GetMerge(r1, c1) != Model.GetMerge(r2, c2);

        // Строка или столбец под координатой с запасом на захват у краёв таблицы; −1 — вне таблицы.
        static int Index(double position, List<double> sizes, double margin)
        {
            var edge = 0.0;
            for (var i = 0; i < sizes.Count; i++)
            {
                edge += sizes[i];
                if (position < edge)
                    return position >= -margin ? i : -1;
            }

            return position <= edge + margin ? sizes.Count - 1 : -1;
        }
    }

    private void ClearRange()
    {
        if (_session.ClearSelection())
            ShowSelection();
    }

    /// <summary>Подсветка выделенных ячеек по выделению сеанса.</summary>
    private void ShowSelection()
    {
        foreach (var ((row, column), editor) in _editors)
            editor.InRange = _session.Range?.Contains(row, column) ?? false;
        UpdateCommandStates();
    }

    /// <summary>Кнопки «Объединить» и «Разделить» активны, только когда действие возможно.</summary>
    private void UpdateCommandStates()
    {
        if (_mergeButton != null)
            _mergeButton.IsEnabled = _session.CanMerge;
        if (_splitButton != null)
            _splitButton.IsEnabled = _session.CanSplit;
    }

    /// <summary>Ячейка под точкой сетки (координаты сетки без масштаба); null — вне таблицы.</summary>
    private (int Row, int Column)? CellAt(Point point)
    {
        int Index(double position, IReadOnlyList<double> sizes)
        {
            var edge = 0.0;
            for (var i = 0; i < sizes.Count; i++)
            {
                edge += sizes[i];
                if (position < edge)
                    return i;
            }

            return -1;
        }

        var column = Index(point.X, _grid.ColumnDefinitions.Select(c => c.ActualWidth).ToList());
        var row = Index(point.Y, _grid.RowDefinitions.Select(r => r.ActualHeight).ToList());
        return row < 0 || column < 0 || point.X < 0 || point.Y < 0 ? null : (row, column);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key == Key.D0 && ctrl)
        {
            SetZoom(1);
            e.Handled = true;
            return;
        }

        // Курсор переставлен клавишами — следующий набор начнёт новый шаг отмены, как в Word.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Tab)
            _session.BreakMerge();

        // Отмена и повтор — для всей таблицы, а не для одной ячейки.
        if (ctrl && (e.Key == Key.Z || e.Key == Key.Y))
        {
            if (e.Key == Key.Y || shift)
                Redo();
            else
                Undo();
            e.Handled = true;
            return;
        }

        // Буфер обмена — с форматом и индексами, обмен с Word и Excel. В полях ввода (шрифт, размеры) — их собственный.
        if (ctrl && e.Key is Key.C or Key.X or Key.V && Keyboard.FocusedElement is not TextBoxBase)
        {
            if (e.Key == Key.V)
                PasteFromClipboard();
            else
                CopyToClipboard(cut: e.Key == Key.X);
            e.Handled = true;
            SyncFormatBar();
            return;
        }

        if (_session.Range is not { } range)
            return;

        switch (e.Key)
        {
            case Key.Delete:
                // Delete очищает все выделенные ячейки, как в Excel.
                ClearCells(range);
                e.Handled = true;
                break;
            case Key.B when ctrl:
                ToggleFormat(f => f.Bold, (f, on) => f.Bold = on);
                e.Handled = true;
                break;
            case Key.I when ctrl:
                ToggleFormat(f => f.Italic, (f, on) => f.Italic = on);
                e.Handled = true;
                break;
            case Key.U when ctrl:
                ToggleFormat(f => f.Underline, (f, on) => f.Underline = on);
                e.Handled = true;
                break;
            case Key.OemPlus when ctrl:
                var script = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ScriptPosition.Superscript : ScriptPosition.Subscript;
                ToggleFormat(f => f.Script == script, (f, on) => f.Script = on ? script : ScriptPosition.Normal);
                e.Handled = true;
                break;
            case Key.Space when ctrl:
                EditSelection(b => b.ResetScript());
                e.Handled = true;
                break;
            case Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl:
                break;
            default:
                // Любая другая клавиша (стрелки, ввод) — снова работа с одной ячейкой.
                if (!ctrl)
                    ClearRange();
                break;
        }

        if (e.Handled)
            SyncFormatBar();
    }
}
