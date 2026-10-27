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

/// <summary>Ячейка с текстом. Координаты в мм от левого верхнего угла, Y вниз.</summary>
public sealed record CellText(int Row, int Column, double Left, double Top, double Width, double Height, IReadOnlyList<TextLine> Lines, HorizontalTextAlignment Alignment);

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
    /// Высота строки с учётом содержимого: заданная, но не меньше нужной под текст.
    /// </summary>
    public static double EffectiveRowHeight(TableModel model, int row)
    {
        var style = model.Style;
        var required = model.Cells[row].Max(cell => BlockHeight(cell, style)) + 2 * style.PaddingMm;
        return Math.Max(model.RowHeights[row], required);
    }

    /// <summary>Размер шрифта фрагмента в чертеже, мм (индексы уменьшены).</summary>
    public static double RenderedSize(TextRun run, TableStyle style)
    {
        var size = run.FontSizeMm ?? style.FontSizeMm;
        return run.Script == ScriptPosition.Normal ? size : size * style.ScriptScale;
    }

    /// <summary>Высота блока текста: от верха первой строки до базовой линии последней.</summary>
    private static double BlockHeight(TableCell cell, TableStyle style)
    {
        var sizes = cell.SplitLines().Select(line => LineSize(line, style)).ToList();
        return sizes[0] + sizes.Skip(1).Sum(size => size * style.LineSpacing);
    }

    /// <summary>Размер строки — наибольший размер основного текста (индексы не учитываются).</summary>
    private static double LineSize(List<TextRun> line, TableStyle style)
    {
        var normal = line.Where(r => r.Script == ScriptPosition.Normal).ToList();
        var source = normal.Count > 0 ? normal : line;
        return source.Count > 0 ? source.Max(r => r.FontSizeMm ?? style.FontSizeMm) : style.FontSizeMm;
    }

    private static List<TextLine> LayoutLines(TableCell cell, TableStyle style, double cellTop, double cellHeight, VerticalTextAlignment alignment)
    {
        var lines = cell.SplitLines();
        var blockHeight = BlockHeight(cell, style);
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

    public static TableLayout Build(TableModel model)
    {
        var style = model.Style;

        var columnEdges = new double[model.ColumnCount + 1];
        for (var c = 0; c < model.ColumnCount; c++)
            columnEdges[c + 1] = columnEdges[c] + model.ColumnWidths[c];

        var rowEdges = new double[model.RowCount + 1];
        for (var r = 0; r < model.RowCount; r++)
            rowEdges[r + 1] = rowEdges[r] + EffectiveRowHeight(model, r);

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
            lines.Add(new LineSegment(columnEdges[c], 0, columnEdges[c], height, style.InnerLineMm));

        for (var r = 1; r < model.RowCount; r++)
        {
            // Заголовок отделяется основной (толстой) линией.
            var weight = model.HasHeader && r == 1 ? style.OuterLineMm : style.InnerLineMm;
            lines.Add(new LineSegment(0, rowEdges[r], width, rowEdges[r], weight));
        }

        var texts = new List<CellText>();
        for (var r = 0; r < model.RowCount; r++)
        {
            for (var c = 0; c < model.ColumnCount; c++)
            {
                var cell = model.Cells[r][c];
                if (cell.IsEmpty)
                    continue;

                var alignment = model.GetHorizontalAlignment(r, c);
                var top = rowEdges[r];
                var cellHeight = rowEdges[r + 1] - top;
                texts.Add(new CellText(r, c, columnEdges[c], top, columnEdges[c + 1] - columnEdges[c], cellHeight,
                    LayoutLines(cell, style, top, cellHeight, model.GetVerticalAlignment(r, c)), alignment));
            }
        }

        return new TableLayout(columnEdges, rowEdges, lines, texts);
    }
}
