using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TextRunAndCellTests
{
    public static TheoryData<Action<TextRun>> FormatChanges => new()
    {
        r => r.Bold = true,
        r => r.Italic = true,
        r => r.Underline = true,
        r => r.Script = ScriptPosition.Superscript,
        r => r.FontName = "Arial",
        r => r.FontSizeMm = 7,
    };

    [Theory]
    [MemberData(nameof(FormatChanges))]
    public void SameFormat_DetectsEveryProperty(Action<TextRun> change)
    {
        var a = new TextRun("x");
        var b = new TextRun("y");
        Assert.True(a.SameFormat(b));

        change(b);

        Assert.False(a.SameFormat(b));
    }

    [Fact]
    public void Clone_IsIndependent_WithTextKeepsFormat()
    {
        var run = new TextRun("a") { Bold = true, FontSizeMm = 7 };

        var clone = run.Clone();
        clone.Bold = false;
        var renamed = run.WithText("b");

        Assert.True(run.Bold);
        Assert.Equal("b", renamed.Text);
        Assert.True(renamed.SameFormat(run));
        Assert.Equal("a", run.Text);
    }

    [Fact]
    public void FromText_EmptyGivesNoRuns()
    {
        Assert.Empty(TableCell.FromText(null, italic: true).Runs);
        Assert.Empty(TableCell.FromText("", italic: true).Runs);
        Assert.True(TableCell.FromText("  ", italic: true).IsEmpty);
    }

    [Fact]
    public void SplitLines_KeepsFormatAcrossBreaks()
    {
        var cell = new TableCell { Runs = { new TextRun("a\nb") { Bold = true }, new TextRun("c\n") } };

        var lines = cell.SplitLines();

        Assert.Equal(3, lines.Count);
        Assert.Equal("a", lines[0].Single().Text);
        Assert.Equal(new[] { "b", "c" }, lines[1].Select(r => r.Text));
        Assert.True(lines[1][0].Bold);
        Assert.False(lines[1][1].Bold);
        Assert.Empty(lines[2]);
    }

    [Fact]
    public void Normalize_MergesAndDropsEmpty()
    {
        var cell = new TableCell { Runs = { new TextRun(""), new TextRun("a\r\n"), new TextRun("b"), new TextRun("c") { Italic = true } } };

        cell.Normalize();

        Assert.Equal(new[] { "a\nb", "c" }, cell.Runs.Select(r => r.Text));
    }

    [Fact]
    public void Clone_KeepsAlignmentAndCopiesRuns()
    {
        var cell = new TableCell
        {
            Runs = { new TextRun("a") },
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = VerticalTextAlignment.Bottom,
        };

        var clone = cell.Clone();
        clone.Runs[0].Text = "b";

        Assert.Equal("a", cell.PlainText);
        Assert.Equal(HorizontalTextAlignment.Center, clone.HorizontalAlignment);
        Assert.Equal(VerticalTextAlignment.Bottom, clone.VerticalAlignment);
    }

    [Fact]
    public void TableStyle_CloneIsIndependent()
    {
        var style = new TableStyle();
        var clone = style.Clone();
        clone.FontSizeMm = 10;

        Assert.Equal(5, style.FontSizeMm);
    }

    [Fact]
    public void CreateEmpty_CopiesStyle()
    {
        var style = new TableStyle { DefaultColumnWidthMm = 30, DefaultRowHeightMm = 10 };

        var model = TableModel.CreateEmpty(2, 2, style);
        style.DefaultColumnWidthMm = 99;

        Assert.All(model.ColumnWidths, w => Assert.Equal(30, w));
        Assert.All(model.RowHeights, h => Assert.Equal(10, h));
        Assert.Equal(30, model.Style.DefaultColumnWidthMm);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void CreateEmpty_RejectsZeroSize(int rows, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TableModel.CreateEmpty(rows, columns));
    }

    [Fact]
    public void InsertAndRemove_RejectOutOfRange()
    {
        var model = TableModel.CreateEmpty(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => model.InsertRow(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.InsertColumn(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.RemoveRow(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.RemoveColumn(5));
    }

    [Fact]
    public void Json_KeepsStyleAndSkipsDefaults()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.Style.UnderlineMm = 0.3;
        model.SetText(0, 0, "a");

        var json = model.ToJson();
        var restored = TableModel.FromJson(json);

        Assert.Equal(0.3, restored.Style.UnderlineMm);
        Assert.DoesNotContain("\"Bold\"", json);
        Assert.DoesNotContain("\"FontName\":null", json);
    }
}
