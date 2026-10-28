using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>
/// Предсказуемые метрики для тестов: каждый символ шириной в высоту шрифта, верх букв на высоте шрифта,
/// наклонный текст выступает вправо на <see cref="Overhang"/> высоты, подчёркивание на 0,2 высоты ниже.
/// </summary>
internal sealed class FixedMetrics : ITextMetrics
{
    public const double Overhang = 0.25;

    public static FixedMetrics Instance { get; } = new();

    public TextExtents Measure(TextRun format, string text, double sizeMm)
    {
        var advance = text.Length * sizeMm;
        var inkRight = advance + (format.Italic && text.Trim().Length > 0 ? Overhang * sizeMm : 0);
        return new TextExtents(sizeMm, 0, advance, inkRight);
    }

    public double UnderlineOffsetMm(TextRun format, double sizeMm) => 0.2 * sizeMm;
}
