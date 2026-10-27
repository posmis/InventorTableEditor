using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Окно редактора таблицы. Слева — структура и размеры, в центре — сетка ячеек.
/// Работает с копией модели; изменения возвращаются только по кнопке подтверждения.
/// </summary>
internal sealed class TableEditorWindow : Window
{
    /// <summary>Масштаб отображения: пикселей на мм.</summary>
    private const double PxPerMm = 96.0 / 25.4 * 1.5;

    private readonly Grid _grid = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBox _columnWidthBox = new() { Width = 60 };
    private readonly TextBox _rowHeightBox = new() { Width = 60 };
    private readonly CheckBox _headerBox = new() { Content = "Заголовки столбцов", Margin = new Thickness(0, 8, 0, 0) };

    private int _row;
    private int _column;
    private RichTextBox? _activeEditor;

    private readonly ComboBox _fontBox = new() { Width = 170, IsEditable = true, ToolTip = "Шрифт" };
    private readonly ComboBox _sizeBox = new() { Width = 60, IsEditable = true, ToolTip = "Высота шрифта, мм" };

    /// <summary>Списки шрифта и размера обновляются из выделения — в это время их события игнорируются.</summary>
    private bool _syncingFormat;

    public TableEditorWindow(TableModel model, bool isNew)
    {
        Model = TableModel.FromJson(model.ToJson());
        Title = isNew ? "Новая таблица" : "Редактирование таблицы";
        Width = 1000;
        Height = 650;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Content = BuildLayout(isNew ? "Вставить в чертёж" : "Применить");
        _headerBox.IsChecked = Model.HasHeader;
        RebuildGrid();
    }

    public TableModel Model { get; }

    private UIElement BuildLayout(string acceptText)
    {
        var root = new DockPanel { Margin = new Thickness(8) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var accept = new Button { Content = acceptText, IsDefault = false, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
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
        bar.Items.Add(FormatButton("Ж", "Жирный (Ctrl+B)", () => Execute(EditingCommands.ToggleBold), b => b.FontWeight = FontWeights.Bold));
        bar.Items.Add(FormatButton("К", "Курсив (Ctrl+I)", () => Execute(EditingCommands.ToggleItalic), b => b.FontStyle = FontStyles.Italic));
        bar.Items.Add(FormatButton("Ч", "Подчёркнутый (Ctrl+U)", () => Execute(EditingCommands.ToggleUnderline),
            b => b.Content = new TextBlock { Text = "Ч", TextDecorations = TextDecorations.Underline }));
        bar.Items.Add(new Separator());
        bar.Items.Add(FormatButton("x²", "Надстрочный (Ctrl+Shift+=)", () => ToggleScript(BaselineAlignment.Superscript), null));
        bar.Items.Add(FormatButton("x₂", "Подстрочный (Ctrl+=)", () => ToggleScript(BaselineAlignment.Subscript), null));

        bar.Items.Add(new Separator());
        bar.Items.Add(FormatButton("", "По левому краю", () => SetHorizontalAlignment(HorizontalTextAlignment.Left),
            b => b.Content = AlignmentIcons.Horizontal(HorizontalTextAlignment.Left)));
        bar.Items.Add(FormatButton("", "По центру", () => SetHorizontalAlignment(HorizontalTextAlignment.Center),
            b => b.Content = AlignmentIcons.Horizontal(HorizontalTextAlignment.Center)));
        bar.Items.Add(FormatButton("", "По правому краю", () => SetHorizontalAlignment(HorizontalTextAlignment.Right),
            b => b.Content = AlignmentIcons.Horizontal(HorizontalTextAlignment.Right)));
        bar.Items.Add(new Separator());
        bar.Items.Add(FormatButton("", "По верхнему краю", () => SetVerticalAlignment(VerticalTextAlignment.Top),
            b => b.Content = AlignmentIcons.Vertical(VerticalTextAlignment.Top)));
        bar.Items.Add(FormatButton("", "По середине", () => SetVerticalAlignment(VerticalTextAlignment.Middle),
            b => b.Content = AlignmentIcons.Vertical(VerticalTextAlignment.Middle)));
        bar.Items.Add(FormatButton("", "По нижнему краю", () => SetVerticalAlignment(VerticalTextAlignment.Bottom),
            b => b.Content = AlignmentIcons.Vertical(VerticalTextAlignment.Bottom)));
        return new ToolBarTray { Margin = new Thickness(0, 0, 0, 8), ToolBars = { bar } };
    }

    private static Button FormatButton(string text, string tooltip, Action action, Action<Button>? decorate)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = tooltip,
            MinWidth = 28,
            FontSize = 14,
            // Кнопка не забирает фокус, чтобы выделение в ячейке сохранилось.
            Focusable = false,
        };
        decorate?.Invoke(button);
        button.Click += (_, _) => action();
        return button;
    }

    private void SetupFontBoxes()
    {
        foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).Append(Model.Style.FontName).Distinct().OrderBy(n => n))
            _fontBox.Items.Add(name);
        foreach (var size in new[] { 2.5, 3.5, 5, 7, 10, 14 })
            _sizeBox.Items.Add(size.ToString("0.#", CultureInfo.CurrentCulture));

        _fontBox.Text = Model.Style.FontName;
        _sizeBox.Text = Model.Style.FontSizeMm.ToString("0.#", CultureInfo.CurrentCulture);

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
        if (_activeEditor == null || string.IsNullOrWhiteSpace(name))
            return;

        _activeEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, RichTextConverter.FontFor(name.Trim()));
        _activeEditor.Focus();
    }

    /// <summary>Задаёт высоту основного текста; индексы в выделении остаются уменьшенными.</summary>
    private void ApplySize(string text)
    {
        if (_activeEditor == null || !TryParseMm(text, 0.5, 100, out var sizeMm))
            return;

        var px = sizeMm * PxPerMm * RichTextConverter.EmPerTextHeight;
        var selection = _activeEditor.Selection;
        selection.ApplyPropertyValue(TextElement.FontSizeProperty, px);

        foreach (var run in RichTextConverter.EnumerateRuns(_activeEditor.Document))
        {
            var overlaps = run.ContentStart.CompareTo(selection.End) < 0 && run.ContentEnd.CompareTo(selection.Start) > 0;
            if (overlaps && RichTextConverter.ScriptOf(run) != ScriptPosition.Normal)
                run.FontSize = px * Model.Style.ScriptScale;
        }

        _activeEditor.Focus();
    }

    private void SetHorizontalAlignment(HorizontalTextAlignment alignment)
    {
        if (_activeEditor == null)
            return;

        Model.GetCell(_row, _column).HorizontalAlignment = alignment;
        var wpfAlignment = ToWpf(alignment);
        _activeEditor.Document.TextAlignment = wpfAlignment;
        foreach (var block in _activeEditor.Document.Blocks)
            block.TextAlignment = wpfAlignment;
        _activeEditor.Focus();
    }

    private void SetVerticalAlignment(VerticalTextAlignment alignment)
    {
        if (_activeEditor == null)
            return;

        Model.GetCell(_row, _column).VerticalAlignment = alignment;
        _activeEditor.VerticalAlignment = ToWpf(alignment);
        _activeEditor.Focus();
    }

    private static System.Windows.TextAlignment ToWpf(HorizontalTextAlignment alignment) => alignment switch
    {
        HorizontalTextAlignment.Center => System.Windows.TextAlignment.Center,
        HorizontalTextAlignment.Right => System.Windows.TextAlignment.Right,
        _ => System.Windows.TextAlignment.Left,
    };

    private static VerticalAlignment ToWpf(VerticalTextAlignment alignment) => alignment switch
    {
        VerticalTextAlignment.Top => VerticalAlignment.Top,
        VerticalTextAlignment.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
    };

    /// <summary>Показывает в списках шрифт и размер текста под курсором или выделенного.</summary>
    private void SyncFormatBoxes()
    {
        if (_activeEditor == null)
            return;

        var selection = _activeEditor.Selection;
        _syncingFormat = true;
        try
        {
            _fontBox.Text = selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family
                ? RichTextConverter.FontNameOf(family)
                : string.Empty;

            var script = selection.GetPropertyValue(Inline.BaselineAlignmentProperty) switch
            {
                BaselineAlignment.Superscript => ScriptPosition.Superscript,
                BaselineAlignment.Subscript => ScriptPosition.Subscript,
                _ => ScriptPosition.Normal,
            };
            _sizeBox.Text = selection.GetPropertyValue(TextElement.FontSizeProperty) is double px
                ? RichTextConverter.TextHeightMm(px, script, Model.Style, PxPerMm).ToString("0.#", CultureInfo.CurrentCulture)
                : string.Empty;
        }
        finally
        {
            _syncingFormat = false;
        }
    }

    private void Execute(RoutedUICommand command)
    {
        if (_activeEditor == null)
            return;

        command.Execute(null, _activeEditor);
        _activeEditor.Focus();
    }

    /// <summary>
    /// Включает или выключает индекс для выделения: смещение базовой линии и уменьшенный размер.
    /// Если выделения нет, действует на текст, который будет набран дальше.
    /// </summary>
    private void ToggleScript(BaselineAlignment target)
    {
        if (_activeEditor == null)
            return;

        var selection = _activeEditor.Selection;
        var current = selection.GetPropertyValue(Inline.BaselineAlignmentProperty) as BaselineAlignment?;
        var isOn = current == target;
        var wasScript = current is BaselineAlignment.Superscript or BaselineAlignment.Subscript;
        var scale = Model.Style.ScriptScale;

        // Размер основного текста выделения: у индекса он хранится уменьшенным.
        var mainSize = selection.GetPropertyValue(TextElement.FontSizeProperty) is double size
            ? (wasScript ? size / scale : size)
            : RichTextConverter.BaseFontSizePx(Model.Style, PxPerMm);

        selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, isOn ? BaselineAlignment.Baseline : target);
        selection.ApplyPropertyValue(TextElement.FontSizeProperty, isOn ? mainSize : mainSize * scale);
        _activeEditor.Focus();
    }

    private StackPanel BuildToolPanel()
    {
        var panel = new StackPanel { Width = 190, Margin = new Thickness(0, 0, 8, 0) };

        panel.Children.Add(Group("Столбец",
            ToolButton("Вставить слева", () => Model.InsertColumn(_column)),
            ToolButton("Вставить справа", () => { Model.InsertColumn(_column + 1); _column++; }),
            ToolButton("Удалить", () => Model.RemoveColumn(_column))));

        panel.Children.Add(Group("Строка",
            ToolButton("Вставить сверху", () => Model.InsertRow(Math.Max(_row, Model.HasHeader ? 1 : 0))),
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

        _columnWidthBox.LostFocus += (_, _) => ApplySizeBoxes();
        _rowHeightBox.LostFocus += (_, _) => ApplySizeBoxes();
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
            RebuildGrid();
            e.Handled = true;
        }
    }

    private void ApplySizeBoxes()
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
            RebuildGrid();
    }

    private static bool TryParseMm(string text, out double value) => TryParseMm(text, 2, 1000, out value);

    private static bool TryParseMm(string text, double min, double max, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private void RebuildGrid()
    {
        _row = Math.Clamp(_row, 0, Model.RowCount - 1);
        _column = Math.Clamp(_column, 0, Model.ColumnCount - 1);
        _headerBox.IsChecked = Model.HasHeader;

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();

        foreach (var width in Model.ColumnWidths)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * PxPerMm) });

        foreach (var height in Model.RowHeights)
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = height * PxPerMm });

        var outer = Model.Style.OuterLineMm * PxPerMm;
        var inner = Math.Max(1, Model.Style.InnerLineMm * PxPerMm);
        RichTextBox? focusTarget = null;
        _activeEditor = null;

        for (var r = 0; r < Model.RowCount; r++)
        {
            for (var c = 0; c < Model.ColumnCount; c++)
            {
                var cell = CreateCell(r, c);
                var bottom = r == Model.RowCount - 1 || (Model.HasHeader && r == 0) ? outer : inner;
                var border = new Border
                {
                    BorderBrush = Brushes.Black,
                    Background = cell.Background,
                    BorderThickness = new Thickness(c == 0 ? outer : 0, r == 0 ? outer : 0, c == Model.ColumnCount - 1 ? outer : inner, bottom),
                    Child = cell,
                };
                // Щелчок по пустому месту ячейки (текст выровнен вверх или вниз) ставит курсор в ячейку.
                border.MouseLeftButtonDown += (_, _) => cell.Focus();
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                _grid.Children.Add(border);

                if (r == _row && c == _column)
                    focusTarget = cell;
            }
        }

        UpdateSizeBoxes();
        if (focusTarget != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => focusTarget.Focus()));
    }

    private RichTextBox CreateCell(int row, int column)
    {
        var style = Model.Style;
        var alignment = ToWpf(Model.GetHorizontalAlignment(row, column));
        var box = new RichTextBox
        {
            Document = RichTextConverter.ToDocument(Model.GetCell(row, column), style, PxPerMm, alignment),
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Model.IsHeaderRow(row) ? new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)) : Brushes.White,
            VerticalAlignment = ToWpf(Model.GetVerticalAlignment(row, column)),
            Padding = new Thickness(style.PaddingMm * PxPerMm, 0, style.PaddingMm * PxPerMm, 0),
        };

        box.TextChanged += (_, _) =>
            Model.SetCell(row, column, RichTextConverter.FromDocument(box.Document, style, PxPerMm, Model.GetCell(row, column)));
        box.SelectionChanged += (_, _) =>
        {
            if (box == _activeEditor)
                SyncFormatBoxes();
        };
        box.GotKeyboardFocus += (_, _) =>
        {
            _row = row;
            _column = column;
            _activeEditor = box;
            UpdateSizeBoxes();
            SyncFormatBoxes();
        };
        box.PreviewKeyDown += CellPreviewKeyDown;
        return box;
    }

    /// <summary>Горячие клавиши индексов как в Word: Ctrl+= — нижний, Ctrl+Shift+= — верхний.</summary>
    private void CellPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.OemPlus || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        ToggleScript((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? BaselineAlignment.Superscript : BaselineAlignment.Subscript);
        e.Handled = true;
    }

    private void UpdateSizeBoxes()
    {
        _columnWidthBox.Text = Model.ColumnWidths[_column].ToString("0.##", CultureInfo.CurrentCulture);
        _rowHeightBox.Text = Model.RowHeights[_row].ToString("0.##", CultureInfo.CurrentCulture);
    }
}
