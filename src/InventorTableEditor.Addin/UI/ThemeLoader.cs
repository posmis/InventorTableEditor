using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace InventorTableEditor.Addin.UI;

/// <summary>
/// Загружает тему окна (UI/Theme.xaml) из ресурсов сборки. Через XamlReader, а не pack-URI:
/// в процессе Inventor у WPF нет объекта Application, и pack-URI сборки аддина может не разрешиться.
/// </summary>
internal static class ThemeLoader
{
    private const string ResourceName = "InventorTableEditor.Theme.xaml";

    private static ResourceDictionary? _theme;

    public static ResourceDictionary Load()
    {
        if (_theme != null)
            return _theme;

        using var stream = typeof(ThemeLoader).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Не найден ресурс темы {ResourceName}.");
        _theme = (ResourceDictionary)XamlReader.Load(stream);
        return _theme;
    }

    /// <summary>Общий вид окон аддина: тема, шрифт интерфейса Inventor, цвета; окно не в панели задач.</summary>
    public static ResourceDictionary ApplyTo(Window window)
    {
        var theme = Load();
        window.Resources.MergedDictionaries.Add(theme);
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 12;
        window.Background = (Brush)theme["WindowBrush"];
        window.Foreground = (Brush)theme["TextBrush"];
        window.UseLayoutRounding = true;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return theme;
    }
}
