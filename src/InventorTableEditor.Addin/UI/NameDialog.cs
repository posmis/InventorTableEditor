using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InventorTableEditor.Addin.UI;

/// <summary>Маленькое окно ввода имени (шаблона): поле, проверка и кнопки «ОК» и «Отмена».</summary>
internal sealed class NameDialog : Window
{
    private readonly TextBox _box = new() { Width = 320, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Func<string, string?> _check;

    public NameDialog(string title, string prompt, string initial, Func<string, string?> check)
    {
        _check = check;
        var theme = ThemeLoader.Load();
        Resources.MergedDictionaries.Add(theme);
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Background = (Brush)theme["WindowBrush"];

        _box.Style = (Style)theme["FieldBox"];
        _box.Text = initial;
        var ok = new Button { Content = "ОК", Style = (Style)theme["PrimaryButton"], IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Отмена", Style = (Style)theme["SecondaryButton"], IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public string Value => _box.Text.Trim();

    private void Accept()
    {
        if (_check(Value) is { } error)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _box.Focus();
            return;
        }

        DialogResult = true;
    }
}
