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

/// <summary>Окно редактора: лента — шрифт, начертание, выравнивание, спецсимволы.</summary>
internal sealed partial class TableEditorWindow
{
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
            Flat(Icons.Vertical(VerticalTextAlignment.Bottom), "По нижнему краю", () => SetVerticalAlignment(VerticalTextAlignment.Bottom)),
            Gap(),
            _wrapButton = WrapButton());

        var groups = new StackPanel { Orientation = Orientation.Horizontal };
        groups.Children.Add(RibbonGroup("Шрифт", font));
        groups.Children.Add(RibbonGroup("Начертание", style));
        groups.Children.Add(RibbonGroup("Выравнивание", alignment));
        groups.Children.Add(RibbonGroup("Вставка", Row(SymbolsButton())));
        groups.Children.Add(RibbonGroup("Таблица", Row(
            Flat(Icons.Templates(), "Шаблоны таблиц\nСоздать таблицу по шаблону, сохранить эту таблицу как шаблон", OpenTemplates),
            Flat(Icons.Settings(), "Настройки таблицы\nОформление, линии, индексы, перенос, настройки по умолчанию", OpenSettings))));

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

    /// <summary>
    /// «Переносить текст», как в Excel: включено — текст сначала сжимается до предела из настроек, потом
    /// переносится по словам; выключено — строки только по Enter, длинная строка сжимается.
    /// </summary>
    private ToggleButton WrapButton()
    {
        var button = new ToggleButton
        {
            Content = Icons.WrapText(),
            ToolTip = "Переносить текст\nВключено: строка сжимается до предела из настроек, потом переносится по словам.\n"
                      + "Выключено: перенос только по Enter, длинная строка сжимается.",
            Style = ThemeStyle("FlatToggle"),
        };
        button.Click += (_, _) => ToggleWrap();
        return button;
    }

    private void ToggleWrap()
    {
        _session.SetWrapText(!_session.WrapText);
        RefreshLayout();
        SyncFormatBar();
        FocusActiveEditor();
    }

    private Button Flat(UIElement icon, string tooltip, Action action)
    {
        var button = new Button { Content = icon, ToolTip = tooltip, Style = ThemeStyle("FlatButton") };
        button.Click += (_, _) => action();
        return button;
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
            SetChecked(_wrapButton, _session.WrapText);
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

    // ---------- Спецсимволы ----------

    /// <summary>Быстрый список спецсимволов: символ и подсказка.</summary>
    private static readonly (string Symbol, string Name)[] Symbols =
    {
        ("⌀", "Знак диаметра"), ("Ø", "Диаметр (буква Ø)"), ("±", "Плюс-минус"), ("°", "Градус"), ("′", "Минута"),
        ("″", "Секунда"), ("×", "Умножение"), ("·", "Точка (умножение)"), ("≤", "Меньше или равно"), ("≥", "Больше или равно"),
        ("≈", "Приблизительно равно"), ("≠", "Не равно"), ("∞", "Бесконечность"), ("√", "Корень"), ("∑", "Сумма"),
        ("∠", "Угол"), ("⊥", "Перпендикулярно"), ("∥", "Параллельно"), ("→", "Стрелка"), ("—", "Тире"),
        ("№", "Номер"), ("§", "Параграф"), ("‰", "Промилле"), ("²", "Квадрат"), ("³", "Куб"),
        ("½", "Половина"), ("¼", "Четверть"), ("¾", "Три четверти"), ("«", "Открывающая кавычка"), ("»", "Закрывающая кавычка"),
        ("α", "Альфа"), ("β", "Бета"), ("γ", "Гамма"), ("δ", "Дельта"), ("Δ", "Дельта прописная"),
        ("ε", "Эпсилон"), ("η", "Эта"), ("θ", "Тета"), ("λ", "Лямбда"), ("μ", "Мю"),
        ("π", "Пи"), ("ρ", "Ро"), ("σ", "Сигма"), ("τ", "Тау"), ("φ", "Фи"),
        ("ψ", "Пси"), ("ω", "Омега"), ("Ω", "Омега прописная"), ("ν", "Ню"), ("ξ", "Кси"),
    };

    /// <summary>Кнопка «Символ» с выпадающей панелью: быстрый список и таблица символов Windows.</summary>
    private Button SymbolsButton()
    {
        var button = new Button
        {
            Content = Row(Icons.Letter("Ω", FontWeights.Normal, FontStyles.Normal, false),
                new TextBlock { Text = "▾", FontSize = 9, Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }),
            ToolTip = "Вставить спецсимвол",
            Style = ThemeStyle("FlatButton"),
        };

        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false };
        var symbols = new UniformGrid { Columns = 10 };
        // Символы показаны шрифтом таблицы: сразу видно, есть ли знак в шрифте; если нет — подставится системный.
        var font = new FontFamily(Model.Style.FontName + ", Segoe UI Symbol");
        foreach (var (symbol, name) in Symbols)
        {
            var item = new Button
            {
                Content = new TextBlock { Text = symbol, FontFamily = font, FontSize = 16 },
                ToolTip = name,
                Style = ThemeStyle("FlatButton"),
                Width = 30,
                Height = 30,
            };
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                InsertSymbol(symbol);
            };
            symbols.Children.Add(item);
        }

        var charmap = new Button
        {
            Content = "Таблица символов Windows…",
            ToolTip = "Выберите символ, нажмите «Копировать», затем Ctrl+V в ячейке",
            Style = ThemeStyle("FlatButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 6, 0, 0),
        };
        charmap.Click += (_, _) =>
        {
            popup.IsOpen = false;
            OpenCharacterMap();
        };

        var content = new StackPanel();
        content.Children.Add(symbols);
        content.Children.Add(new Border { Height = 1, Background = ThemeBrush("BorderBrush"), Margin = new Thickness(0, 6, 0, 0) });
        content.Children.Add(charmap);
        popup.Child = new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = content,
        };

        button.Click += (_, _) => popup.IsOpen = true;
        return button;
    }

    /// <summary>Символ — в текущую ячейку на место курсора (вместо выделенного текста).</summary>
    private void InsertSymbol(string symbol)
    {
        ClearRange();
        ActiveEditor?.Edit(b => b.Insert(symbol));
        FocusActiveEditor();
    }

    private void OpenCharacterMap()
    {
        try
        {
            Process.Start(new ProcessStartInfo("charmap.exe") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Не удалось открыть таблицу символов Windows: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
