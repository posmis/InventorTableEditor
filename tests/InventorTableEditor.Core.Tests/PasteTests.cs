using InventorTableEditor.Core.Clipboard;
using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class PasteTests
{
    private static TableSession Session(int rows = 3, int columns = 3) =>
        new(TableModel.CreateEmpty(rows, columns), () => 0);

    private static List<List<TableCell>> Cells(string tsv) => TableClipboard.FromTsv(tsv, italic: true);

    // ---------- Вставка ячеек в таблицу ----------

    [Fact]
    public void PasteCells_FromCurrentCellAndSelectsResult()
    {
        var session = Session();
        session.MoveTo(1, 1);

        Assert.True(session.PasteCells(Cells("a\tb\r\nc\td")));

        Assert.Equal("a", session.Model.GetText(1, 1));
        Assert.Equal("d", session.Model.GetText(2, 2));
        Assert.Equal("", session.Model.GetText(0, 0));
        Assert.Equal(new CellRange(1, 1, 2, 2), session.Range);
        Assert.Equal((1, 1), (session.Row, session.Column));
    }

    [Fact]
    public void PasteCells_StartsAtSelectionTopLeft()
    {
        var session = Session();
        session.MoveTo(2, 2);
        session.BeginSelection(2, 2);
        session.ExtendSelection(1, 1);

        session.PasteCells(Cells("x"));

        // Одна ячейка в буфере — заполняется всё выделение, как в Excel.
        Assert.All(new CellRange(1, 1, 2, 2).Cells(), c => Assert.Equal("x", session.Model.GetText(c.Row, c.Column)));
        Assert.Equal("", session.Model.GetText(0, 0));
    }

    [Fact]
    public void PasteCells_AddsRowsAndColumnsWhenNeeded()
    {
        var session = Session(2, 2);
        session.MoveTo(1, 1);

        session.PasteCells(Cells("a\tb\tc\r\nd\te\tf"));

        Assert.Equal(3, session.Model.RowCount);
        Assert.Equal(4, session.Model.ColumnCount);
        Assert.Equal("f", session.Model.GetText(2, 3));
        Assert.Equal(session.Model.Style.DefaultColumnWidthMm, session.Model.ColumnWidths[3]);
    }

    [Fact]
    public void PasteCells_KeepsAlignmentWrapAndMerges()
    {
        var session = Session();
        session.Model.GetCell(0, 0).HorizontalAlignment = HorizontalTextAlignment.Right;
        session.Model.GetCell(0, 0).Wrap = true;
        session.Model.Merge(new CellRange(0, 1, 1, 1));

        session.PasteCells(Cells("a\tb\r\nc\td"));

        Assert.Equal(HorizontalTextAlignment.Right, session.Model.GetCell(0, 0).HorizontalAlignment);
        Assert.True(session.Model.GetCell(0, 0).Wrap);
        Assert.Equal("b", session.Model.GetText(0, 1));
        Assert.Equal(new CellRange(0, 1, 1, 1), session.Model.GetMerge(0, 1));
        // Ячейка под объединением закрыта — «d» пропускается.
        Assert.Equal("", session.Model.GetText(1, 1));
        Assert.Equal("c", session.Model.GetText(1, 0));
    }

    [Fact]
    public void PasteCells_IsOneUndoStep()
    {
        var session = Session(1, 1);
        session.Model.SetText(0, 0, "old");

        session.PasteCells(Cells("a\tb\r\nc\td"));
        session.Undo();

        Assert.Equal(1, session.Model.RowCount);
        Assert.Equal(1, session.Model.ColumnCount);
        Assert.Equal("old", session.Model.GetText(0, 0));
    }

    [Fact]
    public void PasteCells_EmptyClipboardDoesNothing()
    {
        var session = Session();

        Assert.False(session.PasteCells(new List<List<TableCell>>()));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void PasteCells_RaggedRowsFillMissingWithEmpty()
    {
        var session = Session();
        session.Model.SetText(1, 1, "keep?");

        session.PasteCells(new List<List<TableCell>>
        {
            new() { TableCell.FromText("a", true), TableCell.FromText("b", true) },
            new() { TableCell.FromText("c", true) },
        });

        Assert.Equal("", session.Model.GetText(1, 1));
        Assert.Equal(new CellRange(0, 0, 1, 1), session.Range);
    }

    [Fact]
    public void PasteCells_SingleCellWithoutSelectionHasNoRange()
    {
        var session = Session();
        session.MoveTo(2, 0);

        session.PasteCells(Cells("z"));

        Assert.Equal("z", session.Model.GetText(2, 0));
        Assert.Null(session.Range);
    }

    // ---------- Фрагменты текста внутри ячейки ----------

    [Fact]
    public void SelectedRuns_KeepFormats()
    {
        var cell = new TableCell();
        cell.Runs.Add(new TextRun("R", italic: true));
        cell.Runs.Add(new TextRun("12", italic: true) { Script = ScriptPosition.Subscript });
        var buffer = new CellTextBuffer(cell, new TableStyle());
        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(2, extend: true);

        var runs = buffer.SelectedRuns();

        Assert.Equal(new[] { "R", "1" }, runs.Select(r => r.Text));
        Assert.Equal(ScriptPosition.Subscript, runs[1].Script);
    }

    [Fact]
    public void InsertRuns_ReplacesSelectionKeepsFormatsAndInheritsFont()
    {
        var cell = new TableCell();
        cell.Runs.Add(new TextRun("ab", italic: true) { FontName = "Arial" });
        var buffer = new CellTextBuffer(cell, new TableStyle());
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(2, extend: true);

        buffer.InsertRuns(new[]
        {
            new TextRun("x"),
            new TextRun("2") { Script = ScriptPosition.Superscript },
        });

        Assert.Equal("ax2", buffer.Text);
        Assert.Equal(3, buffer.Caret);
        Assert.False(buffer.HasSelection);
        var runs = buffer.ToCell(new TableCell()).Runs;
        Assert.Equal(new[] { "a", "x", "2" }, runs.Select(r => r.Text));
        Assert.All(runs, r => Assert.Equal("Arial", r.FontName));
        Assert.Equal(ScriptPosition.Superscript, runs[2].Script);
        Assert.Equal(ScriptPosition.Superscript, buffer.TypingFormat.Script);
    }

    [Fact]
    public void InsertRuns_NormalizesLineBreaks()
    {
        var buffer = new CellTextBuffer(new TableCell(), new TableStyle());

        buffer.InsertRuns(new[] { new TextRun("a\r\nb\rc") });

        Assert.Equal("a\nb\nc", buffer.Text);
    }
}
