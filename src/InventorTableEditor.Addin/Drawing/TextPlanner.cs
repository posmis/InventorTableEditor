using System;
using System.Collections.Generic;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Addin.Drawing;

/// <summary>Фрагмент строки (мм, до сжатия по ширине): положение пера, левый край надписи от начала строки, верх букв от верха таблицы.</summary>
internal sealed record PlannedRun(TextRun Run, double SizeMm, double PenMm, double BoxLeftMm, double BaselineMm, double TopMm);

/// <summary>Фрагмент строки (включая фрагменты из одних пробелов): перо в начале, ширина и базовая линия, мм до сжатия.</summary>
internal sealed record PenPosition(TextRun Run, double SizeMm, double PenMm, double AdvanceMm, double BaselineMm);

/// <summary>Отрезок подчёркивания в координатах таблицы, мм: от X1 до X2 на высоте Y.</summary>
internal sealed record Underline(double X1Mm, double X2Mm, double YMm);

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

        // Индексы после основного текста: где началась группа индексов (конец основного текста
        // без поправки на наклон), выступ последней буквы основного текста, правый край группы
        // и стоит ли уже второй индекс под первым.
        double stackStart = 0, baseOverhang = 0, stackEnd = 0;
        var baseItalic = false;
        var stacked = false;

        foreach (var segment in line.Segments)
        {
            foreach (var run in segment.Runs)
            {
                var size = TableLayout.RenderedSize(run, style);
                var extents = Measure(run, size, style);
                var startsStack = run.Script != ScriptPosition.Normal && (previous == null || previous.Script == ScriptPosition.Normal);
                var secondIndex = previous != null && !stacked
                                  && previous.Script != ScriptPosition.Normal && run.Script != ScriptPosition.Normal
                                  && run.Script != previous.Script;

                if (startsStack)
                {
                    stackStart = pen;
                    stackEnd = pen;
                    stacked = false;
                }

                if (secondIndex)
                {
                    // Верхний и нижний индексы друг под другом: второй начинается там же, где первый.
                    // Верхний у наклонной буквы сдвинут на её выступ, нижний — нет (как в TeX).
                    stacked = true;
                    pen = stackStart + (run.Script == ScriptPosition.Superscript && baseItalic ? baseOverhang : 0);
                }
                else
                {
                    if (previous != null && previous.Script != ScriptPosition.Normal && run.Script == ScriptPosition.Normal)
                        pen = Math.Max(pen, stackEnd);
                    if (previous != null && NeedsItalicCorrection(previous, run))
                        pen += previousOverhang;
                }

                if (run.Script == ScriptPosition.Normal)
                {
                    baseItalic = run.Italic;
                    baseOverhang = extents.OverhangMm;
                }

                previous = run;
                previousOverhang = extents.OverhangMm;
                pens.Add(new PenPosition(run, size, pen, extents.AdvanceMm, segment.BaselineMm));
                if (!string.IsNullOrWhiteSpace(run.Text))
                {
                    runs.Add(new PlannedRun(run, size, pen, pen + extents.BoxLeftMm, segment.BaselineMm, segment.BaselineMm - extents.TopMm));
                    left = Math.Min(left, pen + extents.BoxLeftMm);
                    right = Math.Max(right, pen + extents.AdvanceMm + extents.OverhangMm);
                }

                pen += extents.AdvanceMm;
                if (run.Script != ScriptPosition.Normal)
                    stackEnd = Math.Max(stackEnd, pen);
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

    /// <summary>
    /// Подчёркивание строки — своими линиями, а не шрифтом Inventor (у него слишком толстое).
    /// Соседние подчёркнутые фрагменты на одной базовой линии, включая пробелы между ними, — одна линия.
    /// </summary>
    public static List<Underline> Underlines(LinePlan plan, TableStyle style)
    {
        var result = new List<Underline>();
        Underline? current = null;
        foreach (var pen in plan.Pens)
        {
            if (!pen.Run.Underline)
            {
                Flush();
                continue;
            }

            var metrics = FontMetrics.Get(pen.Run.FontName ?? style.FontName, pen.Run.Bold, pen.Run.Italic);
            var y = pen.BaselineMm + (metrics?.UnderlineOffsetMm(pen.SizeMm) ?? pen.SizeMm * 0.14);
            var x1 = plan.OriginMm + pen.PenMm * plan.WidthScale;
            var x2 = plan.OriginMm + (pen.PenMm + pen.AdvanceMm) * plan.WidthScale;
            if (current != null && Math.Abs(current.YMm - y) < 1e-6 && x1 <= current.X2Mm + 1e-6)
                current = current with { X2Mm = Math.Max(current.X2Mm, x2) };
            else
            {
                Flush();
                current = new Underline(x1, x2, y);
            }
        }

        Flush();
        return result;

        void Flush()
        {
            if (current != null)
                result.Add(current);
            current = null;
        }
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
