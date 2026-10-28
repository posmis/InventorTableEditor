using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class CellTextBufferTests
{
    private static readonly TableStyle Style = new();

    private static CellTextBuffer Buffer(string text) => new(TableCell.FromText(text, italic: true), Style);

    [Fact]
    public void Insert_TypesAtCaretWithFormatOfPreviousChar()
    {
        var buffer = Buffer("ac");
        buffer.MoveTo(1, extend: false);

        buffer.Insert("b");

        Assert.Equal("abc", buffer.Text);
        Assert.Equal(2, buffer.Caret);
        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Italic);
    }

    [Fact]
    public void EmptyCell_TypesWithTableItalic()
    {
        var buffer = new CellTextBuffer(new TableCell(), Style);

        buffer.Insert("x");

        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Italic);
    }

    [Fact]
    public void Insert_ReplacesSelection()
    {
        var buffer = Buffer("hello");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(4, extend: true);

        buffer.Insert("EL");

        Assert.Equal("hELo", buffer.Text);
        Assert.False(buffer.HasSelection);
    }

    [Fact]
    public void BackspaceAndDelete()
    {
        var buffer = Buffer("abc");
        buffer.MoveTo(2, extend: false);

        buffer.Backspace();
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.Caret);

        buffer.Delete();
        Assert.Equal("a", buffer.Text);

        buffer.MoveTo(0, extend: false);
        buffer.Backspace();
        Assert.Equal("a", buffer.Text);
    }

    [Fact]
    public void ToggleScript_WithoutSelection_ChangesTypedTextOnly_AndResetReturnsToNormal()
    {
        var buffer = Buffer("D");
        buffer.MoveTo(1, extend: false);

        buffer.ToggleScript(ScriptPosition.Superscript);
        buffer.Insert("2");
        buffer.ResetScript();
        buffer.Insert("x");

        var runs = buffer.ToCell(new TableCell()).Runs;
        Assert.Equal(new[] { "D", "2", "x" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { ScriptPosition.Normal, ScriptPosition.Superscript, ScriptPosition.Normal }, runs.Select(r => r.Script));
    }

    [Fact]
    public void ToggleScript_OnSelectionTogglesBack()
    {
        var buffer = Buffer("D2");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(2, extend: true);

        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.Equal(ScriptPosition.Subscript, buffer.ToCell(new TableCell()).Runs[1].Script);

        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.Single(buffer.ToCell(new TableCell()).Runs);
    }

    [Fact]
    public void ToggleBold_MixedSelectionBecomesBold()
    {
        var buffer = Buffer("ab");
        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(1, extend: true);
        buffer.ToggleBold();

        buffer.SelectAll();
        buffer.ToggleBold();

        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Bold);
    }

    [Fact]
    public void SetFontSize_TableSizeStoredAsNull()
    {
        var buffer = Buffer("ab");
        buffer.SelectAll();

        buffer.SetFontSize(7);
        Assert.Equal(7, buffer.ToCell(new TableCell()).Runs.Single().FontSizeMm);

        buffer.SetFontSize(Style.FontSizeMm);
        Assert.Null(buffer.ToCell(new TableCell()).Runs.Single().FontSizeMm);
    }

    [Fact]
    public void UndoRedo()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Insert("b");
        buffer.Insert("c");

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.True(buffer.Redo());
        Assert.Equal("abc", buffer.Text);
    }

    [Fact]
    public void ToCell_KeepsAlignmentAndLineBreaks()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Insert("\nb");

        var cell = buffer.ToCell(new TableCell { HorizontalAlignment = HorizontalTextAlignment.Right });

        Assert.Equal("a\nb", cell.PlainText);
        Assert.Equal(HorizontalTextAlignment.Right, cell.HorizontalAlignment);
    }

    [Fact]
    public void WordNavigation()
    {
        var buffer = Buffer("ab  cd ef");

        Assert.Equal(4, buffer.WordRight(0));
        Assert.Equal(4, buffer.WordLeft(6));
        Assert.Equal(0, buffer.WordLeft(3));
    }
}
