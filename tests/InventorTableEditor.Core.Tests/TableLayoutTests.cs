using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TableLayoutTests
{
    [Fact]
    public void Build_ComputesEdgesAndLines()
    {
        var model = TableModel.CreateEmpty(2, 3);

        var layout = TableLayout.Build(model);

        Assert.Equal(new[] { 0.0, 20, 40, 60 }, layout.ColumnEdges);
        Assert.Equal(new[] { 0.0, 10, 20 }, layout.RowEdges);
        // 4 линии рамки + 2 вертикальных + 1 горизонтальный разделитель.
        Assert.Equal(7, layout.Lines.Count);
        Assert.Equal(4, layout.Lines.Count(l => l.WeightMm == 0.5));
        Assert.Empty(layout.Texts);
    }

    [Fact]
    public void Build_HeaderSeparatorIsThick()
    {
        var model = TableModel.CreateEmpty(2, 1);
        model.SetHeader(true);

        var layout = TableLayout.Build(model);

        var separator = layout.Lines.Single(l => l.Y1 == 10 && l.Y2 == 10);
        Assert.Equal(0.5, separator.WeightMm);
        Assert.Equal(0.25, layout.Lines.Single(l => l.Y1 == 20 && l.Y2 == 20).WeightMm);
    }

    [Fact]
    public void Build_RowGrowsForMultilineText()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetText(0, 0, "1\n2\n3");

        var layout = TableLayout.Build(model);

        // 2 * 1 (отступы) + 5 + 2 * 5 * 1.6
        Assert.Equal(23.0, layout.Height, 6);
    }

    [Fact]
    public void Build_HeaderTextCenteredDataTextLeft()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetHeader(true);
        model.SetText(0, 0, "Заголовок");
        model.SetText(1, 0, "Данные");

        var layout = TableLayout.Build(model);

        Assert.Equal(HorizontalTextAlignment.Center, layout.Texts.Single(t => t.Row == 0).Alignment);
        Assert.Equal(HorizontalTextAlignment.Left, layout.Texts.Single(t => t.Row == 1).Alignment);
        Assert.Equal(10.0, layout.Texts.Single(t => t.Row == 1).Top);
    }

    [Fact]
    public void Build_PlacesSuperscriptAndSubscript()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell
        {
            Runs =
            {
                new TextRun("R"),
                new TextRun("1") { Script = ScriptPosition.Subscript },
                new TextRun("x"),
                new TextRun("2") { Script = ScriptPosition.Superscript },
            },
        });

        var line = TableLayout.Build(model).Texts.Single().Lines.Single();

        Assert.Equal(4, line.Segments.Count);
        // Одна строка размером 5 мм по центру ячейки высотой 10 мм: базовая линия на 2.5 + 5.
        Assert.Equal(7.5, line.BaselineMm, 6);
        Assert.Equal(7.5, line.Segments[0].BaselineMm, 6);
        Assert.Equal(7.5 + 0.25 * 5, line.Segments[1].BaselineMm, 6);
        Assert.Equal(7.5 - 0.5 * 5, line.Segments[3].BaselineMm, 6);
        Assert.Equal(5 * 0.65, line.Segments[3].FontSizeMm, 6);
    }

    [Fact]
    public void Build_SplitsRunsByLineBreaks()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a\nb"), new TextRun("2") { Script = ScriptPosition.Superscript } } });

        var lines = TableLayout.Build(model).Texts.Single().Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Segments.Single().Runs.Single().Text);
        Assert.Equal(new[] { ScriptPosition.Normal, ScriptPosition.Superscript }, lines[1].Segments.Select(s => s.Script));
        Assert.Equal(5 * 1.6, lines[1].BaselineMm - lines[0].BaselineMm, 6);
    }

    [Theory]
    [InlineData(VerticalTextAlignment.Top, 1 + 5)]
    [InlineData(VerticalTextAlignment.Middle, 7.5 + 5)]
    [InlineData(VerticalTextAlignment.Bottom, 20 - 1)]
    public void Build_VerticalAlignment(VerticalTextAlignment alignment, double expectedBaseline)
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.RowHeights[0] = 20;
        model.SetText(0, 0, "A");
        model.GetCell(0, 0).VerticalAlignment = alignment;

        var line = TableLayout.Build(model).Texts.Single().Lines.Single();

        Assert.Equal(expectedBaseline, line.BaselineMm, 6);
    }

    [Fact]
    public void Build_ExplicitAlignmentOverridesHeaderDefault()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetHeader(true);
        model.SetText(0, 0, "Заголовок");
        model.GetCell(0, 0).HorizontalAlignment = HorizontalTextAlignment.Right;

        Assert.Equal(HorizontalTextAlignment.Right, TableLayout.Build(model).Texts.Single().Alignment);
    }

    [Fact]
    public void Build_RunFontSizeChangesLineHeightAndScriptSize()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell
        {
            Runs =
            {
                new TextRun("A") { FontSizeMm = 10 },
                new TextRun("2") { FontSizeMm = 10, Script = ScriptPosition.Superscript },
            },
        });

        var layout = TableLayout.Build(model);
        var line = layout.Texts.Single().Lines.Single();

        // 10 мм текста + 2 * 1 мм отступов > 8 мм заданной высоты.
        Assert.Equal(12.0, layout.Height, 6);
        Assert.Equal(10.0, line.FontSizeMm, 6);
        Assert.Equal(6.5, line.Segments[1].FontSizeMm, 6);
        Assert.Equal(line.BaselineMm - 5, line.Segments[1].BaselineMm, 6);
    }

    [Fact]
    public void Build_IncludesEmptyCellsForEditor()
    {
        var model = TableModel.CreateEmpty(1, 2);
        model.SetText(0, 0, "A");

        var layout = TableLayout.Build(model, includeEmptyCells: true);

        var empty = layout.Texts.Single(t => t.Column == 1);
        Assert.Empty(empty.Lines.Single().Segments);
        Assert.Equal(7.5, empty.Lines.Single().BaselineMm, 6);
    }
}
