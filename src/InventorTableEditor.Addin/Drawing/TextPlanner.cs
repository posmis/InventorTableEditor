using System;
using System.Collections.Generic;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>Фрагмент строки (мм, до сжатия по ширине): положение пера, левый край надписи от начала строки, верх букв от верха таблицы.</summary>
internal sealed record PlannedRun(TextRun Run, double SizeMm, double PenMm, double BoxLeftMm, double BaselineMm, double TopMm);

/// <summary>Положение пера в начале фрагмента строки (включая фрагменты из одних пробелов), мм до сжатия.</summary>
internal sealed record PenPosition(TextRun Run, double SizeMm, double PenMm);

/// <summary>
/// Расстановка строки: видимые фрагменты, перья всех фрагментов (для курсора), коэффициент сжатия
/// по ширине и положение начала строки (пера 0) в координатах таблицы с учётом выравнивания ячейки, мм.
/// </summary>
internal sealed record LinePlan(List<PlannedRun> Runs, List<PenPosition> Pens, double WidthScale, double OriginMm);

/// <summary>
/// Расстановка фрагментов строки по метрикам шрифта — общая для чертежа и предпросмотра в редакторе.
/// Фрагменты идут по ширинам букв, как в Word; на стыке наклонного текста с прямым и перед верхним
/// индексом добавляется поправка на наклон. Строка шире ячейки сжимается по ширине.
/// Правила — docs/typography.md.
/// </summary>
internal static class TextPlanner
{
    public static LinePlan PlanLine(TextLine line, TableStyle style, CellText cell)
    {
        var runs = new List<PlannedRun>();
        var pens = new List<PenPosition>();
        double pen = 0, left = double.PositiveInfinity, right = double.NegativeInfinity;
        TextRun? previous = null;
        var previousOverhang = 0.0;

        foreach (var segment in line.Segments)
        {
            foreach (var run in segment.Runs)
            {
                var size = TableLayout.RenderedSize(run, style);
                var extents = Measure(run, size, style);
                if (previous != null && NeedsItalicCorrection(previous, run))
                    pen += previousOverhang;

                previous = run;
                previousOverhang = extents.OverhangMm;
                pens.Add(new PenPosition(run, size, pen));
                if (!string.IsNullOrWhiteSpace(run.Text))
                {
                    runs.Add(new PlannedRun(run, size, pen, pen + extents.BoxLeftMm, segment.BaselineMm, segment.BaselineMm - extents.TopMm));
                    left = Math.Min(left, pen + extents.BoxLeftMm);
                    right = Math.Max(right, pen + extents.AdvanceMm + extents.OverhangMm);
                }

                pen += extents.AdvanceMm;
            }
        }

        if (runs.Count == 0)
        {
            // Пустая строка (или одни пробелы): начало — по выравниванию ячейки.
            var start = cell.Alignment switch
            {
                HorizontalTextAlignment.Center => cell.Left + cell.Width / 2,
                HorizontalTextAlignment.Right => cell.Left + cell.Width - style.PaddingMm,
                _ => cell.Left + style.PaddingMm,
            };
            return new LinePlan(runs, pens, 1, start);
        }

        var available = cell.Width - 2 * style.PaddingMm;
        var width = right - left;
        var scale = width > available && available > 0
            ? Math.Max(0.1, Math.Floor(available / width * 100) / 100)
            : 1.0;

        var scaledWidth = width * scale;
        var lineLeft = cell.Alignment switch
        {
            HorizontalTextAlignment.Center => cell.Left + (cell.Width - scaledWidth) / 2,
            HorizontalTextAlignment.Right => cell.Left + cell.Width - style.PaddingMm - scaledWidth,
            _ => cell.Left + style.PaddingMm,
        };

        return new LinePlan(runs, pens, scale, lineLeft - left * scale);
    }

    /// <summary>Ширина текста в формате фрагмента по ширинам букв, мм (без сжатия).</summary>
    public static double AdvanceMm(TextRun format, string text, TableStyle style) =>
        Measure(format, TableLayout.RenderedSize(format, style), style, text).AdvanceMm;

    /// <summary>Размеры фрагмента по метрикам шрифта; если шрифт не найден в Windows — грубая оценка.</summary>
    private static TextExtents Measure(TextRun run, double size, TableStyle style, string? text = null)
    {
        text ??= run.Text;
        var metrics = FontMetrics.Get(run.FontName ?? style.FontName, run.Bold, run.Italic);
        return metrics?.Measure(text, size, run.Italic) ?? new TextExtents(size, 0, text.Length * size * 0.6, 0);
    }

    /// <summary>
    /// Поправка на наклон (как italic correction в TeX): после наклонного фрагмента верх букв выступает
    /// вправо и налезает на следующий. Нужна перед прямым текстом и перед верхним индексом;
    /// перед нижним индексом не нужна — он стоит ниже выступа.
    /// </summary>
    private static bool NeedsItalicCorrection(TextRun previous, TextRun next) =>
        previous.Italic
        && next.Script != ScriptPosition.Subscript
        && (!next.Italic || next.Script != previous.Script);
}
