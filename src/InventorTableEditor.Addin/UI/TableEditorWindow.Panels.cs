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

/// <summary>Окно редактора: левая панель структуры, поля размеров, строка состояния.</summary>
internal sealed partial class TableEditorWindow
{
    // ---------- Левая панель ----------

    /// <summary>Панель структуры: подпись и ряд кнопок, как на панели таблицы в КОМПАС-3D.</summary>
    private Border BuildToolPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 2, 12, 12) };

        panel.Children.Add(new TextBlock { Text = "СТРУКТУРА", Style = ThemeStyle("SectionHeader") });
        panel.Children.Add(LabeledRow("Ячейки:",
            _mergeButton = Structure(Icons.Merge(), "Объединить выделенные ячейки\n(выделите ячейки мышью или Shift+щелчком)", () => _session.MergeSelection()),
            _splitButton = Structure(Icons.Split(), "Разделить объединённую ячейку", () => _session.SplitCurrent())));
        panel.Children.Add(LabeledRow("Столбец:",
            Flat(Icons.Column(1, Icons.Badge.None), "Выделить столбец", SelectColumn),
            Structure(Icons.Column(0, Icons.Badge.Add), "Вставить столбец слева", _session.InsertColumnLeft),
            Structure(Icons.Column(2, Icons.Badge.Add), "Вставить столбец справа", _session.InsertColumnRight),
            Structure(Icons.Column(1, Icons.Badge.Remove), "Удалить столбец", _session.DeleteColumn)));
        panel.Children.Add(LabeledRow("Строка:",
            Flat(Icons.Row(1, Icons.Badge.None), "Выделить строку", SelectRow),
            Structure(Icons.Row(0, Icons.Badge.Add), "Вставить строку сверху", _session.InsertRowAbove),
            Structure(Icons.Row(2, Icons.Badge.Add), "Вставить строку снизу", _session.InsertRowBelow),
            Structure(Icons.Row(1, Icons.Badge.Remove), "Удалить строку", _session.DeleteRow)));

        panel.Children.Add(new TextBlock { Text = "РАЗМЕРЫ, ММ", Style = ThemeStyle("SectionHeader") });
        panel.Children.Add(LabeledRow("Ширина:", SizeField(_columnWidthBox)));
        panel.Children.Add(LabeledRow("Высота:", SizeField(_rowHeightBox)));

        panel.Children.Add(new TextBlock { Text = "ТАБЛИЦА", Style = ThemeStyle("SectionHeader") });
        _headerBox.Click += (_, _) =>
        {
            _session.SetHeader(_headerBox.IsChecked == true);
            RebuildGrid();
        };
        panel.Children.Add(_headerBox);

        _columnWidthBox.ToolTip = "Ширина текущей ячейки, мм.\nУ объединённой — полная; при вводе меняется её правый столбец.";
        _rowHeightBox.ToolTip = "Высота текущей ячейки, мм.\nЕсли текст не помещается, строка выше заданной —\nздесь показана фактическая высота.\nУ объединённой — полная; при вводе меняется её нижняя строка.";
        // При уходе из поля размера фокус не перехватывается: пользователь мог перейти в соседнее поле.
        _columnWidthBox.LostFocus += (_, _) => ApplySizeBoxes(focusEditor: false);
        _rowHeightBox.LostFocus += (_, _) => ApplySizeBoxes(focusEditor: false);
        _columnWidthBox.KeyDown += SizeBoxKeyDown;
        _rowHeightBox.KeyDown += SizeBoxKeyDown;

        return new Border
        {
            Width = 196,
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = panel,
        };
    }

    private StackPanel LabeledRow(string label, params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Style = ThemeStyle("RowLabel") });
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private UIElement SizeField(TextBox box)
    {
        box.Style = ThemeStyle("FieldBox");
        return box;
    }

    private Button Structure(UIElement icon, string tooltip, Action action)
    {
        var button = new Button { Content = icon, ToolTip = tooltip, Style = ThemeStyle("FlatButton"), Margin = new Thickness(0, 0, 2, 0) };
        button.Click += (_, _) => RunStructure(action);
        return button;
    }

    /// <summary>Изменение структуры таблицы: действие, сообщение об ошибке, перестроение сетки.</summary>
    private void RunStructure(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        RebuildGrid();
    }

    private void SelectColumn()
    {
        _session.SelectColumn();
        ShowSelection();
        FocusActiveEditor();
    }

    private void SelectRow()
    {
        _session.SelectRow();
        ShowSelection();
        FocusActiveEditor();
    }

    // ---------- Размеры ----------

    private void SizeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySizeBoxes();
            e.Handled = true;
        }
    }

    private void ApplySizeBoxes(bool focusEditor = true)
    {
        // Применяется только то, что пользователь изменил: в поле высоты показана фактическая высота,
        // и если её записать как заданную, строка не уменьшится после удаления текста.
        var changed = false;
        if (_columnWidthBox.Text != _shownWidth && MmParser.TryParseCellSize(_columnWidthBox.Text, out var width))
            changed |= _session.SetColumnWidth(width);
        if (_rowHeightBox.Text != _shownHeight && MmParser.TryParseCellSize(_rowHeightBox.Text, out var height))
            changed |= _session.SetRowHeight(height);

        if (changed)
            RebuildGrid(focusEditor);
        else
            UpdateSizeBoxes();
    }

    /// <summary>
    /// Ширина текущего столбца и фактическая высота текущей строки (с учётом текста, который не поместился
    /// в заданную высоту). Поле, в котором пользователь сейчас печатает, не перезаписывается.
    /// </summary>
    private void UpdateSizeBoxes()
    {
        // У объединённой ячейки — полная ширина и высота (сумма её столбцов и строк).
        var merge = Model.GetMerge(_session.Row, _session.Column);
        var height = _layout != null && merge.Bottom + 1 < _layout.RowEdges.Count
            ? _layout.RowEdges[merge.Bottom + 1] - _layout.RowEdges[merge.Top]
            : Enumerable.Range(merge.Top, merge.Bottom - merge.Top + 1).Sum(r => Model.RowHeights[r]);

        _shownWidth = MmParser.Format(_session.CurrentWidth);
        _shownHeight = MmParser.Format(height);
        if (!_columnWidthBox.IsKeyboardFocusWithin)
            _columnWidthBox.Text = _shownWidth;
        if (!_rowHeightBox.IsKeyboardFocusWithin)
            _rowHeightBox.Text = _shownHeight;
    }

    // ---------- Строка состояния ----------

    /// <summary>Подсказки и масштаб слева, кнопки диалога справа.</summary>
    private Border BuildFooter(string acceptText)
    {
        var accept = new Button { Content = acceptText, Style = ThemeStyle("PrimaryButton"), IsDefault = false, Margin = new Thickness(0, 0, 8, 0) };
        accept.Click += (_, _) =>
        {
            ApplySizeBoxes();
            // Размер окна и масштаб сохраняются вместе с таблицей — при следующем открытии окно будет таким же.
            Model.Editor = CurrentView();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Отмена", Style = ThemeStyle("SecondaryButton"), IsCancel = true };

        _zoomSlider.ValueChanged += (_, e) => SetZoom(e.NewValue / 100);
        var zoomOut = Flat(new TextBlock { Text = "−", FontSize = 14 }, "Уменьшить (Ctrl+колесо вниз)", () => SetZoom(Zoom.Next(_zoomTransform.ScaleX, -1)));
        var zoomIn = Flat(new TextBlock { Text = "+", FontSize = 14 }, "Увеличить (Ctrl+колесо вверх)", () => SetZoom(Zoom.Next(_zoomTransform.ScaleX, +1)));
        var zoomReset = Flat(_zoomText, "Натуральный размер, 100 % (Ctrl+0)", () => SetZoom(1));

        var hint = new TextBlock
        {
            Text = "Протяните мышью или Shift+щелчок — несколько ячеек · Ctrl+колесо — масштаб · Tab — следующая ячейка",
            Foreground = ThemeBrush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var bar = new DockPanel { LastChildFill = true };
        var buttons = Row(accept, cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var zoom = Row(zoomOut, _zoomSlider, zoomIn, zoomReset);
        zoom.Margin = new Thickness(0, 0, 24, 0);
        DockPanel.SetDock(zoom, Dock.Right);
        bar.Children.Add(zoom);
        bar.Children.Add(hint);

        return new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = bar,
        };
    }

    private void SetZoom(double zoom)
    {
        zoom = Zoom.Clamp(zoom);
        _zoomTransform.ScaleX = zoom;
        _zoomTransform.ScaleY = zoom;
        _zoomText.Text = $"{zoom * 100:0} %";
        if (Math.Abs(_zoomSlider.Value - zoom * 100) > 0.5)
            _zoomSlider.Value = zoom * 100;
    }

    /// <summary>Текущие размер окна (без развёртывания на весь экран) и масштаб таблицы.</summary>
    private EditorView CurrentView()
    {
        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized && !RestoreBounds.IsEmpty ? RestoreBounds : new Rect(0, 0, ActualWidth, ActualHeight);
        var view = new EditorView
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = maximized,
            Zoom = _zoomTransform.ScaleX,
        };
        view.Normalize();
        return view;
    }
}
