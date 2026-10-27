using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TableModelTests
{
    [Fact]
    public void CreateEmpty_UsesStyleDefaults()
    {
        var model = TableModel.CreateEmpty(3, 4);

        Assert.Equal(3, model.RowCount);
        Assert.Equal(4, model.ColumnCount);
        Assert.All(model.ColumnWidths, w => Assert.Equal(20.0, w));
        Assert.All(model.RowHeights, h => Assert.Equal(8.0, h));
        Assert.All(model.Cells, row => Assert.Equal(4, row.Count));
    }

    [Fact]
    public void InsertAndRemoveColumn_ShiftsCells()
    {
        var model = TableModel.CreateEmpty(1, 2);
        model.SetText(0, 0, "A");
        model.SetText(0, 1, "B");

        model.InsertColumn(1);
        Assert.Equal(new[] { "A", "", "B" }, model.Cells[0].Select(c => c.PlainText));

        model.RemoveColumn(0);
        Assert.Equal(new[] { "", "B" }, model.Cells[0].Select(c => c.PlainText));
        Assert.Equal(2, model.ColumnWidths.Count);
    }

    [Fact]
    public void RemoveLastRowOrColumn_Throws()
    {
        var model = TableModel.CreateEmpty(1, 1);

        Assert.Throws<InvalidOperationException>(() => model.RemoveRow(0));
        Assert.Throws<InvalidOperationException>(() => model.RemoveColumn(0));
    }

    [Fact]
    public void SetHeader_AddsAndRemovesFirstRow()
    {
        var model = TableModel.CreateEmpty(2, 2);
        model.SetText(0, 0, "data");

        model.SetHeader(true);
        Assert.Equal(3, model.RowCount);
        Assert.True(model.IsHeaderRow(0));
        Assert.Equal("data", model.GetText(1, 0));

        model.SetHeader(false);
        Assert.Equal(2, model.RowCount);
        Assert.Equal("data", model.GetText(0, 0));
    }

    [Fact]
    public void SetText_NormalizesLineBreaks()
    {
        var model = TableModel.CreateEmpty(1, 1);

        model.SetText(0, 0, "a\r\nb\rc");

        Assert.Equal("a\nb\nc", model.GetText(0, 0));
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var model = TableModel.CreateEmpty(2, 2);
        model.SetHeader(true);
        model.SetText(1, 1, "R1\nм²");
        model.ColumnWidths[0] = 35.5;
        model.Style.Italic = false;

        var restored = TableModel.FromJson(model.ToJson());

        Assert.Equal(model.ToJson(), restored.ToJson());
        Assert.True(restored.HasHeader);
        Assert.Equal("R1\nм²", restored.GetText(1, 1));
    }

    [Fact]
    public void Json_RoundTripKeepsFormatting()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell
        {
            Runs =
            {
                new TextRun("м", italic: true) { Bold = true },
                new TextRun("2") { Script = ScriptPosition.Superscript, Underline = true },
            },
        });

        var runs = TableModel.FromJson(model.ToJson()).GetCell(0, 0).Runs;

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Bold && runs[0].Italic);
        Assert.Equal(ScriptPosition.Superscript, runs[1].Script);
        Assert.True(runs[1].Underline);
    }

    [Fact]
    public void FromJson_ReadsVersion1Tables()
    {
        var json = """{"Version":1,"Style":{"Italic":true},"HasHeader":false,"ColumnWidths":[20],"RowHeights":[8],"Cells":[["a\nb"]]}""";

        var model = TableModel.FromJson(json);

        Assert.Equal(TableModel.CurrentVersion, model.Version);
        Assert.Equal("a\nb", model.GetText(0, 0));
        Assert.True(model.GetCell(0, 0).Runs.Single().Italic);
    }

    [Fact]
    public void SetCell_MergesRunsWithSameFormat()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a"), new TextRun(""), new TextRun("b"), new TextRun("c") { Bold = true } } });

        var runs = model.GetCell(0, 0).Runs;

        Assert.Equal(new[] { "ab", "c" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void FromJson_RejectsInconsistentData()
    {
        var json = """{"ColumnWidths":[20,20],"RowHeights":[8],"Cells":[[{"Runs":[]}]]}""";

        Assert.Throws<InvalidDataException>(() => TableModel.FromJson(json));
    }
}
