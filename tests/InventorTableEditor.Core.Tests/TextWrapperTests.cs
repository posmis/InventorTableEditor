using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TextWrapperTests
{
    private static readonly TableStyle Style = new();

    private static string[] Lines(TableCell cell, double availableMm) =>
        TextWrapper.Wrap(cell, Style, availableMm, FixedMetrics.Instance)
            .Select(line => string.Concat(line.Select(r => r.Text)))
            .ToArray();

    [Fact]
    public void Wrap_BreaksAtSpacesAndDropsTrailingSpaces()
    {
        // 5 мм на символ, ширина 30 мм — 6 символов в строке.
        var cell = TableCell.FromText("aa bb cc dddd", italic: false);

        Assert.Equal(new[] { "aa bb", "cc", "dddd" }, Lines(cell, 30));
    }

    [Fact]
    public void Wrap_KeepsHardLineBreaks()
    {
        var cell = TableCell.FromText("a\nb c", italic: false);

        Assert.Equal(new[] { "a", "b c" }, Lines(cell, 100));
    }

    [Fact]
    public void Wrap_LongWordStaysOnItsOwnLine()
    {
        var cell = TableCell.FromText("a verylongword b", italic: false);

        Assert.Equal(new[] { "a", "verylongword", "b" }, Lines(cell, 20));
    }

    [Fact]
    public void Wrap_KeepsWordWithIndexTogetherAndMergesRuns()
    {
        var cell = new TableCell
        {
            Runs =
            {
                new TextRun("aaaa D"),
                new TextRun("2") { Script = ScriptPosition.Superscript },
                new TextRun(" bb"),
            },
        };

        var lines = TextWrapper.Wrap(cell, Style, 30, FixedMetrics.Instance);

        Assert.Equal(new[] { "aaaa", "D2 bb" }, lines.Select(l => string.Concat(l.Select(r => r.Text))));
        Assert.Equal(new[] { "D", "2", " bb" }, lines[1].Select(r => r.Text));
    }

    [Fact]
    public void Wrap_WithoutMeasurerDoesNotWrap()
    {
        var cell = TableCell.FromText("aa bb cc dddd", italic: false);

        Assert.Single(TextWrapper.Wrap(cell, Style, 10, null));
    }

    [Fact]
    public void Build_RowGrowsForWrappedText()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetText(0, 0, "aa bb cc");

        // Столбец 20 мм, отступы по 1 мм: 18 мм — 3 символа, три строки.
        var layout = TableLayout.Build(model, FixedMetrics.Instance);

        Assert.Equal(3, layout.Texts.Single().Lines.Count);
        Assert.Equal(2 + 5 + 2 * 5 * 1.6, layout.Height, 6);
    }

    [Fact]
    public void WrapRanges_CoverTextWithTrailingSpacesAndSkipLineBreaks()
    {
        var cell = TableCell.FromText("aa bb cc\nd", italic: false);

        var ranges = TextWrapper.WrapRanges(cell, Style, 30, FixedMetrics.Instance);

        // "aa bb " (0..6), "cc" (6..8), '\n' на 8, "d" (9..10).
        Assert.Equal(new[] { (0, 6), (6, 8), (9, 10) }, ranges);
        Assert.Equal(5, TextWrapper.TrimmedEnd(cell, 0, 6));
    }

    [Fact]
    public void WrapRanges_EmptyCellHasOneEmptyLine()
    {
        Assert.Equal(new[] { (0, 0) }, TextWrapper.WrapRanges(new TableCell(), Style, 30, FixedMetrics.Instance));
    }
}
