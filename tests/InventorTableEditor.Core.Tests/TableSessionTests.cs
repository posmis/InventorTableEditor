using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TableSessionTests
{
    private static TableSession Session(int rows = 3, int columns = 3, int caret = 0) =>
        new(TableModel.CreateEmpty(rows, columns), () => caret);

    private static TableCell Text(string text) => TableCell.FromText(text, italic: false);

    // ---------- Переходы ----------

    [Fact]
    public void MoveTo_ClampsToTable()
    {
        var session = Session();

        session.MoveTo(10, -3);

        Assert.Equal((2, 0), (session.Row, session.Column));
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 1)]
    [InlineData(0, 2, 0, 1, 1, 0)]
    [InlineData(1, 0, 0, -1, 0, 2)]
    [InlineData(1, 1, 1, 0, 2, 1)]
    [InlineData(1, 1, -1, 0, 0, 1)]
    public void Neighbor_TabWrapsToNextRow(int row, int column, int rowStep, int columnStep, int expectedRow, int expectedColumn)
    {
        var session = Session();
        session.MoveTo(row, column);

        Assert.Equal((expectedRow, expectedColumn), session.Neighbor(rowStep, columnStep));
    }

    [Theory]
    [InlineData(2, 2, 0, 1)]
    [InlineData(0, 0, 0, -1)]
    [InlineData(0, 1, -1, 0)]
    [InlineData(2, 1, 1, 0)]
    public void Neighbor_NullOutsideTable(int row, int column, int rowStep, int columnStep)
    {
        var session = Session();
        session.MoveTo(row, column);

        Assert.Null(session.Neighbor(rowStep, columnStep));
    }

    // ---------- Выделение ----------

    [Fact]
    public void Selection_DragExtendsAndReturnsToSingleCell()
    {
        var session = Session();
        session.BeginSelection(0, 0);

        Assert.True(session.ExtendSelection(1, 2));
        Assert.Equal(new CellRange(0, 0, 1, 2), session.Range);
        Assert.False(session.ExtendSelection(1, 2));
        Assert.Equal(6, session.TargetCells.Count());

        Assert.True(session.ExtendSelection(0, 0));
        Assert.Null(session.Range);
        Assert.Equal(new[] { (session.Row, session.Column) }, session.TargetCells);
    }

    [Fact]
    public void Selection_ShiftClickFromCurrentCell()
    {
        var session = Session();
        session.MoveTo(2, 2);

        session.ExtendSelection(0, 1, fromCurrent: true);

        Assert.Equal(new CellRange(0, 1, 2, 2), session.Range);
    }

    [Theory]
    [InlineData(new[] { true, true }, false)]
    [InlineData(new[] { true, false }, true)]
    [InlineData(new bool[0], true)]
    public void ShouldTurnOn_LikeExcel(bool[] states, bool expected)
    {
        Assert.Equal(expected, TableSession.ShouldTurnOn(states.Select(s => (bool?)s)));
    }

    [Fact]
    public void ShouldTurnOn_IgnoresEmptyCells_AllEmptyTurnsOn()
    {
        Assert.False(TableSession.ShouldTurnOn(new bool?[] { true, null }));
        Assert.True(TableSession.ShouldTurnOn(new bool?[] { null, null }));
    }

    // ---------- Текст и отмена ----------

    [Fact]
    public void ReplaceCell_SameContentIsNotAStep()
    {
        var session = Session();

        Assert.False(session.ReplaceCell(0, 0, new TableCell(), EditKind.Deleting));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ReplaceCell_KeepsAlignment()
    {
        var session = Session();
        session.SetHorizontalAlignment(HorizontalTextAlignment.Right);

        session.ReplaceCell(0, 0, Text("a"), EditKind.Typing);

        Assert.Equal(HorizontalTextAlignment.Right, session.CurrentCell.HorizontalAlignment);
    }

    [Fact]
    public void TypingInOneCell_UndoesAsOneStep_UntilCellChanges()
    {
        var session = Session(caret: 2);
        session.ReplaceCell(0, 0, Text("a"), EditKind.Typing);
        session.ReplaceCell(0, 0, Text("ab"), EditKind.Typing);
        session.MoveTo(0, 1);
        session.ReplaceCell(0, 1, Text("c"), EditKind.Typing);

        Assert.Equal(2, session.Undo());
        Assert.Equal("ab", session.Model.GetText(0, 0));
        Assert.Equal("", session.Model.GetText(0, 1));

        session.Undo();
        Assert.Equal("", session.Model.GetText(0, 0));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_RestoresCurrentCellAndClearsSelection()
    {
        var session = Session();
        session.MoveTo(1, 1);
        session.ReplaceCell(1, 1, Text("x"), EditKind.Other);
        session.MoveTo(2, 2);
        session.BeginSelection(0, 0);
        session.ExtendSelection(1, 1);

        session.Undo();

        Assert.Equal((1, 1), (session.Row, session.Column));
        Assert.Null(session.Range);
        Assert.Equal("", session.Model.GetText(1, 1));

        session.Redo();
        Assert.Equal("x", session.Model.GetText(1, 1));
    }

    [Fact]
    public void Batch_IsOneStep()
    {
        var session = Session();

        session.Batch(() =>
        {
            session.ReplaceCell(0, 0, Text("a"), EditKind.Other);
            session.ReplaceCell(1, 1, Text("b"), EditKind.Other);
            session.SetHorizontalAlignment(HorizontalTextAlignment.Center);
        });
        session.Undo();

        Assert.Equal("", session.Model.GetText(0, 0));
        Assert.Equal("", session.Model.GetText(1, 1));
        Assert.Null(session.CurrentCell.HorizontalAlignment);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void UndoWithEmptyHistory_ReturnsNull()
    {
        Assert.Null(Session().Undo());
        Assert.Null(Session().Redo());
    }

    // ---------- Выравнивание ----------

    [Fact]
    public void Alignment_AppliesToSelectionOrCurrentCell()
    {
        var session = Session();
        session.BeginSelection(0, 0);
        session.ExtendSelection(1, 1);

        session.SetVerticalAlignment(VerticalTextAlignment.Top);

        Assert.Equal(VerticalTextAlignment.Top, session.Model.GetCell(1, 1).VerticalAlignment);
        Assert.Null(session.Model.GetCell(2, 2).VerticalAlignment);

        session.ClearSelection();
        session.MoveTo(2, 2);
        session.SetVerticalAlignment(VerticalTextAlignment.Bottom);
        Assert.Equal(VerticalTextAlignment.Bottom, session.Model.GetCell(2, 2).VerticalAlignment);
        Assert.Equal(VerticalTextAlignment.Top, session.Model.GetCell(1, 1).VerticalAlignment);
    }

    // ---------- Структура ----------

    [Fact]
    public void InsertColumnRight_MovesToNewColumn()
    {
        var session = Session(1, 2);
        session.ReplaceCell(0, 0, Text("a"), EditKind.Other);

        session.InsertColumnRight();

        Assert.Equal(3, session.Model.ColumnCount);
        Assert.Equal(1, session.Column);
        Assert.Equal("", session.CurrentCell.PlainText);
        Assert.Equal("a", session.Model.GetText(0, 0));
    }

    [Fact]
    public void InsertColumnLeft_KeepsIndexOnNewColumn()
    {
        var session = Session(1, 2);
        session.MoveTo(0, 1);
        session.ReplaceCell(0, 1, Text("b"), EditKind.Other);

        session.InsertColumnLeft();

        Assert.Equal(1, session.Column);
        Assert.Equal("", session.CurrentCell.PlainText);
        Assert.Equal("b", session.Model.GetText(0, 2));
    }

    [Fact]
    public void DeleteLastColumn_ClampsCurrentColumn()
    {
        var session = Session(1, 3);
        session.MoveTo(0, 2);

        session.DeleteColumn();

        Assert.Equal(2, session.Model.ColumnCount);
        Assert.Equal(1, session.Column);
    }

    [Fact]
    public void DeleteOnlyRowOrColumn_ThrowsWithoutUndoStep()
    {
        var session = Session(1, 1);

        Assert.Throws<InvalidOperationException>(() => session.DeleteRow());
        Assert.Throws<InvalidOperationException>(() => session.DeleteColumn());
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void InsertRowAbove_OnHeaderGoesBelowHeader()
    {
        var session = Session(2, 1);
        session.SetHeader(true);

        session.InsertRowAbove();

        Assert.True(session.Model.IsHeaderRow(0));
        Assert.Equal(1, session.Row);
        Assert.Equal(4, session.Model.RowCount);
    }

    [Fact]
    public void InsertRowBelow_MovesToNewRow()
    {
        var session = Session(2, 1);

        session.InsertRowBelow();

        Assert.Equal(1, session.Row);
        Assert.Equal(3, session.Model.RowCount);
    }

    [Fact]
    public void SetHeader_MovesToFirstRowAndUndoes()
    {
        var session = Session(2, 1);
        session.MoveTo(1, 0);

        session.SetHeader(true);
        Assert.Equal(0, session.Row);
        Assert.True(session.Model.HasHeader);

        session.SetHeader(true);
        session.Undo();
        Assert.False(session.Model.HasHeader);
        Assert.Equal(2, session.Model.RowCount);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void StructureChange_ClearsSelection()
    {
        var session = Session();
        session.BeginSelection(0, 0);
        session.ExtendSelection(1, 1);

        session.InsertRowBelow();

        Assert.Null(session.Range);
    }

    // ---------- Размеры ----------

    [Fact]
    public void SetSizes_RecordOnlyWhenChanged()
    {
        var session = Session();
        session.MoveTo(1, 2);

        Assert.False(session.SetColumnWidth(20));
        Assert.True(session.SetColumnWidth(35.5));
        Assert.True(session.SetRowHeight(12));

        Assert.Equal(35.5, session.Model.ColumnWidths[2]);
        Assert.Equal(12, session.Model.RowHeights[1]);

        session.Undo();
        session.Undo();
        Assert.Equal(20, session.Model.ColumnWidths[2]);
        Assert.False(session.CanUndo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.9)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    public void SetSizes_RejectOutOfRange(double value)
    {
        var session = Session();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetColumnWidth(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetRowHeight(value));
    }

    [Fact]
    public void DragColumnBorder_ChangesOnlyThatColumn_OneUndoStep()
    {
        var session = Session(1, 3);
        session.MoveTo(0, 2);

        // Начало перетаскивания: шаг отмены один раз, дальше — без записи.
        session.Record();
        session.SetColumnWidth(0, 21, record: false);
        session.SetColumnWidth(0, 25, record: false);

        Assert.Equal(new[] { 25.0, 20, 20 }, session.Model.ColumnWidths);
        Assert.Equal(2, session.Column);

        session.Undo();
        Assert.Equal(new[] { 20.0, 20, 20 }, session.Model.ColumnWidths);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SetColumnWidth_RejectsWrongColumn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Session(1, 2).SetColumnWidth(2, 10));
    }
}
