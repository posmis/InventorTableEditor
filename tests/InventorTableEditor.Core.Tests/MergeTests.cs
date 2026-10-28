using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class MergeTests
{
    private static TableModel Table(int rows = 3, int columns = 3)
    {
        var model = TableModel.CreateEmpty(rows, columns);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
                model.SetText(r, c, $"{r}{c}");
        }

        return model;
    }

    // ---------- Модель ----------

    [Fact]
    public void Merge_JoinsTextLikeWord_AndCoversCells()
    {
        var model = Table();

        model.Merge(new CellRange(0, 0, 1, 1));

        Assert.Equal("00\n01\n10\n11", model.GetText(0, 0));
        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, model.Merges());
        Assert.True(model.IsCovered(1, 1));
        Assert.False(model.IsCovered(0, 0));
        Assert.Equal((0, 0), model.AnchorOf(1, 0));
        Assert.Equal("", model.GetText(0, 1));
        Assert.Equal("22", model.GetText(2, 2));
    }

    [Fact]
    public void Merge_SkipsEmptyCellsAndKeepsAnchorAlignment()
    {
        var model = TableModel.CreateEmpty(1, 3);
        model.SetText(0, 2, "c");
        model.GetCell(0, 0).HorizontalAlignment = HorizontalTextAlignment.Center;

        model.Merge(new CellRange(0, 0, 0, 2));

        Assert.Equal("c", model.GetText(0, 0));
        Assert.Equal(HorizontalTextAlignment.Center, model.GetCell(0, 0).HorizontalAlignment);
    }

    [Fact]
    public void Merge_KeepsFormatOfEachCell()
    {
        var model = TableModel.CreateEmpty(1, 2);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a") { Bold = true } } });
        model.SetCell(0, 1, new TableCell { Runs = { new TextRun("b") { Script = ScriptPosition.Subscript } } });

        model.Merge(new CellRange(0, 0, 0, 1));

        var runs = model.GetCell(0, 0).Runs;
        Assert.Equal("a\nb", model.GetText(0, 0));
        Assert.True(runs[0].Bold);
        Assert.Equal(ScriptPosition.Subscript, runs[^1].Script);
    }

    [Fact]
    public void Merge_ExpandsToTouchedMerges()
    {
        var model = Table();
        model.Merge(new CellRange(0, 1, 1, 1));

        // Выделение задевает только низ объединения — объединяется весь прямоугольник.
        model.Merge(new CellRange(1, 0, 1, 1));

        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, model.Merges());
    }

    [Fact]
    public void Merge_SingleCellIsNoOp()
    {
        var model = Table();

        model.Merge(new CellRange(1, 1, 1, 1));

        Assert.Empty(model.Merges());
        Assert.Equal("11", model.GetText(1, 1));
    }

    [Fact]
    public void Split_FromAnyCoveredCell_KeepsTextInAnchor()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 1, 2));

        model.Split(1, 2);

        Assert.Empty(model.Merges());
        Assert.StartsWith("00", model.GetText(0, 0));
        Assert.Equal("", model.GetText(1, 2));
    }

    [Fact]
    public void ExpandToMerges_IsTransitive()
    {
        var model = Table(3, 4);
        model.Merge(new CellRange(0, 1, 1, 1));
        model.Merge(new CellRange(1, 2, 2, 2));

        // Задели (0,1)-(1,1) → прямоугольник дошёл до строки 1 и задел (1,2)-(2,2).
        var range = model.ExpandToMerges(new CellRange(0, 0, 0, 2));

        Assert.Equal(new CellRange(0, 0, 2, 2), range);
    }

    // ---------- Вставка и удаление строк и столбцов ----------

    [Fact]
    public void InsertRowInsideMerge_ExtendsIt_AboveMovesIt()
    {
        var model = Table();
        model.Merge(new CellRange(1, 0, 2, 0));

        model.InsertRow(2);
        Assert.Equal(new[] { new CellRange(1, 0, 3, 0) }, model.Merges());

        model.InsertRow(0);
        Assert.Equal(new[] { new CellRange(2, 0, 4, 0) }, model.Merges());
        Assert.StartsWith("10", model.GetText(2, 0));
    }

    [Fact]
    public void InsertColumnInsideMerge_ExtendsIt_AtRightEdgeDoesNot()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 0, 1));

        model.InsertColumn(1);
        Assert.Equal(new[] { new CellRange(0, 0, 0, 2) }, model.Merges());

        model.InsertColumn(3);
        Assert.Equal(new[] { new CellRange(0, 0, 0, 2) }, model.Merges());
    }

    [Fact]
    public void RemoveAnchorRow_MovesTextToNextRowOfMerge()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 1, 0));

        model.RemoveRow(0);

        Assert.Empty(model.Merges());
        Assert.Equal("00\n10", model.GetText(0, 0));
    }

    [Fact]
    public void RemoveColumnInsideMerge_ShrinksIt()
    {
        var model = Table(2, 4);
        model.Merge(new CellRange(0, 0, 1, 2));

        model.RemoveColumn(1);

        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, model.Merges());
        Assert.StartsWith("00", model.GetText(0, 0));
    }

    [Fact]
    public void RemoveAnchorColumn_MovesTextRight()
    {
        var model = Table(1, 3);
        model.Merge(new CellRange(0, 0, 0, 2));

        model.RemoveColumn(0);

        Assert.Equal(new[] { new CellRange(0, 0, 0, 1) }, model.Merges());
        Assert.Equal("00\n01\n02", model.GetText(0, 0));
    }

    [Fact]
    public void SetHeader_ShiftsMergesDown()
    {
        var model = Table(2, 2);
        model.Merge(new CellRange(0, 0, 1, 0));

        model.SetHeader(true);

        Assert.Equal(new[] { new CellRange(1, 0, 2, 0) }, model.Merges());
    }

    // ---------- JSON ----------

    [Fact]
    public void Json_RoundTripKeepsMerges()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 1, 1));

        var restored = TableModel.FromJson(model.ToJson());

        Assert.Equal(model.Merges(), restored.Merges());
        Assert.DoesNotContain("PlainText", model.ToJson());
        Assert.DoesNotContain("IsEmpty", model.ToJson());
    }

    [Theory]
    [InlineData("""{"ExtraRows":5,"Runs":[]}""")]
    [InlineData("""{"ExtraColumns":-1,"Runs":[]}""")]
    public void FromJson_RepairsBrokenMerges(string cellJson)
    {
        var json = $$"""{"ColumnWidths":[20,20],"RowHeights":[8,8],"Cells":[[{{cellJson}},{"Runs":[]}],[{"Runs":[]},{"Runs":[]}]]}""";

        Assert.Empty(TableModel.FromJson(json).Merges());
    }

    [Fact]
    public void FromJson_RepairsOverlappingMerges()
    {
        var json = """{"ColumnWidths":[20,20],"RowHeights":[8,8],"Cells":[[{"ExtraRows":1,"Runs":[]},{"Runs":[]}],[{"ExtraColumns":1,"Runs":[]},{"Runs":[]}]]}""";

        Assert.Equal(new[] { new CellRange(0, 0, 1, 0) }, TableModel.FromJson(json).Merges());
    }

    // ---------- Раскладка ----------

    [Fact]
    public void Layout_NoLinesInsideMerge()
    {
        var model = TableModel.CreateEmpty(2, 2);
        model.Merge(new CellRange(0, 0, 1, 0));

        var layout = TableLayout.Build(model);

        // Рамка + вертикаль между столбцами (во всю высоту) + горизонталь только в правом столбце.
        Assert.Equal(6, layout.Lines.Count);
        var horizontal = layout.Lines.Single(l => l.Y1 == 10 && l.Y2 == 10);
        Assert.Equal((20.0, 40.0), (horizontal.X1, horizontal.X2));
        Assert.Single(layout.Lines, l => l.X1 == 20 && l.X2 == 20 && l.Y1 == 0 && l.Y2 == 20);
    }

    [Fact]
    public void Layout_MergedTextUsesWholeArea()
    {
        var model = TableModel.CreateEmpty(2, 3);
        model.Merge(new CellRange(0, 0, 1, 1));
        model.SetText(0, 0, "A");

        var text = TableLayout.Build(model).Texts.Single();

        Assert.Equal((0.0, 0.0, 40.0, 20.0), (text.Left, text.Top, text.Width, text.Height));
        // По центру по вертикали всей объединённой ячейки: (20 − 5) / 2 + 5.
        Assert.Equal(12.5, text.Lines.Single().BaselineMm, 6);
    }

    [Fact]
    public void Layout_TallMergedTextGrowsBottomRow()
    {
        var model = TableModel.CreateEmpty(2, 1);
        model.Merge(new CellRange(0, 0, 1, 0));
        model.SetText(0, 0, "1\n2\n3");

        var layout = TableLayout.Build(model);

        // Нужно 2 + 5 + 2 × 8 = 23 мм, есть 20: нижняя строка +3.
        Assert.Equal(new[] { 0.0, 10, 23 }, layout.RowEdges);
    }

    [Fact]
    public void Layout_MergedWidthUsedForWrap()
    {
        var model = TableModel.CreateEmpty(1, 2);
        // «aa aa» = 25 мм: в 18 мм не помещается, в 38 мм — помещается.
        model.SetText(0, 0, "aa aa");
        var narrow = TableLayout.Build(model, FixedMetrics.Instance).Texts.Single(t => t.Column == 0).Lines.Count;

        model.Merge(new CellRange(0, 0, 0, 1));
        var wide = TableLayout.Build(model, FixedMetrics.Instance).Texts.Single().Lines.Count;

        Assert.True(narrow > wide);
    }

    [Fact]
    public void Layout_CoveredCellsHaveNoText()
    {
        var model = TableModel.CreateEmpty(2, 2);
        model.Merge(new CellRange(0, 0, 0, 1));

        var texts = TableLayout.Build(model, includeEmptyCells: true).Texts;

        Assert.Equal(3, texts.Count);
        Assert.DoesNotContain(texts, t => t.Row == 0 && t.Column == 1);
    }

    // ---------- Сеанс ----------

    [Fact]
    public void Session_MergeSelection_UndoRestores()
    {
        var session = new TableSession(Table());
        session.BeginSelection(0, 0);
        session.ExtendSelection(1, 1);

        Assert.True(session.CanMerge);
        Assert.True(session.MergeSelection());
        Assert.Equal((0, 0), (session.Row, session.Column));
        Assert.True(session.CanSplit);
        Assert.Null(session.Range);

        session.Undo();
        Assert.Empty(session.Model.Merges());
        Assert.Equal("11", session.Model.GetText(1, 1));
    }

    [Fact]
    public void Session_SplitCurrent()
    {
        var session = new TableSession(Table());
        session.Model.Merge(new CellRange(0, 0, 0, 1));
        session.MoveTo(0, 1);

        Assert.Equal((0, 0), (session.Row, session.Column));
        Assert.True(session.SplitCurrent());
        Assert.False(session.CanSplit);
        Assert.False(session.SplitCurrent());
    }

    [Fact]
    public void Session_MergeWithoutSelection_DoesNothing()
    {
        var session = new TableSession(Table());

        Assert.False(session.CanMerge);
        Assert.False(session.MergeSelection());
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Session_NeighborSkipsCoveredCells()
    {
        var session = new TableSession(Table());
        session.Model.Merge(new CellRange(0, 0, 1, 1));

        session.MoveTo(0, 0);
        Assert.Equal((0, 2), session.Neighbor(0, 1));
        Assert.Equal((2, 0), session.Neighbor(1, 0));

        session.MoveTo(0, 2);
        Assert.Equal((0, 0), session.Neighbor(0, -1));
        session.MoveTo(2, 1);
        Assert.Equal((0, 0), session.Neighbor(-1, 0));
    }

    [Fact]
    public void Session_SelectionInsideOneMergeIsNotARange()
    {
        var session = new TableSession(Table());
        session.Model.Merge(new CellRange(0, 0, 1, 1));

        session.BeginSelection(0, 0);
        Assert.False(session.ExtendSelection(1, 1));
        Assert.Null(session.Range);

        Assert.True(session.ExtendSelection(1, 2));
        Assert.Equal(new CellRange(0, 0, 1, 2), session.Range);
        // Объединённая ячейка — одна цель для выравнивания.
        Assert.Equal(3, session.TargetCells.Count());
    }

    [Fact]
    public void Session_InsertRightAndBelowGoPastMerge()
    {
        var session = new TableSession(Table());
        session.Model.Merge(new CellRange(0, 0, 1, 1));

        session.InsertColumnRight();
        Assert.Equal((0, 2), (session.Row, session.Column));
        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, session.Model.Merges());

        session.MoveTo(0, 0);
        session.InsertRowBelow();
        Assert.Equal((2, 0), (session.Row, session.Column));
        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, session.Model.Merges());
    }

    [Fact]
    public void SetTextAndSetCell_KeepMerge()
    {
        var model = Table();
        model.Merge(new CellRange(0, 0, 1, 1));

        model.SetText(0, 0, "x");
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("y") } });

        Assert.Equal(new[] { new CellRange(0, 0, 1, 1) }, model.Merges());
        Assert.Equal("y", model.GetText(0, 0));
    }

    [Fact]
    public void Session_TypingInMergedCellKeepsMerge()
    {
        var session = new TableSession(Table());
        session.Model.Merge(new CellRange(0, 0, 0, 2));

        session.ReplaceCell(0, 0, TableCell.FromText("new", italic: false), EditKind.Typing);

        Assert.Equal(new[] { new CellRange(0, 0, 0, 2) }, session.Model.Merges());
    }
}
