using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Layout;

public enum TextAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Отрезок линии таблицы. Координаты в мм от левого верхнего угла, Y вниз.</summary>
public sealed record LineSegment(double X1, double Y1, double X2, double Y2, double WeightMm);

/// <summary>Ячейка с текстом. Координаты в мм от левого верхнего угла, Y вниз.</summary>
public sealed record CellText(int Row, int Column, double Left, double Top, double Width, double Height, string Text, TextAlignment Alignment);

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
        var maxLines = model.Cells[row].Max(t => t.Split('\n').Length);
        var required = 2 * style.PaddingMm + style.FontSizeMm + (maxLines - 1) * style.FontSizeMm * style.LineSpacing;
        return Math.Max(model.RowHeights[row], required);
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
                var text = model.Cells[r][c];
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var alignment = model.IsHeaderRow(r) ? TextAlignment.Center : TextAlignment.Left;
                texts.Add(new CellText(r, c, columnEdges[c], rowEdges[r],
                    columnEdges[c + 1] - columnEdges[c], rowEdges[r + 1] - rowEdges[r], text, alignment));
            }
        }

        return new TableLayout(columnEdges, rowEdges, lines, texts);
    }
}
