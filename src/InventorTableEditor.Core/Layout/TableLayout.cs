using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

/// <summary>Отрезок линии таблицы. Координаты в мм от левого верхнего угла, Y вниз.</summary>
public sealed record LineSegment(double X1, double Y1, double X2, double Y2, double WeightMm);

/// <summary>
/// Подряд идущие фрагменты строки с одинаковым положением (обычный текст, верхний или нижний индекс).
/// В чертеже выводится одной надписью. <see cref="BaselineMm"/> — базовая линия от верха таблицы.
/// </summary>
public sealed record TextSegment(IReadOnlyList<TextRun> Runs, ScriptPosition Script, double FontSizeMm, double BaselineMm);

/// <summary>Строка текста ячейки. <see cref="BaselineMm"/> — базовая линия от верха таблицы.</summary>
public sealed record TextLine(IReadOnlyList<TextSegment> Segments, double BaselineMm, double FontSizeMm);

/// <summary>
/// Ячейка с текстом. Координаты в мм от левого верхнего угла, Y вниз. <see cref="Wrap"/> — текст переносится
/// по словам; иначе строки только по Enter, а слишком длинные сжимаются.
/// </summary>
public sealed record CellText(int Row, int Column, double Left, double Top, double Width, double Height,
    IReadOnlyList<TextLine> Lines, HorizontalTextAlignment Alignment, bool Wrap = true);

/// <summary>
/// Геометрия таблицы для вывода в чертёж: границы столбцов и строк, линии и тексты.
/// </summary>
public sealed class TableLayout
{
    private TableLayout(double[] columnEdges, double[] rowEdges, List<LineSegment> lines, List<CellText> texts)
    {
        ColumnEdges = columnEdges;
        RowEdges = rowEdges;
        Lines = lines;
        Texts = texts;
    }

    /// <summary>X границ столбцов, ColumnCount + 1 значений.</summary>
    public IReadOnlyList<double> ColumnEdges { get; }

    /// <summary>Y границ строк (вниз), RowCount + 1 значений.</summary>
    public IReadOnlyList<double> RowEdges { get; }

    public IReadOnlyList<LineSegment> Lines { get; }

    public IReadOnlyList<CellText> Texts { get; }

    public double Width => ColumnEdges[^1];

    public double Height => RowEdges[^1];

    /// <summary>
    /// Высота строки с учётом содержимого: заданная, но не меньше нужной под текст
    /// (с учётом объединённых ячеек, которые в неё заходят).
    /// </summary>
    public static double EffectiveRowHeight(TableModel model, int row, ITextMetrics? metrics = null)
    {
        var edges = Build(model, metrics).RowEdges;
        return edges[row + 1] - edges[row];
    }

    /// <summary>
    /// Ширина, по которой переносится текст ячейки: доступная ширина (без отступов), делённая на предел
    /// сжатия — строка сначала сжимается до предела и только потом переносится.
    /// </summary>
    public static double WrapWidth(double cellWidthMm, TableStyle style)
    {
        var limit = style.MinScaleBeforeWrap is > 0 and <= 1 ? style.MinScaleBeforeWrap : 1;
        return (cellWidthMm - 2 * style.PaddingMm) / limit;
    }

    /// <summary>Размер шрифта фрагмента в чертеже, мм (индексы уменьшены).</summary>
    public static double RenderedSize(TextRun run, TableStyle style)
    {
        var size = run.FontSizeMm ?? style.FontSizeMm;
        return run.Script == ScriptPosition.Normal ? size : size * style.ScriptScale;
    }

    /// <summary>Высота блока текста: от верха первой строки до базовой линии последней.</summary>
    private static double BlockHeight(List<List<TextRun>> lines, TableStyle style)
    {
        var sizes = lines.Select(line => LineSize(line, style)).ToList();
        return sizes[0] + sizes.Skip(1).Sum(size => size * style.LineSpacing);
    }

    /// <summary>Размер строки — наибольший размер основного текста (индексы не учитываются).</summary>
    private static double LineSize(List<TextRun> line, TableStyle style)
    {
        var normal = line.Where(r => r.Script == ScriptPosition.Normal).ToList();
        var source = normal.Count > 0 ? normal : line;
        return source.Count > 0 ? source.Max(r => r.FontSizeMm ?? style.FontSizeMm) : style.FontSizeMm;
    }

    private static List<TextLine> LayoutLines(List<List<TextRun>> lines, TableStyle style, double cellTop, double cellHeight, VerticalTextAlignment alignment)
    {
        var blockHeight = BlockHeight(lines, style);
        var blockTop = alignment switch
        {
            VerticalTextAlignment.Top => cellTop + style.PaddingMm,
            VerticalTextAlignment.Bottom => cellTop + cellHeight - style.PaddingMm - blockHeight,
            _ => cellTop + (cellHeight - blockHeight) / 2,
        };

        var result = new List<TextLine>();
        var baseline = blockTop;
        for (var i = 0; i < lines.Count; i++)
        {
            var size = LineSize(lines[i], style);
            baseline += i == 0 ? size : size * style.LineSpacing;
            result.Add(new TextLine(Segments(lines[i], style, baseline, size), baseline, size));
        }

        return result;
    }

    private static List<TextSegment> Segments(List<TextRun> line, TableStyle style, double baseline, double lineSize)
    {
        var segments = new List<TextSegment>();
        var start = 0;
        while (start < line.Count)
        {
            var script = line[start].Script;
            var end = start;
            while (end < line.Count && line[end].Script == script)
                end++;

            var runs = line.GetRange(start, end - start);
            var segmentBaseline = script switch
            {
                ScriptPosition.Superscript => baseline - style.SuperscriptRise * lineSize,
                ScriptPosition.Subscript => baseline + style.SubscriptDrop * lineSize,
                _ => baseline,
            };
            segments.Add(new TextSegment(runs, script, runs.Max(r => RenderedSize(r, style)), segmentBaseline));
            start = end;
        }

        return segments;
    }

    /// <param name="metrics">Метрики шрифта для переноса по словам; null — без переноса.</param>
    /// <param name="includeEmptyCells">Включать пустые ячейки в <see cref="Texts"/> (редактору нужно место курсора).</param>
    public static TableLayout Build(TableModel model, ITextMetrics? metrics = null, bool includeEmptyCells = false)
    {
        var style = model.Style;
        var rows = model.RowCount;
        var columns = model.ColumnCount;

        var columnEdges = new double[columns + 1];
        for (var c = 0; c < columns; c++)
            columnEdges[c + 1] = columnEdges[c] + model.ColumnWidths[c];

        // Какой ячейке принадлежит каждая клетка сетки: объединённые — левой верхней.
        var spans = new CellRange[rows, columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
                spans[r, c] = new CellRange(r, c, r, c);
        }

        foreach (var merge in model.Merges())
        {
            foreach (var (r, c) in merge.Cells())
                spans[r, c] = merge;
        }

        // Перенос текста — по ширине ячейки (у объединённой — по ширине всех её столбцов).
        var anchors = new List<(CellRange Span, List<List<TextRun>> Lines)>();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var span = spans[r, c];
                if (span.Top != r || span.Left != c)
                    continue;

                var width = columnEdges[span.Right + 1] - columnEdges[span.Left];
                var cell = model.Cells[r][c];
                anchors.Add((span, TextWrapper.Wrap(cell, style, WrapWidth(width, style), cell.NoWrap ? null : metrics)));
            }
        }

        // Высоты строк: заданные, но не меньше нужных под текст. Объединённая по вертикали ячейка,
        // если ей не хватает места, увеличивает свою нижнюю строку.
        var heights = model.RowHeights.ToArray();
        foreach (var (span, lines) in anchors.Where(a => a.Span.Top == a.Span.Bottom))
            heights[span.Top] = Math.Max(heights[span.Top], BlockHeight(lines, style) + 2 * style.PaddingMm);
        foreach (var (span, lines) in anchors.Where(a => a.Span.Top != a.Span.Bottom).OrderBy(a => a.Span.Bottom))
        {
            var needed = BlockHeight(lines, style) + 2 * style.PaddingMm;
            var available = Enumerable.Range(span.Top, span.Bottom - span.Top + 1).Sum(r => heights[r]);
            if (needed > available)
                heights[span.Bottom] += needed - available;
        }

        var rowEdges = new double[rows + 1];
        for (var r = 0; r < rows; r++)
            rowEdges[r + 1] = rowEdges[r] + heights[r];

        var texts = new List<CellText>();
        foreach (var (span, lines) in anchors)
        {
            var cell = model.Cells[span.Top][span.Left];
            if (cell.IsEmpty && !includeEmptyCells)
                continue;

            var top = rowEdges[span.Top];
            var cellHeight = rowEdges[span.Bottom + 1] - top;
            var left = columnEdges[span.Left];
            texts.Add(new CellText(span.Top, span.Left, left, top, columnEdges[span.Right + 1] - left, cellHeight,
                LayoutLines(lines, style, top, cellHeight, model.GetVerticalAlignment(span.Top, span.Left)),
                model.GetHorizontalAlignment(span.Top, span.Left), !cell.NoWrap));
        }

        return new TableLayout(columnEdges, rowEdges, GridLines(model, spans, columnEdges, rowEdges), texts);
    }

    /// <summary>
    /// Линии таблицы: рамка — основной линией, разделители — тонкой, линия под строкой заголовков —
    /// основной. Внутри объединённых ячеек линий нет; соседние отрезки одной линии склеиваются.
    /// </summary>
    private static List<LineSegment> GridLines(TableModel model, CellRange[,] spans, double[] columnEdges, double[] rowEdges)
    {
        var style = model.Style;
        var width = columnEdges[^1];
        var height = rowEdges[^1];
        var lines = new List<LineSegment>
        {
            new(0, 0, width, 0, style.OuterLineMm),
            new(0, height, width, height, style.OuterLineMm),
            new(0, 0, 0, height, style.OuterLineMm),
            new(width, 0, width, height, style.OuterLineMm),
        };

        for (var c = 1; c < model.ColumnCount; c++)
        {
            var x = columnEdges[c];
            var pieces = Enumerable.Range(0, model.RowCount)
                .Where(r => spans[r, c - 1] != spans[r, c])
                .Select(r => (From: rowEdges[r], To: rowEdges[r + 1]));
            lines.AddRange(Join(pieces).Select(p => new LineSegment(x, p.From, x, p.To, style.InnerLineMm)));
        }

        for (var r = 1; r < model.RowCount; r++)
        {
            var y = rowEdges[r];
            var weight = model.HasHeader && r == 1 ? style.OuterLineMm : style.InnerLineMm;
            var pieces = Enumerable.Range(0, model.ColumnCount)
                .Where(c => spans[r - 1, c] != spans[r, c])
                .Select(c => (From: columnEdges[c], To: columnEdges[c + 1]));
            lines.AddRange(Join(pieces).Select(p => new LineSegment(p.From, y, p.To, y, weight)));
        }

        return lines;
    }

    /// <summary>Склеивает отрезки, идущие встык, в один.</summary>
    private static IEnumerable<(double From, double To)> Join(IEnumerable<(double From, double To)> pieces)
    {
        (double From, double To)? current = null;
        foreach (var piece in pieces)
        {
            if (current is { } open && Math.Abs(open.To - piece.From) < 1e-9)
            {
                current = (open.From, piece.To);
                continue;
            }

            if (current is { } done)
                yield return done;
            current = piece;
        }

        if (current is { } last)
            yield return last;
    }
}
