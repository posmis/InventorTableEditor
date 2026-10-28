using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Editing;

/// <summary>
/// Строка ячейки для курсора: диапазон символов [Start, End] (End — за последним символом, '\n' не входит),
/// базовая линия и размер, расстановка и X каждой позиции курсора от Start до End, мм от угла таблицы.
/// </summary>
public sealed record CaretLine(int Start, int End, double BaselineMm, double SizeMm, LinePlan Plan, IReadOnlyList<double> XMm)
{
    public double XOf(int index) => XMm[index - Start];

    public bool Contains(int index) => index >= Start && index <= End;
}

/// <summary>
/// Где стоит курсор и куда попадает щелчок: позиции курсора по той же расстановке, что и текст
/// на чертеже (<see cref="LinePlanner"/>). Строится по тексту ячейки и её раскладке.
/// </summary>
public sealed class CaretMap
{
    private CaretMap(List<CaretLine> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<CaretLine> Lines { get; }

    /// <param name="cell">Текст ячейки, по которому построена <paramref name="layout"/>.</param>
    /// <param name="layout">Раскладка ячейки (с пустыми ячейками), строки — после переноса.</param>
    public static CaretMap Build(TableCell cell, CellText layout, TableStyle style, ITextMetrics metrics)
    {
        var lines = new List<CaretLine>();
        var ranges = TextWrapper.WrapRanges(cell, style, TableLayout.WrapWidth(layout.Width, style), layout.Wrap ? metrics : null);
        var defaultFormat = new TextRun(string.Empty, style.Italic);
        for (var i = 0; i < ranges.Count && i < layout.Lines.Count; i++)
        {
            var (start, end) = ranges[i];
            var line = layout.Lines[i];
            var plan = LinePlanner.PlanLine(line, style, layout, metrics);
            var trimmedEnd = TextWrapper.TrimmedEnd(cell, start, end);
            lines.Add(new CaretLine(start, end, line.BaselineMm, line.FontSizeMm, plan,
                Positions(plan, start, end, trimmedEnd, style, metrics, defaultFormat)));
        }

        return new CaretMap(lines);
    }

    /// <summary>Строка с позицией; на границе переноса по словам — следующая строка (как в Word).</summary>
    public CaretLine? LineOf(int index)
    {
        CaretLine? found = null;
        foreach (var line in Lines)
        {
            if (line.Contains(index))
                found = line;
            if (found != null && index < line.End)
                break;
        }

        return found ?? (Lines.Count > 0 ? Lines[^1] : null);
    }

    /// <summary>Позиция ближе всего к точке (мм от угла таблицы): строка — по высоте, позиция — по X.</summary>
    public int HitTest(double xMm, double yMm)
    {
        if (Lines.Count == 0)
            return 0;

        var line = Lines.FirstOrDefault(l => yMm <= l.BaselineMm + l.SizeMm * 0.35) ?? Lines[^1];
        return Nearest(line, xMm);
    }

    /// <summary>Позиция на строке выше (−1) или ниже (+1) с той же X; null — дальше строк нет.</summary>
    public int? VerticalMove(int index, int direction)
    {
        var current = LineOf(index);
        if (current == null)
            return null;

        var target = IndexOf(current) + direction;
        if (target < 0 || target >= Lines.Count)
            return null;

        return Nearest(Lines[target], current.XOf(Math.Clamp(index, current.Start, current.End)));
    }

    private int IndexOf(CaretLine line)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (ReferenceEquals(Lines[i], line))
                return i;
        }

        return -1;
    }

    private static int Nearest(CaretLine line, double xMm)
    {
        var best = 0;
        for (var k = 1; k < line.XMm.Count; k++)
        {
            if (Math.Abs(line.XMm[k] - xMm) < Math.Abs(line.XMm[best] - xMm))
                best = k;
        }

        return line.Start + best;
    }

    /// <summary>X каждой позиции курсора: по перьям фрагментов и ширинам букв; пробелы в конце — после текста.</summary>
    private static List<double> Positions(LinePlan plan, int start, int end, int trimmedEnd,
        TableStyle style, ITextMetrics metrics, TextRun defaultFormat)
    {
        var positions = new List<double>();
        var pens = plan.Pens;
        var runIndex = 0;
        var offsetInRun = 0;
        for (var k = start; k <= trimmedEnd; k++)
        {
            // На границе фрагментов курсор встаёт в начало следующего (там учтены поправка на наклон и индексы).
            while (runIndex < pens.Count - 1 && offsetInRun >= pens[runIndex].Run.Text.Length)
            {
                runIndex++;
                offsetInRun = 0;
            }

            if (pens.Count == 0)
            {
                positions.Add(plan.OriginMm);
            }
            else
            {
                var pen = pens[runIndex];
                var prefix = pen.Run.Text[..Math.Min(offsetInRun, pen.Run.Text.Length)];
                positions.Add(plan.ToTableX(pen.PenMm + Advance(pen.Run, prefix, style, metrics)));
            }

            offsetInRun++;
        }

        // Пробелы в конце строки не рисуются, но курсор по ним ходит.
        var lastFormat = pens.Count > 0 ? pens[^1].Run : defaultFormat;
        var endX = positions[^1];
        for (var k = trimmedEnd + 1; k <= end; k++)
            positions.Add(endX + Advance(lastFormat, new string(' ', k - trimmedEnd), style, metrics) * plan.WidthScale);

        return positions;
    }

    private static double Advance(TextRun format, string text, TableStyle style, ITextMetrics metrics) =>
        text.Length == 0 ? 0 : metrics.Measure(format, text, TableLayout.RenderedSize(format, style)).AdvanceMm;
}
