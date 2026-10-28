using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class TableHistoryTests
{
    private static readonly TableCursor Cursor = new(0, 0, 0);

    [Fact]
    public void UndoRestoresTableBeforeAction_RedoReturnsIt()
    {
        var history = new TableHistory();
        var model = TableModel.CreateEmpty(1, 1);

        history.Record(model, new TableCursor(0, 0, 0));
        model.SetText(0, 0, "a");

        var undone = history.Undo(model, new TableCursor(0, 0, 1))!.Value;
        Assert.Equal("", undone.Model.GetText(0, 0));
        Assert.Equal(new TableCursor(0, 0, 0), undone.Cursor);

        var redone = history.Redo(undone.Model, undone.Cursor)!.Value;
        Assert.Equal("a", redone.Model.GetText(0, 0));
        Assert.Equal(new TableCursor(0, 0, 1), redone.Cursor);
    }

    [Fact]
    public void ActionsInDifferentCells_AreSeparateSteps()
    {
        var history = new TableHistory();
        var model = TableModel.CreateEmpty(1, 2);

        history.Record(model, Cursor);
        model.SetText(0, 0, "a");
        history.Record(model, new TableCursor(0, 1, 0));
        model.SetText(0, 1, "b");

        var first = history.Undo(model, Cursor)!.Value;
        Assert.Equal("a", first.Model.GetText(0, 0));
        Assert.Equal("", first.Model.GetText(0, 1));

        var second = history.Undo(first.Model, Cursor)!.Value;
        Assert.Equal("", second.Model.GetText(0, 0));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void TypingWithSameMergeKey_IsOneStep_UntilBreak()
    {
        var history = new TableHistory();
        var model = TableModel.CreateEmpty(1, 1);

        history.Record(model, Cursor, "type:0:0");
        model.SetText(0, 0, "a");
        history.Record(model, Cursor, "type:0:0");
        model.SetText(0, 0, "ab");
        history.BreakMerge();
        history.Record(model, Cursor, "type:0:0");
        model.SetText(0, 0, "ab c");

        Assert.Equal("ab", history.Undo(model, Cursor)!.Value.Model.GetText(0, 0));
        model.SetText(0, 0, "ab");
        Assert.Equal("", history.Undo(model, Cursor)!.Value.Model.GetText(0, 0));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void NewActionClearsRedo_AndEmptyHistoryReturnsNull()
    {
        var history = new TableHistory();
        var model = TableModel.CreateEmpty(1, 1);
        Assert.Null(history.Undo(model, Cursor));

        history.Record(model, Cursor);
        var undone = history.Undo(model, Cursor)!.Value;
        Assert.True(history.CanRedo);

        history.Record(undone.Model, Cursor);
        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(undone.Model, Cursor));
    }

    [Fact]
    public void UndoBreaksMerge()
    {
        var history = new TableHistory();
        var model = TableModel.CreateEmpty(1, 1);

        history.Record(model, Cursor, "type:0:0");
        model.SetText(0, 0, "a");
        var undone = history.Undo(model, Cursor)!.Value.Model;
        history.Redo(undone, Cursor);

        // После отмены/повтора набор начинает новый шаг.
        history.Record(model, Cursor, "type:0:0");
        Assert.Equal(2, CountUndo(history, model));
    }

    private static int CountUndo(TableHistory history, TableModel model)
    {
        var count = 0;
        while (history.Undo(model, Cursor) is { } step)
        {
            model = step.Model;
            count++;
        }

        return count;
    }
}
