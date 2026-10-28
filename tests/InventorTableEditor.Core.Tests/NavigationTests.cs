using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

/// <summary>Переходы между ячейками (Tab, Shift+Tab, стрелки) с объединёнными ячейками.</summary>
public class NavigationTests
{
    private static TableSession Session(int rows, int columns, params CellRange[] merges)
    {
        var model = TableModel.CreateEmpty(rows, columns);
        foreach (var merge in merges)
            model.Merge(merge);
        return new TableSession(model, () => 0);
    }

    [Fact]
    public void Tab_FromFullWidthMergeSpanningRows_LeavesIt()
    {
        // Объединены две верхние строки целиком: раньше Tab возвращал в ту же ячейку.
        var session = Session(3, 3, new CellRange(0, 0, 1, 2));

        Assert.Equal((2, 0), session.Neighbor(0, 1));
    }

    [Fact]
    public void ShiftTab_IntoFullWidthMerge_GoesToItsAnchor()
    {
        var session = Session(3, 3, new CellRange(0, 0, 1, 2));
        session.MoveTo(2, 0);

        Assert.Equal((0, 0), session.Neighbor(0, -1));
    }

    [Fact]
    public void ShiftTab_FromFullWidthMergeAtBottom_GoesAbove()
    {
        var session = Session(3, 2, new CellRange(1, 0, 2, 1));
        session.MoveTo(1, 0);

        Assert.Equal((0, 1), session.Neighbor(0, -1));
    }

    [Fact]
    public void Tab_WholeTableMerged_HasNowhereToGo()
    {
        var session = Session(2, 2, new CellRange(0, 0, 1, 1));

        Assert.Null(session.Neighbor(0, 1));
        Assert.Null(session.Neighbor(0, -1));
        Assert.Null(session.Neighbor(1, 0));
        Assert.Null(session.Neighbor(-1, 0));
    }

    [Fact]
    public void Tab_SkipsCellsCoveredByMergeToTheRight()
    {
        var session = Session(2, 4, new CellRange(0, 1, 0, 2));

        Assert.Equal((0, 1), session.Neighbor(0, 1));
        session.MoveTo(0, 1);
        Assert.Equal((0, 3), session.Neighbor(0, 1));
    }

    [Fact]
    public void Tab_WrapsToNextRowBelowMergeStartingMidRow()
    {
        // Объединение в правых столбцах двух строк: после него — первая ячейка второй строки.
        var session = Session(3, 3, new CellRange(0, 1, 1, 2));
        session.MoveTo(0, 1);

        Assert.Equal((1, 0), session.Neighbor(0, 1));
    }

    [Fact]
    public void Down_FromTallMerge_GoesBelowIt()
    {
        var session = Session(4, 2, new CellRange(0, 0, 2, 0));

        Assert.Equal((3, 0), session.Neighbor(1, 0));
    }

    [Fact]
    public void Up_IntoMerge_GoesToAnchor()
    {
        var session = Session(3, 3, new CellRange(0, 0, 1, 2));
        session.MoveTo(2, 2);

        Assert.Equal((0, 0), session.Neighbor(-1, 0));
    }

    [Fact]
    public void EdgesOfTable_ReturnNull()
    {
        var session = Session(2, 2);

        Assert.Null(session.Neighbor(-1, 0));
        Assert.Null(session.Neighbor(0, -1));
        session.MoveTo(1, 1);
        Assert.Null(session.Neighbor(1, 0));
        Assert.Null(session.Neighbor(0, 1));
    }

    [Fact]
    public void MoveTo_CoveredCell_GoesToAnchor()
    {
        var session = Session(3, 3, new CellRange(1, 1, 2, 2));

        session.MoveTo(2, 2);

        Assert.Equal((1, 1), (session.Row, session.Column));
    }
}
