using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

/// <summary>
/// Видимый фрагмент строки (мм, до сжатия): номер в <see cref="LinePlan.Pens"/>, перо, левый край надписи
/// от начала строки, базовая линия и верх букв от верха таблицы.
/// </summary>
public sealed record PlannedRun(int PenIndex, TextRun Run, double SizeMm, double PenMm, double BoxLeftMm, double BaselineMm, double TopMm);

/// <summary>Фрагмент строки (включая фрагменты из одних пробелов): перо в начале, ширина и базовая линия, мм до сжатия.</summary>
public sealed record PenPosition(TextRun Run, double SizeMm, double PenMm, double AdvanceMm, double BaselineMm);

/// <summary>Отрезок подчёркивания в координатах таблицы, мм: от X1 до X2 на высоте Y.</summary>
public sealed record UnderlineSegment(double X1Mm, double X2Mm, double YMm);

/// <summary>
/// Расстановка строки: видимые фрагменты, перья всех фрагментов (для курсора), коэффициент сжатия
/// по ширине и положение начала строки (пера 0) в координатах таблицы с учётом выравнивания, мм.
/// </summary>
public sealed record LinePlan(IReadOnlyList<PlannedRun> Runs, IReadOnlyList<PenPosition> Pens, double WidthScale, double OriginMm)
{
    /// <summary>X в координатах таблицы для положения пера (с учётом сжатия).</summary>
    public double ToTableX(double penMm) => OriginMm + penMm * WidthScale;
}

/// <summary>
/// Расстановка фрагментов строки по метрикам шрифта — общая для чертежа и редактора.
/// Фрагменты идут по ширинам букв, как в Word; на стыке наклонного текста с прямым и перед верхним
/// индексом добавляется поправка на наклон; верхний и нижний индексы подряд ставятся друг под другом;
/// строка шире ячейки сжимается по ширине. Правила — docs/typography.md.
/// </summary>
public static class LinePlanner
{
    /// <summary>Наименьшее сжатие по ширине.</summary>
    public const double MinWidthScale = 0.1;

    public static LinePlan PlanLine(TextLine line, TableStyle style, CellText cell, ITextMetrics metrics)
    {
        var runs = new List<PlannedRun>();
        var pens = new List<PenPosition>();
        double pen = 0, left = double.PositiveInfinity, right = double.NegativeInfinity;
        TextRun? previous = null;
        var previousOverhang = 0.0;

        // Группа индексов после основного текста: где она началась (конец основного текста без
        // поправки на наклон), выступ последней буквы основного текста, правый край группы
        // и стоит ли уже второй индекс под первым.
        double stackStart = 0, baseOverhang = 0, stackEnd = 0;
        var baseItalic = false;
        var stacked = false;

        foreach (var segment in line.Segments)
        {
            foreach (var run in segment.Runs)
            {
                var size = TableLayout.RenderedSize(run, style);
                var extents = metrics.Measure(run, run.Text, size);
                var isScript = run.Script != ScriptPosition.Normal;
                var afterScript = previous != null && previous.Script != ScriptPosition.Normal;

                if (isScript && !afterScript)
                {
                    stackStart = pen;
                    stackEnd = pen;
                    stacked = false;
                }

                if (isScript && afterScript && !stacked && run.Script != previous!.Script)
                {
                    // Второй индекс под первым, с того же места. Верхний у наклонной буквы сдвинут
                    // на её выступ, нижний — нет (как в TeX).
                    stacked = true;
                    pen = stackStart + (run.Script == ScriptPosition.Superscript && baseItalic ? baseOverhang : 0);
                }
                else
                {
                    if (afterScript && !isScript)
                        pen = Math.Max(pen, stackEnd);
                    if (previous != null && NeedsItalicCorrection(previous, run))
                        pen += previousOverhang;
                }

                if (!isScript)
                {
                    baseItalic = run.Italic;
                    baseOverhang = extents.OverhangMm;
                }

                previous = run;
                previousOverhang = extents.OverhangMm;
                pens.Add(new PenPosition(run, size, pen, extents.AdvanceMm, segment.BaselineMm));
                if (!string.IsNullOrWhiteSpace(run.Text))
                {
                    runs.Add(new PlannedRun(pens.Count - 1, run, size, pen, pen + extents.BoxLeftMm, segment.BaselineMm, segment.BaselineMm - extents.TopMm));
                    left = Math.Min(left, pen + extents.BoxLeftMm);
                    right = Math.Max(right, pen + extents.AdvanceMm + extents.OverhangMm);
                }

                pen += extents.AdvanceMm;
                if (isScript)
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
            ? Math.Max(MinWidthScale, Math.Floor(available / width * 100) / 100)
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
    /// Поправки ширины фрагментов по факту (жирный в Inventor уже, чем по шрифту): номер фрагмента
    /// в <see cref="LinePlan.Pens"/> → изменение его ширины, мм до сжатия. Следующие фрагменты сдвигаются,
    /// строка заново выравнивается в ячейке; сжатие не пересчитывается.
    /// </summary>
    public static LinePlan WithWidthCorrections(LinePlan plan, IReadOnlyDictionary<int, double> deltas, CellText cell)
    {
        if (deltas.Count == 0)
            return plan;

        var shifts = new double[plan.Pens.Count];
        var shift = 0.0;
        var pens = new List<PenPosition>();
        for (var i = 0; i < plan.Pens.Count; i++)
        {
            shifts[i] = shift;
            var delta = deltas.TryGetValue(i, out var value) ? value : 0;
            pens.Add(plan.Pens[i] with { PenMm = plan.Pens[i].PenMm + shift, AdvanceMm = plan.Pens[i].AdvanceMm + delta });
            shift += delta;
        }

        var runs = plan.Runs
            .Select(r => r with { PenMm = r.PenMm + shifts[r.PenIndex], BoxLeftMm = r.BoxLeftMm + shifts[r.PenIndex] })
            .ToList();
        var origin = cell.Alignment switch
        {
            HorizontalTextAlignment.Center => plan.OriginMm - shift * plan.WidthScale / 2,
            HorizontalTextAlignment.Right => plan.OriginMm - shift * plan.WidthScale,
            _ => plan.OriginMm,
        };
        return new LinePlan(runs, pens, plan.WidthScale, origin);
    }

    /// <summary>
    /// Подчёркивание строки — своими линиями, а не шрифтом Inventor (у него слишком толстое).
    /// Соседние подчёркнутые фрагменты на одной базовой линии, включая пробелы между ними, — одна линия.
    /// </summary>
    public static List<UnderlineSegment> Underlines(LinePlan plan, ITextMetrics metrics)
    {
        var result = new List<UnderlineSegment>();
        UnderlineSegment? current = null;
        foreach (var pen in plan.Pens)
        {
            if (!pen.Run.Underline)
            {
                Flush();
                continue;
            }

            var y = pen.BaselineMm + metrics.UnderlineOffsetMm(pen.Run, pen.SizeMm);
            var x1 = plan.ToTableX(pen.PenMm);
            var x2 = plan.ToTableX(pen.PenMm + pen.AdvanceMm);
            if (current != null && Math.Abs(current.YMm - y) < 1e-6 && x1 <= current.X2Mm + 1e-6)
            {
                current = current with { X2Mm = Math.Max(current.X2Mm, x2) };
            }
            else
            {
                Flush();
                current = new UnderlineSegment(x1, x2, y);
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

    /// <summary>
    /// Поправка на наклон (как italic correction в TeX): после наклонного фрагмента верх букв выступает
    /// вправо и налезает на следующий. Нужна перед прямым текстом и перед верхним индексом;
    /// перед нижним индексом не нужна — он стоит ниже выступа.
    /// </summary>
    public static bool NeedsItalicCorrection(TextRun previous, TextRun next) =>
        previous.Italic
        && next.Script != ScriptPosition.Subscript
        && (!next.Italic || next.Script != previous.Script);
}
