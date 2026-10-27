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
        Assert.Equal(new[] { "A", "", "B" }, model.Cells[0]);

        model.RemoveColumn(0);
        Assert.Equal(new[] { "", "B" }, model.Cells[0]);
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
    public void FromJson_RejectsInconsistentData()
    {
        var json = """{"ColumnWidths":[20,20],"RowHeights":[8],"Cells":[["a"]]}""";

        Assert.Throws<InvalidDataException>(() => TableModel.FromJson(json));
    }
}
