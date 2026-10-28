using System.Windows.Media;

namespace InventorTableEditor.Addin.UI;

/// <summary>Шрифт и масштаб текста в окне редактора.</summary>
internal static class EditorFonts
{
    /// <summary>
    /// Отношение размера шрифта WPF (em) к высоте шрифта на чертеже (высоте заглавной буквы).
    /// Задаётся по метрикам шрифта таблицы; используется, если метрики конкретного шрифта недоступны.
    /// </summary>
    public static double EmPerTextHeight { get; set; } = 1.4;

    /// <summary>Шрифт с запасными вариантами, если основной не установлен в Windows.</summary>
    public static FontFamily FontFor(string fontName) => new(fontName + ", ISOCPEUR, GOST type A, Arial");
}
