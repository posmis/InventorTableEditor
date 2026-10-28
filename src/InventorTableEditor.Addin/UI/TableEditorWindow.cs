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
/// Окно редактора таблицы в стиле Inventor: сверху лента форматирования, слева панель структуры
/// (как в КОМПАС-3D), в центре таблица с масштабом как в Excel (Ctrl+колесо), снизу строка состояния.
/// Каждая ячейка — <see cref="CellEditor"/>: текст и при наборе выглядит как на чертеже.
/// Работает с копией модели; изменения возвращаются только по кнопке подтверждения.
/// </summary>
internal sealed class TableEditorWindow : Window
{
    /// <summary>Пикселей на мм при масштабе 100 % — натуральный размер на экране.</summary>
    private const double PxPerMm = 96.0 / 25.4;

    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;

    /// <summary>Масштаб запоминается до закрытия Inventor, как в Excel — для следующего открытия окна.</summary>
    private static double _lastZoom = 1.5;

    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    private readonly ResourceDictionary _theme = ThemeLoader.Load();
    private readonly Grid _grid = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly Slider _zoomSlider = new() { Minimum = MinZoom * 100, Maximum = MaxZoom * 100, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomText = new() { Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _columnWidthBox = new() { Width = 56 };
    private readonly TextBox _rowHeightBox = new() { Width = 56 };
    private readonly CheckBox _headerBox = new() { Content = "Строка заголовков", Margin = new Thickness(0, 2, 0, 0) };
    private readonly ComboBox _fontBox = new() { Width = 168, IsEditable = true, ToolTip = "Шрифт", Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _sizeBox = new() { Width = 58, IsEditable = true, ToolTip = "Высота шрифта, мм" };
    private readonly Dictionary<(int Row, int Column), CellEditor> _editors = new();
    private readonly TableHistory _history = new();
    private ITextMetrics _metrics;

    /// <summary>Идёт действие над несколькими ячейками: в историю оно записано одним шагом заранее.</summary>
    private bool _batch;

    private ToggleButton? _boldButton;
    private ToggleButton? _italicButton;
    private ToggleButton? _underlineButton;
    private ToggleButton? _superscriptButton;
    private ToggleButton? _subscriptButton;

    private int _row;
    private int _column;

    /// <summary>Выделение ячеек как в Excel: от начальной ячейки до текущей; null — одна ячейка.</summary>
    private CellRange? _range;
    private (int Row, int Column) _rangeAnchor;
    private bool _draggingRange;

    /// <summary>Списки шрифта и размера обновляются из формата под курсором — в это время их события игнорируются.</summary>
    private bool _syncingFormat;

    public TableEditorWindow(TableModel model, bool isNew)
    {
        Model = TableModel.FromJson(model.ToJson());
        _metrics = new WpfTextMetrics(Model.Style);
        EditorFonts.EmPerTextHeight = FontMetrics.Get(Model.Style.FontName)?.EmPerCapHeight ?? 1.4;

        Resources.MergedDictionaries.Add(_theme);
        Title = isNew ? "Новая таблица" : "Редактирование таблицы";
        Width = 1100;
        Height = 700;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Background = ThemeBrush("WindowBrush");
        Foreground = ThemeBrush("TextBrush");
        UseLayoutRounding = true;

        Content = BuildLayout(isNew ? "Вставить в чертёж" : "Применить");
        SetZoom(_lastZoom);
        RebuildGrid();
    }

    public TableModel Model { get; private set; }

    private CellEditor? ActiveEditor => _editors.TryGetValue((_row, _column), out var editor) ? editor : null;

    private Brush ThemeBrush(string key) => (Brush)_theme[key];

    private Style ThemeStyle(string key) => (Style)_theme[key];

    private UIElement BuildLayout(string acceptText)
    {
        var root = new DockPanel();

        var ribbon = BuildRibbon();
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var footer = BuildFooter(acceptText);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tools = BuildToolPanel();
        DockPanel.SetDock(tools, Dock.Left);
        root.Children.Add(tools);

        // Таблица на сером поле, как лист в Inventor; масштаб — через LayoutTransform (текст остаётся чётким).
        var sheet = new Border
        {
            Background = Brushes.White,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Child = _grid,
            LayoutTransform = _zoomTransform,
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = ThemeBrush("CanvasBrush"),
            Content = sheet,
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            // Ctrl+колесо — масштаб, как в Excel: шаг 10 %.
            SetZoom(_zoomTransform.ScaleX + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
        };
        root.Children.Add(scroll);

        PreviewKeyDown += OnWindowPreviewKeyDown;
        PreviewTextInput += (_, _) => ClearRange();
        _grid.PreviewMouseLeftButtonDown += OnGridMouseDown;
        _grid.PreviewMouseMove += OnGridMouseMove;
        _grid.PreviewMouseLeftButtonUp += (_, _) => _draggingRange = false;

        return root;
    }

    // ---------- История отмены ----------

    private TableCursor CurrentCursor() => new(_row, _column, ActiveEditor?.Buffer.Caret ?? 0);

    /// <summary>Запоминает таблицу до действия (одним шагом для всей таблицы, как в Excel).</summary>
    private void RecordUndo(string? mergeKey = null)
    {
        if (!_batch)
            _history.Record(Model, CurrentCursor(), mergeKey);
    }

    /// <summary>Действие над несколькими ячейками — один шаг истории.</summary>
    private void Batch(Action action)
    {
        RecordUndo();
        _batch = true;
        try
        {
            action();
        }
        finally
        {
            _batch = false;
        }
    }

    private void Undo() => Restore(_history.Undo(Model, CurrentCursor()));

    private void Redo() => Restore(_history.Redo(Model, CurrentCursor()));

    /// <summary>Подставляет таблицу из истории и ставит курсор туда, где было действие.</summary>
    private void Restore((TableModel Model, TableCursor Cursor)? step)
    {
        if (step is not { } value)
            return;

        Model = value.Model;
        _metrics = new WpfTextMetrics(Model.Style);
        _row = value.Cursor.Row;
        _column = value.Cursor.Column;
        RebuildGrid(focusEditor: false);

        if (ActiveEditor is { } editor)
        {
            var caret = value.Cursor.Caret;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => editor.FocusAt(caret)));
        }
    }

    // ---------- Выделение ячеек ----------

    /// <summary>Щелчок по ячейке начинает выделение; Shift+щелчок — прямоугольник от текущей ячейки, как в Excel.</summary>
    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CellAt(e.GetPosition(_grid)) is not { } cell)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && cell != (_row, _column))
        {
            _rangeAnchor = (_row, _column);
            SetRange(cell);
            // Курсор остаётся в начальной ячейке, текст в ячейке под мышью не выделяется.
            e.Handled = true;
            return;
        }

        ClearRange();
        _rangeAnchor = cell;
        _draggingRange = true;
    }

    /// <summary>Протягивание мышью за пределы начальной ячейки выделяет прямоугольник ячеек.</summary>
    private void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingRange || e.LeftButton != MouseButtonState.Pressed || CellAt(e.GetPosition(_grid)) is not { } cell)
            return;

        if (cell == _rangeAnchor)
            ClearRange();
        else
            SetRange(cell);
    }

    private void SetRange((int Row, int Column) to)
    {
        var range = CellRange.Between(_rangeAnchor, to);
        if (_range == range)
            return;

        _range = range;
        foreach (var ((row, column), editor) in _editors)
            editor.InRange = range.Contains(row, column);
    }

    private void ClearRange()
    {
        if (_range == null)
            return;

        _range = null;
        foreach (var editor in _editors.Values)
            editor.InRange = false;
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
            _history.BreakMerge();

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

        if (_range is not { } range)
            return;

        switch (e.Key)
        {
            case Key.Delete:
                // Delete очищает все выделенные ячейки, как в Excel.
                Batch(() =>
                {
                    foreach (var editor in EditorsIn(range))
                        editor.EditAll(b => b.DeleteSelection());
                });
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

    // ---------- Лента ----------

    /// <summary>Лента как в Inventor: группы кнопок с подписью снизу, разделённые тонкими линиями.</summary>
    private Border BuildRibbon()
    {
        SetupFontBoxes();

        var font = Row(_fontBox, _sizeBox);
        var style = Row(
            _boldButton = Toggle(Icons.Letter("Ж", FontWeights.Bold, FontStyles.Normal, false), "Жирный (Ctrl+B)",
                f => f.Bold, (f, on) => f.Bold = on),
            _italicButton = Toggle(Icons.Letter("К", FontWeights.Normal, FontStyles.Italic, false), "Наклонный (Ctrl+I)",
                f => f.Italic, (f, on) => f.Italic = on),
            _underlineButton = Toggle(Icons.Letter("Ч", FontWeights.Normal, FontStyles.Normal, true), "Подчёркнутый (Ctrl+U)",
                f => f.Underline, (f, on) => f.Underline = on),
            Gap(),
            _superscriptButton = Toggle(Icons.Script(ScriptPosition.Superscript), "Верхний индекс (Ctrl+Shift+=)\nОбычный текст — Ctrl+Пробел",
                f => f.Script == ScriptPosition.Superscript, (f, on) => f.Script = on ? ScriptPosition.Superscript : ScriptPosition.Normal),
            _subscriptButton = Toggle(Icons.Script(ScriptPosition.Subscript), "Нижний индекс (Ctrl+=)\nОбычный текст — Ctrl+Пробел",
                f => f.Script == ScriptPosition.Subscript, (f, on) => f.Script = on ? ScriptPosition.Subscript : ScriptPosition.Normal));
        var alignment = Row(
            Flat(Icons.Horizontal(HorizontalTextAlignment.Left), "По левому краю", () => SetHorizontalAlignment(HorizontalTextAlignment.Left)),
            Flat(Icons.Horizontal(HorizontalTextAlignment.Center), "По центру", () => SetHorizontalAlignment(HorizontalTextAlignment.Center)),
            Flat(Icons.Horizontal(HorizontalTextAlignment.Right), "По правому краю", () => SetHorizontalAlignment(HorizontalTextAlignment.Right)),
            Gap(),
            Flat(Icons.Vertical(VerticalTextAlignment.Top), "По верхнему краю", () => SetVerticalAlignment(VerticalTextAlignment.Top)),
            Flat(Icons.Vertical(VerticalTextAlignment.Middle), "По середине", () => SetVerticalAlignment(VerticalTextAlignment.Middle)),
            Flat(Icons.Vertical(VerticalTextAlignment.Bottom), "По нижнему краю", () => SetVerticalAlignment(VerticalTextAlignment.Bottom)));

        var groups = new StackPanel { Orientation = Orientation.Horizontal };
        groups.Children.Add(RibbonGroup("Шрифт", font));
        groups.Children.Add(RibbonGroup("Начертание", style));
        groups.Children.Add(RibbonGroup("Выравнивание", alignment));

        return new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 6, 6, 4),
            Child = groups,
        };
    }

    private Border RibbonGroup(string title, UIElement content)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        stack.Children.Add(new Border { Height = 30, Child = content, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = title, Style = ThemeStyle("RibbonGroupLabel") });
        return new Border { BorderBrush = ThemeBrush("BorderBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = stack };
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static FrameworkElement Gap() => new Border { Width = 6 };

    /// <summary>Переключатель формата: нажат, если формат включён у текста под курсором.</summary>
    private ToggleButton Toggle(UIElement icon, string tooltip, Func<TextRun, bool> isOn, Action<TextRun, bool> set)
    {
        var button = new ToggleButton { Content = icon, ToolTip = tooltip, Style = ThemeStyle("FlatToggle") };
        button.Click += (_, _) =>
        {
            ToggleFormat(isOn, set);
            // Состояние кнопки — по фактическому формату текста, а не по щелчку.
            SyncFormatBar();
        };
        return button;
    }

    /// <summary>
    /// Включает или выключает формат. Для нескольких выделенных ячеек — как в Excel: если формат есть
    /// у всего их текста, он снимается, иначе включается у всего. Для одной ячейки — у выделенного текста
    /// или у набираемого.
    /// </summary>
    private void ToggleFormat(Func<TextRun, bool> isOn, Action<TextRun, bool> set)
    {
        if (_range is { } range)
        {
            var editors = EditorsIn(range).ToList();
            var allOn = editors.Select(e => e.AllText(isOn)).Where(v => v != null).All(v => v == true);
            Batch(() =>
            {
                foreach (var editor in editors)
                    editor.EditAll(b => b.ApplyFormat(f => set(f, !allOn)));
            });
        }
        else
        {
            ActiveEditor?.Edit(b =>
            {
                var on = b.All(isOn);
                b.ApplyFormat(f => set(f, !on));
            });
        }

        FocusActiveEditor();
    }

    /// <summary>Действие с форматом — над всеми выделенными ячейками целиком или над текущей ячейкой.</summary>
    private void EditSelection(Action<CellTextBuffer> action)
    {
        if (_range is { } range)
        {
            Batch(() =>
            {
                foreach (var editor in EditorsIn(range))
                    editor.EditAll(action);
            });
        }
        else
        {
            ActiveEditor?.Edit(action);
        }
    }

    private IEnumerable<CellEditor> EditorsIn(CellRange range) =>
        range.Cells().Select(cell => _editors.TryGetValue(cell, out var editor) ? editor : null).OfType<CellEditor>();

    /// <summary>Ячейки, к которым относятся выравнивание и формат: выделенные или текущая.</summary>
    private IEnumerable<(int Row, int Column)> TargetCells() =>
        _range?.Cells() ?? new[] { (_row, _column) };

    private Button Flat(UIElement icon, string tooltip, Action action)
    {
        var button = new Button { Content = icon, ToolTip = tooltip, Style = ThemeStyle("FlatButton") };
        button.Click += (_, _) => action();
        return button;
    }

    // ---------- Левая панель ----------

    /// <summary>Панель структуры: подпись и ряд кнопок, как на панели таблицы в КОМПАС-3D.</summary>
    private Border BuildToolPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 2, 12, 12) };

        panel.Children.Add(new TextBlock { Text = "СТРУКТУРА", Style = ThemeStyle("SectionHeader") });
        panel.Children.Add(LabeledRow("Столбец:",
            Structure(Icons.Column(0, Icons.Badge.Add), "Вставить столбец слева", () => Model.InsertColumn(_column)),
            Structure(Icons.Column(2, Icons.Badge.Add), "Вставить столбец справа", () => { Model.InsertColumn(_column + 1); _column++; }),
            Structure(Icons.Column(1, Icons.Badge.Remove), "Удалить столбец", () => Model.RemoveColumn(_column))));
        panel.Children.Add(LabeledRow("Строка:",
            Structure(Icons.Row(0, Icons.Badge.Add), "Вставить строку сверху", () => Model.InsertRow(_row)),
            Structure(Icons.Row(2, Icons.Badge.Add), "Вставить строку снизу", () => { Model.InsertRow(_row + 1); _row++; }),
            Structure(Icons.Row(1, Icons.Badge.Remove), "Удалить строку", () => Model.RemoveRow(_row))));

        panel.Children.Add(new TextBlock { Text = "РАЗМЕРЫ, ММ", Style = ThemeStyle("SectionHeader") });
        panel.Children.Add(LabeledRow("Ширина:", SizeField(_columnWidthBox)));
        panel.Children.Add(LabeledRow("Высота:", SizeField(_rowHeightBox)));

        panel.Children.Add(new TextBlock { Text = "ТАБЛИЦА", Style = ThemeStyle("SectionHeader") });
        _headerBox.Click += (_, _) =>
        {
            RecordUndo();
            Model.SetHeader(_headerBox.IsChecked == true);
            _row = 0;
            RebuildGrid();
        };
        panel.Children.Add(_headerBox);

        _columnWidthBox.ToolTip = "Ширина текущего столбца, мм";
        _rowHeightBox.ToolTip = "Высота текущей строки, мм (растёт под текст)";
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
        button.Click += (_, _) =>
        {
            RecordUndo();
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

    // ---------- Строка состояния ----------

    /// <summary>Подсказки и масштаб слева, кнопки диалога справа.</summary>
    private Border BuildFooter(string acceptText)
    {
        var accept = new Button { Content = acceptText, Style = ThemeStyle("PrimaryButton"), IsDefault = false, Margin = new Thickness(0, 0, 8, 0) };
        accept.Click += (_, _) =>
        {
            ApplySizeBoxes();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Отмена", Style = ThemeStyle("SecondaryButton"), IsCancel = true };

        _zoomSlider.ValueChanged += (_, e) => SetZoom(e.NewValue / 100);
        var zoomOut = Flat(new TextBlock { Text = "−", FontSize = 14 }, "Уменьшить (Ctrl+колесо вниз)", () => SetZoom(_zoomTransform.ScaleX - 0.1));
        var zoomIn = Flat(new TextBlock { Text = "+", FontSize = 14 }, "Увеличить (Ctrl+колесо вверх)", () => SetZoom(_zoomTransform.ScaleX + 0.1));
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
        zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 2);
        _lastZoom = zoom;
        _zoomTransform.ScaleX = zoom;
        _zoomTransform.ScaleY = zoom;
        _zoomText.Text = $"{zoom * 100:0} %";
        if (Math.Abs(_zoomSlider.Value - zoom * 100) > 0.5)
            _zoomSlider.Value = zoom * 100;
    }

    // ---------- Формат текста ----------

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

        EditSelection(b => b.SetFontName(name.Trim()));
        FocusActiveEditor();
    }

    private void ApplySize(string text)
    {
        if (!TryParseMm(text, 0.5, 100, out var sizeMm))
            return;

        EditSelection(b => b.SetFontSize(sizeMm));
        FocusActiveEditor();
    }

    private void SetHorizontalAlignment(HorizontalTextAlignment alignment)
    {
        RecordUndo();
        foreach (var (row, column) in TargetCells())
            Model.GetCell(row, column).HorizontalAlignment = alignment;
        RefreshLayout();
        FocusActiveEditor();
    }

    private void SetVerticalAlignment(VerticalTextAlignment alignment)
    {
        RecordUndo();
        foreach (var (row, column) in TargetCells())
            Model.GetCell(row, column).VerticalAlignment = alignment;
        RefreshLayout();
        FocusActiveEditor();
    }

    private void FocusActiveEditor()
    {
        var editor = ActiveEditor;
        if (editor != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => editor.Focus()));
    }

    /// <summary>Показывает на ленте шрифт, размер и включённые форматы текста под курсором.</summary>
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
        var widthChanged = TryParseMm(_columnWidthBox.Text, out var width) && width != Model.ColumnWidths[_column];
        var heightChanged = TryParseMm(_rowHeightBox.Text, out var height) && height != Model.RowHeights[_row];
        if (!widthChanged && !heightChanged)
            return;

        RecordUndo();
        if (widthChanged)
            Model.ColumnWidths[_column] = width;
        if (heightChanged)
            Model.RowHeights[_row] = height;
        RebuildGrid(focusEditor);
    }

    private static bool TryParseMm(string text, out double value) => TryParseMm(text, 2, 1000, out value);

    private static bool TryParseMm(string text, double min, double max, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private void UpdateSizeBoxes()
    {
        _columnWidthBox.Text = Model.ColumnWidths[_column].ToString("0.##", CultureInfo.CurrentCulture);
        _rowHeightBox.Text = Model.RowHeights[_row].ToString("0.##", CultureInfo.CurrentCulture);
    }

    // ---------- Сетка таблицы ----------

    private void RebuildGrid(bool focusEditor = true)
    {
        _row = Math.Clamp(_row, 0, Model.RowCount - 1);
        _column = Math.Clamp(_column, 0, Model.ColumnCount - 1);
        _headerBox.IsChecked = Model.HasHeader;

        _range = null;
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        _editors.Clear();

        foreach (var width in Model.ColumnWidths)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * PxPerMm) });
        foreach (var unused in Model.RowHeights)
            _grid.RowDefinitions.Add(new RowDefinition());

        // Линии таблицы — как на чертеже: рамка и линия под заголовками толще разделителей.
        var outer = Math.Max(1, Model.Style.OuterLineMm * PxPerMm);
        var inner = Math.Max(0.5, Model.Style.InnerLineMm * PxPerMm);

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
        var editor = new CellEditor(Model.GetCell(row, column), Model.Style, PxPerMm, background, ThemeBrush("AccentBrush"));

        editor.ContentChanged += (e, kind) =>
        {
            var before = Model.GetCell(row, column);
            var after = e.Buffer.ToCell(before);
            if (before.SameContent(after))
            {
                e.Layout = e.Layout;
                return;
            }

            RecordUndo(kind switch
            {
                EditKind.Typing => $"type:{row}:{column}",
                EditKind.Deleting => $"delete:{row}:{column}",
                _ => null,
            });
            Model.SetCell(row, column, after);
            RefreshLayout();
        };
        // Курсор переставлен мышью — следующий набор начнёт новый шаг отмены.
        editor.PreviewMouseLeftButtonDown += (_, _) => _history.BreakMerge();
        editor.StateChanged += _ => SyncFormatBar();
        editor.GotKeyboardFocus += (_, _) =>
        {
            _history.BreakMerge();
            _row = row;
            _column = column;
            UpdateSizeBoxes();
            SyncFormatBar();
        };
        editor.NavigateRequested += (_, rows, columns) => Navigate(row + rows, column + columns, columns);
        return editor;
    }

    /// <summary>
    /// Переход в соседнюю ячейку: Tab — следующая (в конце строки — первая ячейка следующей строки),
    /// стрелки вверх/вниз с первой/последней строки текста — ячейка выше/ниже.
    /// </summary>
    private void Navigate(int row, int column, int columnStep)
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
}
