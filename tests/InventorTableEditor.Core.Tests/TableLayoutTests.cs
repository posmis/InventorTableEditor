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
        Assert.Equal(new[] { 0.0, 8, 16 }, layout.RowEdges);
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

        var separator = layout.Lines.Single(l => l.Y1 == 8 && l.Y2 == 8);
        Assert.Equal(0.5, separator.WeightMm);
        Assert.Equal(0.25, layout.Lines.Single(l => l.Y1 == 16 && l.Y2 == 16).WeightMm);
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

        Assert.Equal(TextAlignment.Center, layout.Texts.Single(t => t.Row == 0).Alignment);
        Assert.Equal(TextAlignment.Left, layout.Texts.Single(t => t.Row == 1).Alignment);
        Assert.Equal(8.0, layout.Texts.Single(t => t.Row == 1).Top);
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
        // Одна строка размером 5 мм по центру ячейки высотой 8 мм: базовая линия на 1.5 + 5.
        Assert.Equal(6.5, line.BaselineMm, 6);
        Assert.Equal(6.5, line.Segments[0].BaselineMm, 6);
        Assert.Equal(6.5 + 0.25 * 5, line.Segments[1].BaselineMm, 6);
        Assert.Equal(6.5 - 0.5 * 5, line.Segments[3].BaselineMm, 6);
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
}
