using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>Краевые случаи раскладки, переноса и расстановки (метрики — <see cref="FixedMetrics"/>: символ = высота шрифта).</summary>
public class LayoutEdgeCaseTests
{
    private static readonly TableStyle Style = new();

    private static string[] Wrap(string text, double width) =>
        TextWrapper.Wrap(TableCell.FromText(text, italic: false), Style, width, FixedMetrics.Instance)
            .Select(line => string.Concat(line.Select(r => r.Text)))
            .ToArray();

    [Fact]
    public void Wrap_SeveralSpacesStayWithWord()
    {
        // «aa   » = 25 мм, «aa» = 10 мм: в 20 мм «aa» помещается, «bb» переносится.
        Assert.Equal(new[] { "aa", "bb" }, Wrap("aa   bb", 20));
    }

    [Fact]
    public void Wrap_LeadingSpacesKept()
    {
        Assert.Equal(new[] { "  a" }, Wrap("  a", 100));
    }

    [Fact]
    public void Wrap_WordExactlyFitting_StaysOnLine()
    {
        Assert.Equal(new[] { "ab cd" }, Wrap("ab cd", 25));
        Assert.Equal(new[] { "ab", "cd" }, Wrap("ab cd", 24.9));
    }

    [Fact]
    public void Wrap_ZeroWidthDoesNotWrap()
    {
        Assert.Single(Wrap("aa bb cc", 0));
    }

    [Fact]
    public void Build_ColumnEdgesFollowWidths()
    {
        var model = TableModel.CreateEmpty(1, 3);
        model.ColumnWidths[0] = 10;
        model.ColumnWidths[1] = 25.5;

        var layout = TableLayout.Build(model);

        Assert.Equal(new[] { 0, 10, 35.5, 55.5 }, layout.ColumnEdges);
        Assert.Equal(55.5, layout.Width);
    }

    [Fact]
    public void Build_SetRowHeightWinsOverSmallText()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.RowHeights[0] = 30;
        model.SetText(0, 0, "a");

        Assert.Equal(30, TableLayout.Build(model).Height, 6);
    }

    [Fact]
    public void Build_LinePitchUsesEachLineSize()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a\n"), new TextRun("B") { FontSizeMm = 10 } } });

        var lines = TableLayout.Build(model).Texts.Single().Lines;

        // Вторая строка 10 мм: базовая линия ниже первой на 10 × 1,6.
        Assert.Equal(16, lines[1].BaselineMm - lines[0].BaselineMm, 6);
        Assert.Equal(10, lines[1].FontSizeMm);
    }

    [Fact]
    public void Build_SingleRowTableHasOnlyFrame()
    {
        var model = TableModel.CreateEmpty(1, 1);

        var layout = TableLayout.Build(model);

        Assert.Equal(4, layout.Lines.Count);
        Assert.All(layout.Lines, l => Assert.Equal(Style.OuterLineMm, l.WeightMm));
    }

    [Fact]
    public void Build_LineOfOnlyIndexUsesIndexBaseSize()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("2") { Script = ScriptPosition.Superscript, FontSizeMm = 8 } } });

        var line = TableLayout.Build(model).Texts.Single().Lines.Single();

        Assert.Equal(8, line.FontSizeMm);
        Assert.Equal(8 * Style.ScriptScale, line.Segments.Single().FontSizeMm, 6);
    }

    [Fact]
    public void RenderedSize_ScalesOnlyIndices()
    {
        Assert.Equal(5, TableLayout.RenderedSize(new TextRun("a"), Style));
        Assert.Equal(7, TableLayout.RenderedSize(new TextRun("a") { FontSizeMm = 7 }, Style));
        Assert.Equal(7 * Style.ScriptScale, TableLayout.RenderedSize(new TextRun("a") { FontSizeMm = 7, Script = ScriptPosition.Subscript }, Style), 6);
    }

    [Fact]
    public void PlanLine_RightAlignedCompressedLineEndsAtPadding()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("abcdef") }, HorizontalAlignment = HorizontalTextAlignment.Right });
        var cell = TableLayout.Build(model, FixedMetrics.Instance).Texts.Single();

        var plan = LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, FixedMetrics.Instance);

        Assert.True(plan.WidthScale < 1);
        Assert.True(plan.ToTableX(30) <= cell.Left + cell.Width - Style.PaddingMm + 1e-9);
    }

    [Fact]
    public void PlanLine_LeadingSpacesShiftTextButAreNotDrawn()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a\n  b") } });
        var cell = TableLayout.Build(model, FixedMetrics.Instance).Texts.Single();

        var plan = LinePlanner.PlanLine(cell.Lines[1], model.Style, cell, FixedMetrics.Instance);

        var run = plan.Runs.Single();
        Assert.Equal("b", run.Run.Text);
        Assert.Equal(10, run.PenMm, 6);
        Assert.Equal(10, run.BoxLeftMm, 6);
        // Перо для курсора — с начала пробелов.
        Assert.Equal(0, plan.Pens.Single().PenMm, 6);
        Assert.Equal("  b", plan.Pens.Single().Run.Text);
    }

    [Fact]
    public void PlanLine_SpacesOnlyLine_HasPensButNoVisibleRuns()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a\n  \nb") } });
        var cell = TableLayout.Build(model, FixedMetrics.Instance, includeEmptyCells: true).Texts.Single();

        var plan = LinePlanner.PlanLine(cell.Lines[1], model.Style, cell, FixedMetrics.Instance);

        Assert.Empty(plan.Runs);
        Assert.Equal(cell.Left + Style.PaddingMm, plan.OriginMm, 6);
    }

    [Fact]
    public void Underlines_OffsetScalesWithSize_AndSkipPlainText()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.ColumnWidths[0] = 100;
        model.SetCell(0, 0, new TableCell
        {
            Runs = { new TextRun("a") { Underline = true, FontSizeMm = 10 }, new TextRun("b"), new TextRun("c") { Underline = true } },
        });
        var cell = TableLayout.Build(model, FixedMetrics.Instance).Texts.Single();
        var plan = LinePlanner.PlanLine(cell.Lines[0], model.Style, cell, FixedMetrics.Instance);

        var lines = LinePlanner.Underlines(plan, FixedMetrics.Instance);

        Assert.Equal(2, lines.Count);
        Assert.Equal(plan.Runs[0].BaselineMm + 2, lines[0].YMm, 6);
        Assert.Equal(plan.Runs[0].BaselineMm + 1, lines[1].YMm, 6);
    }

    [Fact]
    public void ApproximateMetrics_AreUsableFallback()
    {
        var extents = ApproximateTextMetrics.Instance.Measure(new TextRun("ab"), "ab", 5);

        Assert.Equal(6, extents.AdvanceMm, 6);
        Assert.Equal(0, extents.OverhangMm);
        Assert.Equal(6, extents.BoxWidthMm, 6);
        Assert.True(ApproximateTextMetrics.Instance.UnderlineOffsetMm(new TextRun("a"), 5) > 0);
    }

    [Fact]
    public void TextExtents_OverhangOnlyWhenInkBeyondAdvance()
    {
        Assert.Equal(1, new TextExtents(5, 0, 4, 5).OverhangMm);
        Assert.Equal(0, new TextExtents(5, 0, 4, 3.5).OverhangMm);
        Assert.Equal(6, new TextExtents(5, -1, 4, 5).BoxWidthMm);
    }
}
