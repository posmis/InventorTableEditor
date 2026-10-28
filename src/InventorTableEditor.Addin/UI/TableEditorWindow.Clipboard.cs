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

/// <summary>Окно редактора: буфер обмена и контекстное меню.</summary>
internal sealed partial class TableEditorWindow
{
    // ---------- Буфер обмена ----------

    /// <summary>
    /// Копирование, как в Excel и Word: выделенные ячейки — таблицей, выделенный текст ячейки — с форматом,
    /// без выделения — вся текущая ячейка.
    /// </summary>
    private void CopyToClipboard(bool cut)
    {
        if (_session.Range is { } range)
        {
            if (ClipboardData.SetCells(Model, range) && cut)
                ClearCells(range);
            return;
        }

        if (ActiveEditor is not { } editor)
            return;

        if (editor.Buffer.HasSelection)
        {
            if (ClipboardData.SetRuns(editor.Buffer.SelectedRuns()) && cut)
                editor.Edit(b => b.DeleteSelection());
        }
        else if (ClipboardData.SetCells(Model, new CellRange(_session.Row, _session.Column, _session.Row, _session.Column)) && cut)
        {
            editor.EditAll(b => b.DeleteSelection());
        }
    }

    /// <summary>
    /// Вставка: одна ячейка из буфера — в текст текущей ячейки на место курсора (с форматом и индексами);
    /// несколько ячеек или выделение в таблице — по ячейкам, начиная с левой верхней, как в Excel.
    /// </summary>
    private void PasteFromClipboard()
    {
        if (ClipboardData.GetCells(Model.Style.Italic) is not { } cells)
            return;

        if (_session.Range == null && cells.Count == 1 && cells[0].Count == 1)
        {
            ActiveEditor?.Edit(b => b.InsertRuns(cells[0][0].Runs));
            return;
        }

        if (!_session.PasteCells(cells))
            return;

        // Сетка перестраивается (могли добавиться строки и столбцы), вставленные ячейки остаются выделенными.
        var pasted = _session.Range;
        RebuildGrid();
        if (pasted is { } range)
        {
            _session.BeginSelection(range.Top, range.Left);
            _session.ExtendSelection(range.Bottom, range.Right);
            ShowSelection();
        }
    }

    /// <summary>Очищает текст выделенных ячеек — один шаг отмены.</summary>
    private void ClearCells(CellRange range) => _session.Batch(() =>
    {
        foreach (var editor in EditorsIn(range))
            editor.EditAll(b => b.DeleteSelection());
    });

    // ---------- Контекстное меню ----------

    /// <summary>
    /// Правая кнопка, как в Excel: внутри выделенных ячеек выделение остаётся, вне — ячейка под мышью
    /// становится текущей.
    /// </summary>
    private void OnGridRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CellAt(e.GetPosition(_grid)) is not { } cell)
            return;

        if (_session.Range?.Contains(cell.Row, cell.Column) == true)
            return;

        ClearRange();
        if (_editors.TryGetValue(Model.AnchorOf(cell.Row, cell.Column), out var editor))
            editor.Focus();
    }

    /// <summary>Пункты меню собираются при каждом открытии — с актуальной доступностью.</summary>
    private void FillContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var canCopy = _session.Range != null || ActiveEditor != null;

        Item("Вырезать", "Ctrl+X", null, canCopy, () => CopyToClipboard(cut: true));
        Item("Копировать", "Ctrl+C", null, canCopy, () => CopyToClipboard(cut: false));
        Item("Вставить", "Ctrl+V", null, ClipboardData.HasData(), PasteFromClipboard);
        menu.Items.Add(new Separator());
        Item("Объединить ячейки", null, Icons.Merge(), _session.CanMerge, () => RunStructure(() => _session.MergeSelection()));
        Item("Разделить ячейку", null, Icons.Split(), _session.CanSplit, () => RunStructure(() => _session.SplitCurrent()));
        menu.Items.Add(new Separator());
        Item("Вставить строку сверху", null, Icons.Row(0, Icons.Badge.Add), true, () => RunStructure(_session.InsertRowAbove));
        Item("Вставить строку снизу", null, Icons.Row(2, Icons.Badge.Add), true, () => RunStructure(_session.InsertRowBelow));
        Item("Вставить столбец слева", null, Icons.Column(0, Icons.Badge.Add), true, () => RunStructure(_session.InsertColumnLeft));
        Item("Вставить столбец справа", null, Icons.Column(2, Icons.Badge.Add), true, () => RunStructure(_session.InsertColumnRight));
        Item("Удалить строку", null, Icons.Row(1, Icons.Badge.Remove), Model.RowCount > 1, () => RunStructure(_session.DeleteRow));
        Item("Удалить столбец", null, Icons.Column(1, Icons.Badge.Remove), Model.ColumnCount > 1, () => RunStructure(_session.DeleteColumn));
        menu.Items.Add(new Separator());
        Item("Выделить строку", null, Icons.Row(1, Icons.Badge.None), true, SelectRow);
        Item("Выделить столбец", null, Icons.Column(1, Icons.Badge.None), true, SelectColumn);
        menu.Items.Add(new Separator());
        var wrap = new MenuItem { Header = "Переносить текст", IsCheckable = true, IsChecked = _session.WrapText, Icon = Icons.WrapText() };
        wrap.Click += (_, _) => ToggleWrap();
        menu.Items.Add(wrap);

        void Item(string header, string? gesture, UIElement? icon, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty, Icon = icon, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }
}
