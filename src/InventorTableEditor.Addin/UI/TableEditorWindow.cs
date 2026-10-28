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

    /// <summary>Масштаб запоминается до закрытия Inventor, как в Excel — для следующего открытия окна.</summary>
    private static double _lastZoom = 1.5;

    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    private readonly ResourceDictionary _theme = ThemeLoader.Load();
    private readonly Grid _grid = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly Slider _zoomSlider = new() { Minimum = Zoom.Min * 100, Maximum = Zoom.Max * 100, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomText = new() { Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _columnWidthBox = new() { Width = 56 };
    private readonly TextBox _rowHeightBox = new() { Width = 56 };
    private readonly CheckBox _headerBox = new() { Content = "Строка заголовков", Margin = new Thickness(0, 2, 0, 0) };
    private readonly ComboBox _fontBox = new() { Width = 168, IsEditable = true, ToolTip = "Шрифт", Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _sizeBox = new() { Width = 58, IsEditable = true, ToolTip = "Высота шрифта, мм" };
    private readonly Dictionary<(int Row, int Column), CellEditor> _editors = new();
    private readonly TableSession _session;
    private ITextMetrics _metrics;

    private ToggleButton? _boldButton;
    private ToggleButton? _italicButton;
    private ToggleButton? _underlineButton;
    private ToggleButton? _superscriptButton;
    private ToggleButton? _subscriptButton;

    /// <summary>Идёт протягивание мышью для выделения ячеек.</summary>
    private bool _draggingRange;

    /// <summary>Списки шрифта и размера обновляются из формата под курсором — в это время их события игнорируются.</summary>
    private bool _syncingFormat;

    public TableEditorWindow(TableModel model, bool isNew)
    {
        _session = new TableSession(TableModel.FromJson(model.ToJson()), () => ActiveEditor?.Buffer.Caret ?? 0);
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

    /// <summary>Таблица после правки — её забирает команда по кнопке подтверждения.</summary>
    public TableModel Model => _session.Model;

    private CellEditor? ActiveEditor => _editors.TryGetValue((_session.Row, _session.Column), out var editor) ? editor : null;

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
            SetZoom(Zoom.Next(_zoomTransform.ScaleX, e.Delta));
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

    private void Undo() => Restore(_session.Undo());

    private void Redo() => Restore(_session.Redo());

    /// <summary>Показывает таблицу из истории и ставит курсор туда, где было действие.</summary>
    private void Restore(int? caret)
    {
        if (caret is not { } position)
            return;

        _metrics = new WpfTextMetrics(Model.Style);
        RebuildGrid(focusEditor: false);
        if (ActiveEditor is { } editor)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => editor.FocusAt(position)));
    }

    // ---------- Выделение ячеек ----------

    /// <summary>Щелчок по ячейке начинает выделение; Shift+щелчок — прямоугольник от текущей ячейки, как в Excel.</summary>
    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CellAt(e.GetPosition(_grid)) is not { } cell)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && cell != (_session.Row, _session.Column))
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
        if (_draggingRange && e.LeftButton == MouseButtonState.Pressed && CellAt(e.GetPosition(_grid)) is { } cell
            && _session.ExtendSelection(cell.Row, cell.Column))
            ShowSelection();
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

        if (_session.Range is not { } range)
            return;

        switch (e.Key)
        {
            case Key.Delete:
                // Delete очищает все выделенные ячейки, как в Excel.
                _session.Batch(() =>
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
        if (_session.Range is { } range)
        {
            var editors = EditorsIn(range).ToList();
            var turnOn = TableSession.ShouldTurnOn(editors.Select(e => e.AllText(isOn)));
            _session.Batch(() =>
            {
                foreach (var editor in editors)
                    editor.EditAll(b => b.ApplyFormat(f => set(f, turnOn)));
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
        if (_session.Range is { } range)
        {
            _session.Batch(() =>
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
            Structure(Icons.Column(0, Icons.Badge.Add), "Вставить столбец слева", _session.InsertColumnLeft),
            Structure(Icons.Column(2, Icons.Badge.Add), "Вставить столбец справа", _session.InsertColumnRight),
            Structure(Icons.Column(1, Icons.Badge.Remove), "Удалить столбец", _session.DeleteColumn)));
        panel.Children.Add(LabeledRow("Строка:",
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
        if (!MmParser.TryParse(text, 0.5, 100, out var sizeMm))
            return;

        EditSelection(b => b.SetFontSize(sizeMm));
        FocusActiveEditor();
    }

    private void SetHorizontalAlignment(HorizontalTextAlignment alignment)
    {
        _session.SetHorizontalAlignment(alignment);
        RefreshLayout();
        FocusActiveEditor();
    }

    private void SetVerticalAlignment(VerticalTextAlignment alignment)
    {
        _session.SetVerticalAlignment(alignment);
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
        var changed = false;
        if (MmParser.TryParseCellSize(_columnWidthBox.Text, out var width))
            changed |= _session.SetColumnWidth(width);
        if (MmParser.TryParseCellSize(_rowHeightBox.Text, out var height))
            changed |= _session.SetRowHeight(height);

        if (changed)
            RebuildGrid(focusEditor);
        else
            UpdateSizeBoxes();
    }

    private void UpdateSizeBoxes()
    {
        _columnWidthBox.Text = MmParser.Format(Model.ColumnWidths[_session.Column]);
        _rowHeightBox.Text = MmParser.Format(Model.RowHeights[_session.Row]);
    }

    // ---------- Сетка таблицы ----------

    private void RebuildGrid(bool focusEditor = true)
    {
        _session.ClearSelection();
        _headerBox.IsChecked = Model.HasHeader;

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
            if (_session.ReplaceCell(row, column, e.Buffer.ToCell(new TableCell()), kind))
                RefreshLayout();
            else
                e.Layout = e.Layout;
        };
        // Курсор переставлен мышью — следующий набор начнёт новый шаг отмены.
        editor.PreviewMouseLeftButtonDown += (_, _) => _session.BreakMerge();
        editor.StateChanged += _ => SyncFormatBar();
        editor.GotKeyboardFocus += (_, _) =>
        {
            _session.MoveTo(row, column);
            UpdateSizeBoxes();
            SyncFormatBar();
        };
        editor.NavigateRequested += (_, rows, columns) =>
        {
            if (_session.Neighbor(rows, columns) is { } cell && _editors.TryGetValue(cell, out var target))
            {
                _session.MoveTo(cell.Row, cell.Column);
                target.FocusAtEnd();
            }
        };
        return editor;
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
