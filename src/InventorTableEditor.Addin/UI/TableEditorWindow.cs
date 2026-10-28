using System;
using System.Collections.Generic;
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

/// <summary>
/// Окно редактора таблицы. Сверху — форматирование текста, слева — структура и размеры,
/// в центре — сетка ячеек. Каждая ячейка — <see cref="CellEditor"/>: текст и при наборе выглядит как на чертеже.
/// Работает с копией модели; изменения возвращаются только по кнопке подтверждения.
/// </summary>
internal sealed class TableEditorWindow : Window
{
    /// <summary>Масштаб отображения: пикселей на мм.</summary>
    private const double PxPerMm = 96.0 / 25.4 * 1.5;

    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA));

    private readonly Grid _grid = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBox _columnWidthBox = new() { Width = 60 };
    private readonly TextBox _rowHeightBox = new() { Width = 60 };
    private readonly CheckBox _headerBox = new() { Content = "Заголовки столбцов", Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _fontBox = new() { Width = 170, IsEditable = true, ToolTip = "Шрифт" };
    private readonly ComboBox _sizeBox = new() { Width = 60, IsEditable = true, ToolTip = "Высота шрифта, мм" };
    private readonly Dictionary<(int Row, int Column), CellEditor> _editors = new();
    private readonly ITextMetrics _metrics;

    private ToggleButton? _boldButton;
    private ToggleButton? _italicButton;
    private ToggleButton? _underlineButton;
    private ToggleButton? _superscriptButton;
    private ToggleButton? _subscriptButton;

    private int _row;
    private int _column;

    /// <summary>Списки шрифта и размера обновляются из формата под курсором — в это время их события игнорируются.</summary>
    private bool _syncingFormat;

    public TableEditorWindow(TableModel model, bool isNew)
    {
        Model = TableModel.FromJson(model.ToJson());
        _metrics = new WpfTextMetrics(Model.Style);
        EditorFonts.EmPerTextHeight = FontMetrics.Get(Model.Style.FontName)?.EmPerCapHeight ?? 1.4;
        Title = isNew ? "Новая таблица" : "Редактирование таблицы";
        Width = 1000;
        Height = 650;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Content = BuildLayout(isNew ? "Вставить в чертёж" : "Применить");
        RebuildGrid();
    }

    public TableModel Model { get; }

    private CellEditor? ActiveEditor => _editors.TryGetValue((_row, _column), out var editor) ? editor : null;

    private UIElement BuildLayout(string acceptText)
    {
        var root = new DockPanel { Margin = new Thickness(8) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var accept = new Button { Content = acceptText, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        accept.Click += (_, _) =>
        {
            ApplySizeBoxes();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(12, 4, 12, 4) };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var formatBar = BuildFormatBar();
        DockPanel.SetDock(formatBar, Dock.Top);
        root.Children.Add(formatBar);

        var tools = BuildToolPanel();
        DockPanel.SetDock(tools, Dock.Left);
        root.Children.Add(tools);

        var canvas = new Border { Background = Brushes.WhiteSmoke, Padding = new Thickness(16), Child = _grid };
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvas,
        });

        return root;
    }

    /// <summary>Верхняя панель: форматирование выделенного текста (или текста, который будет набран).</summary>
    private ToolBarTray BuildFormatBar()
    {
        var bar = new ToolBar();
        SetupFontBoxes();
        bar.Items.Add(_fontBox);
        bar.Items.Add(_sizeBox);
        bar.Items.Add(new Separator());
        bar.Items.Add(_boldButton = ToggleFormatButton("Ж", "Жирный (Ctrl+B)", b => b.ToggleBold(), b => b.FontWeight = FontWeights.Bold));
        bar.Items.Add(_italicButton = ToggleFormatButton("К", "Наклонный (Ctrl+I)", b => b.ToggleItalic(), b => b.FontStyle = FontStyles.Italic));
        bar.Items.Add(_underlineButton = ToggleFormatButton("Ч", "Подчёркнутый (Ctrl+U)", b => b.ToggleUnderline(),
            b => b.Content = new TextBlock { Text = "Ч", TextDecorations = TextDecorations.Underline }));
        bar.Items.Add(new Separator());
        bar.Items.Add(_superscriptButton = ToggleFormatButton("x²", "Надстрочный (Ctrl+Shift+=). Обычный текст — Ctrl+Пробел",
            b => b.ToggleScript(ScriptPosition.Superscript), null));
        bar.Items.Add(_subscriptButton = ToggleFormatButton("x₂", "Подстрочный (Ctrl+=). Обычный текст — Ctrl+Пробел",
            b => b.ToggleScript(ScriptPosition.Subscript), null));

        bar.Items.Add(new Separator());
        bar.Items.Add(AlignmentButton("По левому краю", AlignmentIcons.Horizontal(HorizontalTextAlignment.Left),
            () => SetHorizontalAlignment(HorizontalTextAlignment.Left)));
        bar.Items.Add(AlignmentButton("По центру", AlignmentIcons.Horizontal(HorizontalTextAlignment.Center),
            () => SetHorizontalAlignment(HorizontalTextAlignment.Center)));
        bar.Items.Add(AlignmentButton("По правому краю", AlignmentIcons.Horizontal(HorizontalTextAlignment.Right),
            () => SetHorizontalAlignment(HorizontalTextAlignment.Right)));
        bar.Items.Add(new Separator());
        bar.Items.Add(AlignmentButton("По верхнему краю", AlignmentIcons.Vertical(VerticalTextAlignment.Top),
            () => SetVerticalAlignment(VerticalTextAlignment.Top)));
        bar.Items.Add(AlignmentButton("По середине", AlignmentIcons.Vertical(VerticalTextAlignment.Middle),
            () => SetVerticalAlignment(VerticalTextAlignment.Middle)));
        bar.Items.Add(AlignmentButton("По нижнему краю", AlignmentIcons.Vertical(VerticalTextAlignment.Bottom),
            () => SetVerticalAlignment(VerticalTextAlignment.Bottom)));
        return new ToolBarTray { Margin = new Thickness(0, 0, 0, 8), ToolBars = { bar } };
    }

    /// <summary>Кнопка-переключатель: нажата, если формат включён у текста под курсором.</summary>
    private ToggleButton ToggleFormatButton(string text, string tooltip, Action<CellTextBuffer> action, Action<ToggleButton>? decorate)
    {
        var button = new ToggleButton
        {
            Content = text,
            ToolTip = tooltip,
            MinWidth = 28,
            FontSize = 14,
            // Кнопка не забирает фокус, чтобы курсор и выделение в ячейке сохранились.
            Focusable = false,
        };
        decorate?.Invoke(button);
        button.Click += (_, _) =>
        {
            ActiveEditor?.Edit(action);
            // Состояние кнопки — по фактическому формату текста, а не по щелчку.
            SyncFormatBar();
        };
        return button;
    }

    private static Button AlignmentButton(string tooltip, UIElement icon, Action action)
    {
        var button = new Button { Content = icon, ToolTip = tooltip, MinWidth = 28, Focusable = false };
        button.Click += (_, _) => action();
        return button;
    }

    private void SetupFontBoxes()
    {
        foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).Append(Model.Style.FontName).Distinct().OrderBy(n => n))
            _fontBox.Items.Add(name);
        foreach (var size in new[] { 2.5, 3.5, 5, 7, 10, 14 })
            _sizeBox.Items.Add(size.ToString("0.#", CultureInfo.CurrentCulture));

        _fontBox.SelectionChanged += (_, _) =>
        {
            if (!_syncingFormat && _fontBox.SelectedItem is string name)
                ApplyFont(name);
        };
        _sizeBox.SelectionChanged += (_, _) =>
        {
            if (!_syncingFormat && _sizeBox.SelectedItem is string size)
                ApplySize(size);
        };
        _fontBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyFont(_fontBox.Text);
                e.Handled = true;
            }
        };
        _sizeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplySize(_sizeBox.Text);
                e.Handled = true;
            }
        };
    }

    private void ApplyFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        ActiveEditor?.Edit(b => b.SetFontName(name.Trim()));
        FocusActiveEditor();
    }

    private void ApplySize(string text)
    {
        if (!TryParseMm(text, 0.5, 100, out var sizeMm))
            return;

        ActiveEditor?.Edit(b => b.SetFontSize(sizeMm));
        FocusActiveEditor();
    }

    private void SetHorizontalAlignment(HorizontalTextAlignment alignment)
    {
        Model.GetCell(_row, _column).HorizontalAlignment = alignment;
        RefreshLayout();
        FocusActiveEditor();
    }

    private void SetVerticalAlignment(VerticalTextAlignment alignment)
    {
        Model.GetCell(_row, _column).VerticalAlignment = alignment;
        RefreshLayout();
        FocusActiveEditor();
    }

    private void FocusActiveEditor()
    {
        var editor = ActiveEditor;
        if (editor != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => editor.Focus()));
    }

    /// <summary>Показывает на панели шрифт, размер и включённые форматы текста под курсором.</summary>
    private void SyncFormatBar()
    {
        var editor = ActiveEditor;
        if (editor == null)
            return;

        var format = editor.Buffer.CurrentFormat;
        _syncingFormat = true;
        try
        {
            _fontBox.Text = format.FontName ?? Model.Style.FontName;
            _sizeBox.Text = (format.FontSizeMm ?? Model.Style.FontSizeMm).ToString("0.#", CultureInfo.CurrentCulture);
            SetChecked(_boldButton, format.Bold);
            SetChecked(_italicButton, format.Italic);
            SetChecked(_underlineButton, format.Underline);
            SetChecked(_superscriptButton, format.Script == ScriptPosition.Superscript);
            SetChecked(_subscriptButton, format.Script == ScriptPosition.Subscript);
        }
        finally
        {
            _syncingFormat = false;
        }
    }

    private static void SetChecked(ToggleButton? button, bool value)
    {
        if (button != null)
            button.IsChecked = value;
    }

    private StackPanel BuildToolPanel()
    {
        var panel = new StackPanel { Width = 190, Margin = new Thickness(0, 0, 8, 0) };

        panel.Children.Add(Group("Столбец",
            ToolButton("Вставить слева", () => Model.InsertColumn(_column)),
            ToolButton("Вставить справа", () => { Model.InsertColumn(_column + 1); _column++; }),
            ToolButton("Удалить", () => Model.RemoveColumn(_column))));

        panel.Children.Add(Group("Строка",
            ToolButton("Вставить сверху", () => Model.InsertRow(_row)),
            ToolButton("Вставить снизу", () => { Model.InsertRow(_row + 1); _row++; }),
            ToolButton("Удалить", () => Model.RemoveRow(_row))));

        panel.Children.Add(Group("Размеры, мм",
            SizeRow("Ширина столбца", _columnWidthBox),
            SizeRow("Высота строки", _rowHeightBox)));

        _headerBox.Click += (_, _) =>
        {
            Model.SetHeader(_headerBox.IsChecked == true);
            _row = 0;
            RebuildGrid();
        };
        panel.Children.Add(_headerBox);

        // При уходе из поля размера фокус не перехватывается: пользователь мог перейти в соседнее поле.
        _columnWidthBox.LostFocus += (_, _) => ApplySizeBoxes(focusEditor: false);
        _rowHeightBox.LostFocus += (_, _) => ApplySizeBoxes(focusEditor: false);
        _columnWidthBox.KeyDown += SizeBoxKeyDown;
        _rowHeightBox.KeyDown += SizeBoxKeyDown;

        return panel;
    }

    private static GroupBox Group(string header, params UIElement[] children)
    {
        var stack = new StackPanel { Margin = new Thickness(4) };
        foreach (var child in children)
            stack.Children.Add(child);
        return new GroupBox { Header = header, Content = stack, Margin = new Thickness(0, 0, 0, 8) };
    }

    private Button ToolButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4, 2, 4, 2) };
        button.Click += (_, _) =>
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
        };
        return button;
    }

    private static DockPanel SizeRow(string label, TextBox box)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

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
        var changed = false;
        if (TryParseMm(_columnWidthBox.Text, out var width) && width != Model.ColumnWidths[_column])
        {
            Model.ColumnWidths[_column] = width;
            changed = true;
        }

        if (TryParseMm(_rowHeightBox.Text, out var height) && height != Model.RowHeights[_row])
        {
            Model.RowHeights[_row] = height;
            changed = true;
        }

        if (changed)
            RebuildGrid(focusEditor);
    }

    private static bool TryParseMm(string text, out double value) => TryParseMm(text, 2, 1000, out value);

    private static bool TryParseMm(string text, double min, double max, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private void RebuildGrid(bool focusEditor = true)
    {
        _row = Math.Clamp(_row, 0, Model.RowCount - 1);
        _column = Math.Clamp(_column, 0, Model.ColumnCount - 1);
        _headerBox.IsChecked = Model.HasHeader;

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        _editors.Clear();

        foreach (var width in Model.ColumnWidths)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * PxPerMm) });
        foreach (var unused in Model.RowHeights)
            _grid.RowDefinitions.Add(new RowDefinition());

        var outer = Model.Style.OuterLineMm * PxPerMm;
        var inner = Math.Max(1, Model.Style.InnerLineMm * PxPerMm);

        for (var r = 0; r < Model.RowCount; r++)
        {
            for (var c = 0; c < Model.ColumnCount; c++)
            {
                var editor = CreateEditor(r, c);
                _editors[(r, c)] = editor;

                var bottom = r == Model.RowCount - 1 || (Model.HasHeader && r == 0) ? outer : inner;
                var border = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(c == 0 ? outer : 0, r == 0 ? outer : 0, c == Model.ColumnCount - 1 ? outer : inner, bottom),
                    Child = editor,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                _grid.Children.Add(border);
            }
        }

        UpdateSizeBoxes();
        RefreshLayout();
        if (focusEditor)
            FocusActiveEditor();
    }

    private CellEditor CreateEditor(int row, int column)
    {
        var background = Model.IsHeaderRow(row) ? HeaderBackground : Brushes.White;
        var editor = new CellEditor(Model.GetCell(row, column), Model.Style, PxPerMm, background);

        editor.ContentChanged += e =>
        {
            Model.SetCell(row, column, e.Buffer.ToCell(Model.GetCell(row, column)));
            RefreshLayout();
        };
        editor.StateChanged += _ => SyncFormatBar();
        editor.GotKeyboardFocus += (_, _) =>
        {
            _row = row;
            _column = column;
            UpdateSizeBoxes();
            SyncFormatBar();
        };
        editor.NavigateRequested += (_, rows, columns) => Navigate(row + rows, column + columns, rows, columns);
        return editor;
    }

    /// <summary>
    /// Переход в соседнюю ячейку: Tab — следующая (в конце строки — первая ячейка следующей строки),
    /// стрелки вверх/вниз с первой/последней строки текста — ячейка выше/ниже.
    /// </summary>
    private void Navigate(int row, int column, int rowStep, int columnStep)
    {
        if (columnStep != 0)
        {
            if (column >= Model.ColumnCount)
            {
                column = 0;
                row++;
            }
            else if (column < 0)
            {
                column = Model.ColumnCount - 1;
                row--;
            }
        }

        if (row < 0 || row >= Model.RowCount || !_editors.TryGetValue((row, column), out var target))
            return;

        _row = row;
        _column = column;
        target.FocusAtEnd();
    }

    /// <summary>
    /// Пересчитывает раскладку таблицы так же, как для чертежа: высоты строк (с переносом по словам)
    /// и расстановку текста каждой ячейки.
    /// </summary>
    private void RefreshLayout()
    {
        var layout = TableLayout.Build(Model, _metrics, includeEmptyCells: true);
        for (var r = 0; r < Model.RowCount && r < _grid.RowDefinitions.Count; r++)
            _grid.RowDefinitions[r].Height = new GridLength((layout.RowEdges[r + 1] - layout.RowEdges[r]) * PxPerMm);

        foreach (var text in layout.Texts)
        {
            if (_editors.TryGetValue((text.Row, text.Column), out var editor))
                editor.Layout = text;
        }
    }

    private void UpdateSizeBoxes()
    {
        _columnWidthBox.Text = Model.ColumnWidths[_column].ToString("0.##", CultureInfo.CurrentCulture);
        _rowHeightBox.Text = Model.RowHeights[_row].ToString("0.##", CultureInfo.CurrentCulture);
    }
}
