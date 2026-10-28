using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;
using InventorTableEditor.Core.Settings;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Окно «Настройки таблицы»: оформление открытой таблицы и, по флажку, настройки по умолчанию для новых
/// таблиц (сохраняются в профиле пользователя). «Заводские» и «Мои по умолчанию» заполняют поля
/// соответствующими значениями; применяется всё только по кнопке «Применить».
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly string[] HorizontalNames = { "Слева", "По центру", "Справа" };
    private static readonly string[] VerticalNames = { "Сверху", "По середине", "Снизу" };

    private readonly ResourceDictionary _theme;
    private readonly List<NumberField> _numbers = new();
    private readonly ComboBox _fontBox = new() { IsEditable = true, Width = 170 };
    private readonly CheckBox _italicBox = new() { Content = "Наклонный текст", Margin = new Thickness(0, 4, 0, 4) };
    private readonly ComboBox _headerAlignmentBox = Choice(HorizontalNames);
    private readonly ComboBox _dataAlignmentBox = Choice(HorizontalNames);
    private readonly ComboBox _verticalAlignmentBox = Choice(VerticalNames);
    private readonly TextBox _rowsBox = new();
    private readonly TextBox _columnsBox = new();
    private readonly CheckBox _newHeaderBox = new() { Content = "Строка заголовков", Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _saveBox = new()
    {
        Content = "Сохранить как настройки по умолчанию для новых таблиц",
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly UserSettings _saved;

    public SettingsWindow(TableStyle tableStyle, UserSettings saved)
    {
        _saved = saved;
        _theme = ThemeLoader.ApplyTo(this);
        Title = "Настройки таблицы";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        Content = BuildLayout();
        Fill(tableStyle, saved);
    }

    /// <summary>Оформление для открытой таблицы (после «Применить»).</summary>
    public TableStyle ResultStyle { get; private set; } = new();

    private Brush ThemeBrush(string key) => (Brush)_theme[key];

    private Style ThemeStyle(string key) => (Style)_theme[key];

    private UIElement BuildLayout()
    {
        foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n))
            _fontBox.Items.Add(name);

        var text = Section("ТЕКСТ");
        text.Children.Add(Labeled("Шрифт:", _fontBox));
        text.Children.Add(Number("Высота шрифта, мм:", "Высота шрифта", s => s.FontSizeMm, (s, v) => s.FontSizeMm = v));
        text.Children.Add(_italicBox);
        text.Children.Add(Number("Межстрочный интервал:", "Межстрочный интервал", s => s.LineSpacing, (s, v) => s.LineSpacing = v,
            tooltip: "Шаг строк текста в ячейке в долях высоты шрифта"));
        text.Children.Add(Number("Отступ от линий, мм:", "Отступ текста", s => s.PaddingMm, (s, v) => s.PaddingMm = v));
        _italicBox.ToolTip = "Наклон нового текста. Текст с наклоном по умолчанию изменится вместе с ним,\nявно выпрямленный или наклонённый — останется как есть.";

        var scripts = Section("ИНДЕКСЫ, % ВЫСОТЫ ШРИФТА");
        scripts.Children.Add(Number("Размер индекса:", "Размер индекса", s => s.ScriptScale, (s, v) => s.ScriptScale = v, percent: true));
        scripts.Children.Add(Number("Подъём верхнего:", "Подъём верхнего индекса", s => s.SuperscriptRise, (s, v) => s.SuperscriptRise = v, percent: true));
        scripts.Children.Add(Number("Опускание нижнего:", "Опускание нижнего индекса", s => s.SubscriptDrop, (s, v) => s.SubscriptDrop = v, percent: true));

        var wrap = Section("ПЕРЕНОС ТЕКСТА");
        wrap.Children.Add(Number("Предел сжатия, %:", "Предел сжатия перед переносом", s => s.MinScaleBeforeWrap, (s, v) => s.MinScaleBeforeWrap = v,
            percent: true, tooltip: "При включённом переносе строка сначала сжимается по ширине до этого предела\nи только потом переносится по словам. 100 % — без сжатия."));

        var lines = Section("ТОЛЩИНА ЛИНИЙ, ММ");
        lines.Children.Add(Number("Рамка:", "Рамка", s => s.OuterLineMm, (s, v) => s.OuterLineMm = v));
        lines.Children.Add(Number("Разделители:", "Разделители", s => s.InnerLineMm, (s, v) => s.InnerLineMm = v));
        lines.Children.Add(Number("Под заголовками:", "Линия под заголовками", s => s.HeaderLineMm, (s, v) => s.HeaderLineMm = v));
        lines.Children.Add(Number("Подчёркивание:", "Подчёркивание", s => s.UnderlineMm, (s, v) => s.UnderlineMm = v));

        var sizes = Section("РАЗМЕРЫ НОВЫХ ЯЧЕЕК, ММ");
        sizes.Children.Add(Number("Ширина столбца:", "Ширина столбца", s => s.DefaultColumnWidthMm, (s, v) => s.DefaultColumnWidthMm = v,
            tooltip: "Для вставляемых столбцов. Размеры уже существующих не меняются."));
        sizes.Children.Add(Number("Высота строки:", "Высота строки", s => s.DefaultRowHeightMm, (s, v) => s.DefaultRowHeightMm = v,
            tooltip: "Для вставляемых строк. Размеры уже существующих не меняются."));

        var alignment = Section("ВЫРАВНИВАНИЕ ПО УМОЛЧАНИЮ");
        alignment.Children.Add(Labeled("Заголовки:", _headerAlignmentBox));
        alignment.Children.Add(Labeled("Данные:", _dataAlignmentBox));
        alignment.Children.Add(Labeled("По вертикали:", _verticalAlignmentBox));

        var newTable = Section("НОВАЯ ТАБЛИЦА");
        newTable.Children.Add(Labeled("Строк:", Field(_rowsBox, "Строк данных, без строки заголовков")));
        newTable.Children.Add(Labeled("Столбцов:", Field(_columnsBox, null)));
        newTable.Children.Add(_newHeaderBox);
        newTable.ToolTip = "Сохраняется вместе с настройками по умолчанию";

        var left = Column(text, scripts, wrap);
        var middle = Column(lines, sizes);
        var right = Column(alignment, newTable);
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 6, 16, 12) };
        columns.Children.Add(left);
        columns.Children.Add(middle);
        columns.Children.Add(right);

        var body = new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = columns,
        };

        var root = new DockPanel();
        var footer = BuildFooter();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);
        root.Children.Add(footer);
        return root;
    }

    private UIElement BuildFooter()
    {
        var factory = new Button { Content = "Заводские", Style = ThemeStyle("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Заполнить поля заводскими значениями" };
        factory.Click += (_, _) =>
        {
            var defaults = new UserSettings();
            Fill(defaults.Style, defaults);
        };
        var mine = new Button { Content = "Мои по умолчанию", Style = ThemeStyle("SecondaryButton"),
            ToolTip = "Заполнить поля сохранёнными настройками по умолчанию" };
        mine.Click += (_, _) => Fill(_saved.Style, _saved);

        var apply = new Button { Content = "Применить", Style = ThemeStyle("PrimaryButton"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Применить к открытой таблице" };
        apply.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Отмена", Style = ThemeStyle("SecondaryButton"), IsCancel = true };

        var resets = new StackPanel { Orientation = Orientation.Horizontal };
        resets.Children.Add(factory);
        resets.Children.Add(mine);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(resets);

        var footer = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        footer.Children.Add(_saveBox);
        footer.Children.Add(bottom);
        return footer;
    }

    /// <summary>Заполняет поля: оформление и параметры новой таблицы.</summary>
    private void Fill(TableStyle style, UserSettings settings)
    {
        _fontBox.Text = style.FontName;
        _italicBox.IsChecked = style.Italic;
        foreach (var field in _numbers)
            field.Box.Text = MmParser.Format(field.Get(style) * (field.Percent ? 100 : 1));
        _headerAlignmentBox.SelectedIndex = (int)style.HeaderAlignment;
        _dataAlignmentBox.SelectedIndex = (int)style.DataAlignment;
        _verticalAlignmentBox.SelectedIndex = (int)style.VerticalAlignment;
        _rowsBox.Text = settings.NewTableRows.ToString(CultureInfo.CurrentCulture);
        _columnsBox.Text = settings.NewTableColumns.ToString(CultureInfo.CurrentCulture);
        _newHeaderBox.IsChecked = settings.NewTableHeader;
    }

    /// <summary>Проверяет поля; при ошибках показывает их списком и окно остаётся открытым.</summary>
    private void Apply()
    {
        var errors = new List<string>();
        var style = new TableStyle
        {
            FontName = _fontBox.Text.Trim(),
            Italic = _italicBox.IsChecked == true,
            HeaderAlignment = (HorizontalTextAlignment)Math.Max(0, _headerAlignmentBox.SelectedIndex),
            DataAlignment = (HorizontalTextAlignment)Math.Max(0, _dataAlignmentBox.SelectedIndex),
            VerticalAlignment = (VerticalTextAlignment)Math.Max(0, _verticalAlignmentBox.SelectedIndex),
        };
        foreach (var field in _numbers)
        {
            if (MmParser.TryParse(field.Box.Text, double.MinValue, double.MaxValue, out var value))
                field.Set(style, field.Percent ? value / 100 : value);
            else
                errors.Add($"{field.Name}: введите число.");
        }

        var settings = new UserSettings { Style = style, NewTableHeader = _newHeaderBox.IsChecked == true };
        if (int.TryParse(_rowsBox.Text.Trim(), out var rows))
            settings.NewTableRows = rows;
        else
            errors.Add("Строк в новой таблице: введите целое число.");
        if (int.TryParse(_columnsBox.Text.Trim(), out var columns))
            settings.NewTableColumns = columns;
        else
            errors.Add("Столбцов в новой таблице: введите целое число.");

        // Неразобранные поля остались заводскими — проверка диапазонов о них не сообщит повторно.
        errors.AddRange(settings.Check());
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Исправьте значения:\n\n" + string.Join("\n", errors), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_saveBox.IsChecked == true && UserSettingsFile.Save(settings) is { } error)
        {
            MessageBox.Show(this, $"Не удалось сохранить настройки в {UserSettingsFile.FilePath}:\n{error}", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultStyle = style;
        DialogResult = true;
    }

    private StackPanel Section(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, Style = ThemeStyle("SectionHeader") });
        return panel;
    }

    private static StackPanel Column(params UIElement[] sections)
    {
        var column = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 20, 0) };
        foreach (var section in sections)
            column.Children.Add(section);
        return column;
    }

    private StackPanel Labeled(string label, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private TextBox Field(TextBox box, string? tooltip)
    {
        box.Style = ThemeStyle("FieldBox");
        box.Width = 70;
        box.ToolTip = tooltip;
        return box;
    }

    private StackPanel Number(string label, string name, Func<TableStyle, double> get, Action<TableStyle, double> set,
        bool percent = false, string? tooltip = null)
    {
        var box = Field(new TextBox(), tooltip);
        _numbers.Add(new NumberField(name, box, get, set, percent));
        var row = Labeled(label, box);
        row.ToolTip = tooltip;
        return row;
    }

    private static ComboBox Choice(string[] names)
    {
        var box = new ComboBox { Width = 110 };
        foreach (var name in names)
            box.Items.Add(name);
        return box;
    }

    private sealed record NumberField(string Name, TextBox Box, Func<TableStyle, double> Get, Action<TableStyle, double> Set, bool Percent);
}
