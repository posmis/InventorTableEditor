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

/// <summary>
/// Окно редактора таблицы в стиле Inventor: сверху лента форматирования, слева панель структуры
/// (как в КОМПАС-3D), в центре таблица с масштабом как в Excel (Ctrl+колесо), снизу строка состояния.
/// Каждая ячейка — <see cref="CellEditor"/>: текст и при наборе выглядит как на чертеже.
/// Работает с копией модели; изменения возвращаются только по кнопке подтверждения.
/// </summary>
internal sealed partial class TableEditorWindow : Window
{
    /// <summary>Пикселей на мм при масштабе 100 % — натуральный размер на экране.</summary>
    private const double PxPerMm = 96.0 / 25.4;

    /// <summary>
    /// Размер окна и масштаб последней открытой таблицы — для новых таблиц (до закрытия Inventor).
    /// У каждой таблицы свои размер и масштаб хранятся в ней самой (<see cref="TableModel.Editor"/>).
    /// </summary>
    private static EditorView _lastView = new();

    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    private readonly ResourceDictionary _theme;
    private readonly GridLinesOverlay _linesOverlay;
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

    private Button? _mergeButton;
    private Button? _splitButton;
    private ToggleButton? _boldButton;
    private ToggleButton? _italicButton;
    private ToggleButton? _underlineButton;
    private ToggleButton? _superscriptButton;
    private ToggleButton? _subscriptButton;
    private ToggleButton? _wrapButton;

    /// <summary>Последняя раскладка таблицы — по ней в поле высоты показывается фактическая высота строки.</summary>
    private TableLayout? _layout;

    /// <summary>Значения, показанные в полях размеров: размер применяется, только если пользователь изменил число.</summary>
    private string _shownWidth = string.Empty;
    private string _shownHeight = string.Empty;

    /// <summary>Идёт протягивание мышью для выделения ячеек.</summary>
    private bool _draggingRange;

    /// <summary>
    /// Перетаскивание границы: правой — ширина столбца, нижней — высота строки. Номер столбца или строки,
    /// точка начала (px сетки по X или Y) и исходный размер, мм.
    /// </summary>
    private (bool Row, int Index, double Start, double StartSize)? _resize;

    /// <summary>Шаг отмены для перетаскивания уже записан (записывается при первом изменении ширины).</summary>
    private bool _resizeRecorded;

    /// <summary>На каком расстоянии от границы столбца (px экрана) курсор становится стрелкой ширины.</summary>
    private const double BorderGrip = 4;

    /// <summary>Списки шрифта и размера обновляются из формата под курсором — в это время их события игнорируются.</summary>
    private bool _syncingFormat;

    public TableEditorWindow(TableModel model, bool isNew)
    {
        _session = new TableSession(TableModel.FromJson(model.ToJson()), () => ActiveEditor?.Buffer.Caret ?? 0);
        UpdateMetrics();
        _linesOverlay = new GridLinesOverlay(PxPerMm);

        _theme = ThemeLoader.ApplyTo(this);
        Title = isNew ? "Новая таблица" : "Редактирование таблицы";
        MinWidth = EditorView.MinWidth;
        MinHeight = EditorView.MinHeight;
        var view = (Model.Editor ?? _lastView).Clone();
        view.Normalize();
        // Окно не больше рабочей области экрана (таблицу могли открыть на большом мониторе).
        Width = Math.Min(view.Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(view.Height, SystemParameters.WorkArea.Height);
        if (view.Maximized)
            WindowState = WindowState.Maximized;

        Content = BuildLayout(isNew ? "Вставить в чертёж" : "Применить");
        SetZoom(view.Zoom);
        Closing += (_, _) => _lastView = CurrentView();
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
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.22 },
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
        _grid.PreviewMouseLeftButtonUp += OnGridMouseUp;
        _grid.PreviewMouseRightButtonDown += OnGridRightButtonDown;
        _grid.ContextMenu = new ContextMenu { Style = ThemeStyle("ContextMenu") };
        _grid.ContextMenuOpening += (_, _) => FillContextMenu(_grid.ContextMenu);
        _grid.MouseLeave += (_, _) =>
        {
            if (_resize != null)
                return;
            _grid.Cursor = null;
            _grid.ForceCursor = false;
        };
        // Захват мыши потерян (например, Alt+Tab во время перетаскивания) — перетаскивание заканчивается.
        _grid.LostMouseCapture += (_, _) =>
        {
            if (_resize == null)
                return;
            _resize = null;
            UpdateSizeBoxes();
        };

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

        UpdateMetrics();
        RebuildGrid(focusEditor: false);
        if (ActiveEditor is { } editor)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => editor.FocusAt(position)));
    }

    /// <summary>Метрики шрифта таблицы — после открытия, отмены и смены оформления.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_metrics))]
    private void UpdateMetrics()
    {
        _metrics = new WpfTextMetrics(Model.Style);
        EditorFonts.EmPerTextHeight = FontMetrics.Get(Model.Style.FontName)?.EmPerCapHeight ?? 1.4;
    }

    // ---------- Настройки ----------

    /// <summary>Окно настроек: оформление этой таблицы (один шаг отмены) и настройки для новых таблиц.</summary>
    private void OpenSettings()
    {
        var window = new SettingsWindow(Model.Style, UserSettingsFile.Load()) { Owner = this };
        if (window.ShowDialog() == true && _session.ApplyStyle(window.ResultStyle))
        {
            UpdateMetrics();
            RebuildGrid();
        }
        else
        {
            FocusActiveEditor();
        }
    }

    /// <summary>Шаблоны: открытая таблица заменяется таблицей из шаблона (один шаг отмены).</summary>
    private void OpenTemplates()
    {
        ApplySizeBoxes(focusEditor: false);
        var window = new TemplatesWindow(new Core.Templates.TemplateLibrary(AddinPaths.TemplatesDirectory), Model) { Owner = this };
        if (window.ShowDialog() == true && window.Result is { } model)
        {
            _session.ReplaceTable(model);
            UpdateMetrics();
            RebuildGrid();
        }
        else
        {
            FocusActiveEditor();
        }
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

        // Линии и фон строки заголовков — общим слоем под ячейками, привязанным к пикселям устройства:
        // рамки дробной толщины у каждой ячейки размывались и сливались с белым фоном.
        Grid.SetRowSpan(_linesOverlay, Math.Max(1, Model.RowCount));
        Grid.SetColumnSpan(_linesOverlay, Math.Max(1, Model.ColumnCount));
        _grid.Children.Add(_linesOverlay);

        // Одна ячейка редактора на объединённую ячейку: она растянута на все свои строки и столбцы.
        for (var r = 0; r < Model.RowCount; r++)
        {
            for (var c = 0; c < Model.ColumnCount; c++)
            {
                if (Model.IsCovered(r, c))
                    continue;

                var span = Model.GetMerge(r, c);
                var editor = CreateEditor(r, c);
                _editors[(r, c)] = editor;

                Grid.SetRow(editor, r);
                Grid.SetColumn(editor, c);
                Grid.SetRowSpan(editor, span.Bottom - span.Top + 1);
                Grid.SetColumnSpan(editor, span.Right - span.Left + 1);
                _grid.Children.Add(editor);
            }
        }

        UpdateSizeBoxes();
        RefreshLayout();
        UpdateCommandStates();
        if (focusEditor)
            FocusActiveEditor();
    }

    private CellEditor CreateEditor(int row, int column)
    {
        // Фон не рисуем: белый фон даёт лист, строка заголовков — общий слой GridLinesOverlay под ячейками.
        var editor = new CellEditor(Model.GetCell(row, column), Model.Style, PxPerMm, Brushes.Transparent, ThemeBrush("AccentBrush"));

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
            foreach (var other in _editors.Values)
                other.IsCurrent = ReferenceEquals(other, editor);
            UpdateCommandStates();
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
        _layout = layout;
        _linesOverlay.Lines = layout.Lines;
        _linesOverlay.TableWidthMm = layout.Width;
        _linesOverlay.HeaderBackground = HeaderBackground;
        _linesOverlay.HeaderRows = HeaderSpans(layout);
        for (var r = 0; r < Model.RowCount && r < _grid.RowDefinitions.Count; r++)
            _grid.RowDefinitions[r].Height = new GridLength((layout.RowEdges[r + 1] - layout.RowEdges[r]) * PxPerMm);

        foreach (var text in layout.Texts)
        {
            if (_editors.TryGetValue((text.Row, text.Column), out var editor))
                editor.Layout = text;
        }

        // Высота строки могла вырасти под текст — поле высоты показывает фактическую.
        UpdateSizeBoxes();
    }

    /// <summary>Интервалы строк заголовков (мм от верха таблицы) для заливки — смежные строки склеиваются.</summary>
    private List<(double TopMm, double BottomMm)> HeaderSpans(TableLayout layout)
    {
        var spans = new List<(double TopMm, double BottomMm)>();
        for (var r = 0; r < Model.RowCount; r++)
        {
            if (!Model.IsHeaderRow(r))
                continue;
            if (spans.Count > 0 && Math.Abs(spans[^1].BottomMm - layout.RowEdges[r]) < 1e-9)
                spans[^1] = (spans[^1].TopMm, layout.RowEdges[r + 1]);
            else
                spans.Add((layout.RowEdges[r], layout.RowEdges[r + 1]));
        }

        return spans;
    }
}
