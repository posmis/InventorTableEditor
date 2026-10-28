using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

/// <summary>
/// Размеры текста в мм от положения пера: верх букв над базовой линией; левый край надписи Inventor
/// (≤ 0: у наклонного текста нижние выносные элементы уходят левее начала); ширина по ширинам букв;
/// правый край очертаний букв (с наклоном) — по нему Inventor считает правый край надписи.
/// </summary>
public readonly record struct TextExtents(double TopMm, double BoxLeftMm, double AdvanceMm, double InkRightMm)
{
    /// <summary>Насколько наклонные буквы выступают правее ширины текста (≥ 0).</summary>
    public double OverhangMm => Math.Max(0, InkRightMm - AdvanceMm);

    /// <summary>Ширина надписи Inventor: от левого края надписи до правого края очертаний букв.</summary>
    public double BoxWidthMm => InkRightMm - BoxLeftMm;
}

/// <summary>Метрики шрифта: размеры текста в формате фрагмента. В аддине — по шрифту Windows (WPF).</summary>
public interface ITextMetrics
{
    /// <summary>Размеры <paramref name="text"/> в формате <paramref name="format"/> при высоте <paramref name="sizeMm"/>.</summary>
    TextExtents Measure(TextRun format, string text, double sizeMm);

    /// <summary>Расстояние от базовой линии вниз до линии подчёркивания, мм.</summary>
    double UnderlineOffsetMm(TextRun format, double sizeMm);
}

/// <summary>
/// Грубая оценка без шрифта: каждый символ шириной 0,6 высоты, верх букв на высоте шрифта.
/// Используется, когда шрифт не найден, и как запасной вариант.
/// </summary>
public sealed class ApproximateTextMetrics : ITextMetrics
{
    public static ApproximateTextMetrics Instance { get; } = new();

    public TextExtents Measure(TextRun format, string text, double sizeMm) =>
        new(sizeMm, 0, text.Length * sizeMm * 0.6, text.Length * sizeMm * 0.6);

    public double UnderlineOffsetMm(TextRun format, double sizeMm) => sizeMm * 0.14;
}
