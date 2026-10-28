using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InventorTableEditor.Core.Model;
using InventorTableEditor.Core.Templates;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Окно «Шаблоны таблиц»: список (встроенные и свои) с предпросмотром, создание таблицы по шаблону,
/// сохранение открытой таблицы как шаблона, переименование и удаление своих шаблонов.
/// </summary>
internal sealed class TemplatesWindow : Window
{
    private readonly ResourceDictionary _theme;
    private readonly TemplateLibrary _library;
    private readonly TableModel _current;
    private readonly ListBox _list = new() { Width = 250, BorderThickness = new Thickness(0) };
    private readonly ContentControl _preview = new();
    private readonly TextBlock _previewNote = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _createButton;
    private readonly Button _renameButton;
    private readonly Button _deleteButton;
    private TableModel? _selectedModel;

    public TemplatesWindow(TemplateLibrary library, TableModel current)
    {
        _library = library;
        _current = current;
        _theme = ThemeLoader.ApplyTo(this);
        Title = "Шаблоны таблиц";
        Width = 860;
        Height = 560;
        MinWidth = 640;
        MinHeight = 400;
        _previewNote.Foreground = ThemeBrush("MutedTextBrush");

        _createButton = new Button { Content = "Создать по шаблону", Style = ThemeStyle("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Заменить открытую таблицу таблицей из шаблона (отменяется Ctrl+Z)" };
        _createButton.Click += (_, _) => Create();
        _renameButton = Secondary("Переименовать…", Rename);
        _deleteButton = Secondary("Удалить", Delete);

        Content = BuildLayout();
        _list.SelectionChanged += (_, _) => ShowSelected();
        _list.MouseDoubleClick += (_, e) =>
        {
            if (_list.SelectedItem != null && e.ChangedButton == MouseButton.Left)
                Create();
        };
        Reload(selectName: null);
    }

    /// <summary>Таблица по выбранному шаблону (после «Создать по шаблону»).</summary>
    public TableModel? Result { get; private set; }

    private Brush ThemeBrush(string key) => (Brush)_theme[key];

    private Style ThemeStyle(string key) => (Style)_theme[key];

    private UIElement BuildLayout()
    {
        var listPanel = new DockPanel();
        var header = new TextBlock { Text = "ШАБЛОНЫ", Style = ThemeStyle("SectionHeader"), Margin = new Thickness(12, 10, 12, 4) };
        DockPanel.SetDock(header, Dock.Top);
        listPanel.Children.Add(header);
        listPanel.Children.Add(_list);
        var left = new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = listPanel,
        };

        // Предпросмотр уменьшается под окно, но не увеличивается больше натурального размера ×1,5.
        var viewbox = new Viewbox { Child = _preview, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top };
        var sheet = new Border
        {
            Background = ThemeBrush("CanvasBrush"),
            Padding = new Thickness(16),
            Child = viewbox,
        };
        var right = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        var previewHeader = new TextBlock { Text = "ПРЕДПРОСМОТР", Style = ThemeStyle("SectionHeader"), Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(previewHeader, Dock.Top);
        DockPanel.SetDock(_previewNote, Dock.Bottom);
        right.Children.Add(previewHeader);
        right.Children.Add(_previewNote);
        right.Children.Add(sheet);

        var body = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(right);

        var save = Secondary("Сохранить открытую таблицу как шаблон…", Save);
        save.Margin = new Thickness(0, 0, 8, 0);
        var manage = new StackPanel { Orientation = Orientation.Horizontal };
        manage.Children.Add(save);
        manage.Children.Add(_renameButton);
        manage.Children.Add(_deleteButton);

        var close = new Button { Content = "Закрыть", Style = ThemeStyle("SecondaryButton"), IsCancel = true };
        var dialog = new StackPanel { Orientation = Orientation.Horizontal };
        dialog.Children.Add(_createButton);
        dialog.Children.Add(close);

        var footerBar = new DockPanel();
        DockPanel.SetDock(dialog, Dock.Right);
        footerBar.Children.Add(dialog);
        footerBar.Children.Add(manage);
        var footer = new Border
        {
            Background = ThemeBrush("PanelBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = footerBar,
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        return root;
    }

    private Button Secondary(string text, Action action)
    {
        var button = new Button { Content = text, Style = ThemeStyle("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private TableTemplate? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as TableTemplate;

    /// <summary>Перечитывает список шаблонов и выделяет шаблон с именем (или первый).</summary>
    private void Reload(string? selectName)
    {
        _list.Items.Clear();
        foreach (var template in _library.List())
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = template.Name, VerticalAlignment = VerticalAlignment.Center });
            if (template.BuiltIn)
                label.Children.Add(new TextBlock { Text = "  встроенный", Foreground = ThemeBrush("MutedTextBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            _list.Items.Add(new ListBoxItem { Content = label, Tag = template, Padding = new Thickness(12, 5, 12, 5) });
        }

        _list.SelectedItem = _list.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals((i.Tag as TableTemplate)?.Name, selectName, StringComparison.OrdinalIgnoreCase))
            ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        ShowSelected();
    }

    private void ShowSelected()
    {
        var template = Selected;
        _selectedModel = null;
        _preview.Content = null;
        _previewNote.Text = string.Empty;
        if (template != null)
        {
            try
            {
                _selectedModel = _library.Load(template);
                _preview.Content = TablePreview.Create(_selectedModel, ThemeBrush("AccentBrush"));
                _previewNote.Text = $"{_selectedModel.RowCount} × {_selectedModel.ColumnCount}, "
                                    + $"{Core.Editing.MmParser.Format(_selectedModel.ColumnWidths.Sum())} мм в ширину"
                                    + (template.BuiltIn ? " · встроенный шаблон: значения заполните сами" : string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                _previewNote.Text = "Шаблон не открывается: " + ex.Message;
            }
        }

        _createButton.IsEnabled = _selectedModel != null;
        _renameButton.IsEnabled = template is { BuiltIn: false };
        _deleteButton.IsEnabled = template is { BuiltIn: false };
    }

    private void Create()
    {
        if (_selectedModel == null)
            return;

        Result = _selectedModel;
        DialogResult = true;
    }

    private void Save()
    {
        var dialog = new NameDialog("Сохранить как шаблон", "Имя шаблона:", string.Empty, TemplateLibrary.CheckName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Value;
        var overwrite = false;
        if (_library.Exists(name))
        {
            if (_library.List().Any(t => t.BuiltIn && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Warn($"«{name}» — встроенный шаблон, выберите другое имя.");
                return;
            }

            if (MessageBox.Show(this, $"Шаблон «{name}» уже есть. Заменить его?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            overwrite = true;
        }

        Run(() => Reload(_library.Save(name, _current, overwrite).Name));
    }

    private void Rename()
    {
        if (Selected is not { BuiltIn: false } template)
            return;

        var dialog = new NameDialog("Переименовать шаблон", "Новое имя шаблона:", template.Name, TemplateLibrary.CheckName) { Owner = this };
        if (dialog.ShowDialog() == true)
            Run(() => Reload(_library.Rename(template, dialog.Value).Name));
    }

    private void Delete()
    {
        if (Selected is not { BuiltIn: false } template)
            return;

        if (MessageBox.Show(this, $"Удалить шаблон «{template.Name}»?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Run(() =>
            {
                _library.Delete(template);
                Reload(selectName: null);
            });
    }

    /// <summary>Действие с файлами шаблонов: ошибки показываются сообщением и пишутся в журнал.</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AddinLog.Write($"Шаблоны ({_library.Directory}): {ex.Message}");
            Warn(ex.Message);
        }
    }

    private void Warn(string message) => MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
